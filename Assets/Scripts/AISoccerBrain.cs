using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fase 5 — Lógica de decisão da IA (portada do futebol.c → C# puro).
///
/// SEM dependência de render/MonoBehaviour: só matemática de decisão.
/// O MatchManager chama estas funções e o SoccerPlayer executa o resultado.
///
/// Comportamentos portados:
///   - Posições "home" do 4-3-3 (calculadas por papel).
///   - "Defensor mais próximo persegue a bola; o resto forma barreira" via
///     blend ponderado entre home, bola e gol próprio.
///   - Dono da bola decide: chutar (se em alcance e mirando o gol), passar
///     (companheiro mais à frente com linha livre) ou driblar rumo ao gol.
/// </summary>
public static class AISoccerBrain
{
    // -------------------------------------------------------------------------
    // Decisão do JOGADOR SEM A BOLA
    // -------------------------------------------------------------------------
    /// <summary>
    /// Calcula o alvo de movimentação de um jogador sem a bola.
    ///
    /// Filosofia (corrige o "alinhamento/agrupamento"): o time se desloca como um
    /// BLOCO COESO, não como indivíduos que correm todos para a bola. Cada jogador
    /// permanece na sua LANE (posição home por papel) e o bloco inteiro:
    ///   - desliza lateralmente (X) em direção ao lado da bola;
    ///   - sobe (atacando) ou recua (defendendo) em Z conforme a posse e o papel.
    /// Por cima disso aplicamos SEPARAÇÃO entre companheiros para nunca empilhar.
    ///
    /// Só o perseguidor designado vai de fato à bola.
    /// </summary>
    /// <param name="self">Jogador.</param>
    /// <param name="ballPos">Posição da bola.</param>
    /// <param name="ownGoalCenter">Gol do próprio time (para postura defensiva).</param>
    /// <param name="fieldCenter">Centro do campo (referência do deslocamento do bloco).</param>
    /// <param name="teamHasBall">Meu time está com a posse? (define subir/recuar).</param>
    /// <param name="isDesignatedChaser">Sou o perseguidor designado?</param>
    /// <param name="teammates">Companheiros (para separação anti-agrupamento).</param>
    /// <param name="lateralShift">Quanto o bloco desliza no eixo X rumo à bola (0..1).</param>
    /// <param name="depthShift">Quanto o bloco sobe/recua no eixo Z (metros).</param>
    /// <param name="separationRadius">Distância mínima desejada entre companheiros.</param>
    /// <param name="separationStrength">Força do empurrão de separação.</param>
    public static Decision DecideOffBall(
        SoccerPlayer self,
        Vector3 ballPos,
        Vector3 ownGoalCenter,
        Vector3 fieldCenter,
        bool teamHasBall,
        bool isDesignatedChaser,
        IReadOnlyList<SoccerPlayer> teammates,
        float lateralShift = 0.45f,
        float depthShift = 3.0f,
        float separationRadius = 2.4f,
        float separationStrength = 1.2f)
    {
        if (isDesignatedChaser)
        {
            return new Decision { type = DecisionType.ChaseBall, targetPosition = ballPos };
        }

        Vector3 home = self.HomePosition;

        // 1) Deslocamento lateral do BLOCO: todo mundo acompanha o lado da bola,
        //    mantendo a sua distância relativa à home (preserva a forma da linha).
        float shiftX = (ballPos.x - fieldCenter.x) * lateralShift;

        // 2) Deslocamento em profundidade por POSSE e PAPEL.
        //    Sentido do ataque do próprio time = do gol próprio para o centro.
        float attackDir = Mathf.Sign(fieldCenter.z - ownGoalCenter.z);
        if (attackDir == 0f) attackDir = 1f;

        float roleDepth = RoleDepthFactor(self.role); // atacante sobe mais, zagueiro menos
        float shiftZ = teamHasBall
            ?  depthShift * roleDepth * attackDir            // com a bola: sobe
            : -depthShift * (1f - roleDepth) * attackDir;    // sem a bola: compacta atrás

        Vector3 target = new Vector3(home.x + shiftX, home.y, home.z + shiftZ);

        // 3) Separação: empurra para longe de companheiros próximos (anti-empilhamento).
        if (teammates != null)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < teammates.Count; i++)
            {
                var mate = teammates[i];
                if (mate == null || mate == self) continue;

                Vector3 d = self.transform.position - mate.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist > 0.001f && dist < separationRadius)
                {
                    // Quanto mais perto, mais forte o empurrão (linear).
                    push += d.normalized * (separationRadius - dist) / separationRadius;
                }
            }
            target += push * separationStrength;
        }

        target.y = home.y;
        return new Decision { type = DecisionType.MoveToFormation, targetPosition = target };
    }

    /// <summary>
    /// Quanto cada papel "sobe" no campo (0 = bem recuado, 1 = bem adiantado).
    /// Mantém zagueiros atrás e atacantes na frente — evita o alinhamento numa linha só.
    /// </summary>
    private static float RoleDepthFactor(Role role)
    {
        switch (role)
        {
            case Role.Goalkeeper: return 0.0f;
            case Role.Defender:   return 0.25f;
            case Role.Midfielder: return 0.6f;
            case Role.Forward:    return 0.9f;
            default:              return 0.5f;
        }
    }

    // -------------------------------------------------------------------------
    // Dispersão pós-gol — alvos espalhados longe do gol
    // -------------------------------------------------------------------------
    /// <summary>
    /// Alvo de dispersão após um gol/defesa: a home do jogador empurrada para LONGE
    /// do gol indicado, com um leque lateral por índice, para que os jogadores se
    /// afastem do gol em vez de ficarem amontoados nele.
    /// </summary>
    public static Vector3 DisperseTarget(
        SoccerPlayer self,
        Vector3 goalToLeave,
        Vector3 fieldCenter,
        int indexInTeam,
        int teamCount,
        float spreadDistance = 6f)
    {
        Vector3 home = self.HomePosition;

        // Direção "para longe do gol" (do gol em direção ao centro do campo).
        Vector3 away = fieldCenter - goalToLeave; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        away.Normalize();

        // Leque lateral: distribui os jogadores ao longo do eixo perpendicular.
        Vector3 side = Vector3.Cross(Vector3.up, away);
        float t = teamCount > 1 ? (indexInTeam / (float)(teamCount - 1)) - 0.5f : 0f;

        Vector3 target = home + away * spreadDistance + side * (t * spreadDistance * 1.5f);
        target.y = home.y;
        return target;
    }

    // -------------------------------------------------------------------------
    // Decisão do DONO DA BOLA
    // -------------------------------------------------------------------------
    /// <summary>
    /// Dono da bola decide entre chutar, passar ou driblar.
    /// </summary>
    public static Decision DecideOnBall(
        SoccerPlayer self,
        Vector3 targetGoalCenter,
        float goalWidth,
        IReadOnlyList<SoccerPlayer> teammates,
        IReadOnlyList<SoccerPlayer> opponents,
        float shootingRange,
        float passSearchRange,
        float passLaneClearance)
    {
        Vector3 pos = self.transform.position;
        float distToGoal = Vector3.Distance(pos, targetGoalCenter);

        // 1) Em alcance de chute → finaliza
        if (distToGoal <= shootingRange)
        {
            // Mira um ponto aleatório entre as traves (variando largura)
            float half = goalWidth * 0.5f;
            Vector3 aim = targetGoalCenter;
            aim.x += Random.Range(-half * 0.8f, half * 0.8f);
            aim.y += Random.Range(0.2f, 1.6f);
            return new Decision { type = DecisionType.Shoot, shootTarget = aim };
        }

        // 2) Procura passe para um companheiro mais à frente com linha livre
        SoccerPlayer best = FindPassTarget(self, targetGoalCenter, teammates, opponents,
                                           passSearchRange, passLaneClearance);
        if (best != null)
            return new Decision { type = DecisionType.Pass, passTarget = best };

        // 3) Senão, dribla em direção ao gol
        return new Decision { type = DecisionType.Dribble, targetPosition = targetGoalCenter };
    }

    /// <summary>
    /// Encontra o melhor companheiro para passe: mais à frente (mais perto do gol
    /// alvo que o portador) e com a linha de passe relativamente livre de adversários.
    /// </summary>
    private static SoccerPlayer FindPassTarget(
        SoccerPlayer self,
        Vector3 targetGoalCenter,
        IReadOnlyList<SoccerPlayer> teammates,
        IReadOnlyList<SoccerPlayer> opponents,
        float searchRange,
        float laneClearance)
    {
        Vector3 from = self.transform.position;
        float selfDistToGoal = Vector3.Distance(from, targetGoalCenter);

        SoccerPlayer best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < teammates.Count; i++)
        {
            var mate = teammates[i];
            if (mate == null || mate == self) continue;

            Vector3 to = mate.transform.position;
            float dist = Vector3.Distance(from, to);
            if (dist > searchRange || dist < 1.5f) continue;

            // Só passa para frente (companheiro mais perto do gol que eu)
            float mateDistToGoal = Vector3.Distance(to, targetGoalCenter);
            if (mateDistToGoal >= selfDistToGoal - 0.5f) continue;

            // Linha de passe livre?
            if (!IsLaneClear(from, to, opponents, laneClearance)) continue;

            // Score: prioriza quem avança mais rumo ao gol
            float score = (selfDistToGoal - mateDistToGoal);
            if (score > bestScore)
            {
                bestScore = score;
                best = mate;
            }
        }
        return best;
    }

    /// <summary>Verifica se nenhum adversário está perto demais do segmento from→to.</summary>
    private static bool IsLaneClear(Vector3 from, Vector3 to,
                                    IReadOnlyList<SoccerPlayer> opponents, float clearance)
    {
        for (int i = 0; i < opponents.Count; i++)
        {
            var opp = opponents[i];
            if (opp == null) continue;
            float d = DistancePointToSegment(opp.transform.position, from, to);
            if (d < clearance) return false;
        }
        return true;
    }

    private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f);
        t = Mathf.Clamp01(t);
        Vector3 proj = a + ab * t;
        return Vector3.Distance(p, proj);
    }

    // -------------------------------------------------------------------------
    // Formação 4-3-3 — posições "home"
    // -------------------------------------------------------------------------
    /// <summary>
    /// Gera as 11 posições home de um time em 4-3-3, em coordenadas de mundo.
    /// </summary>
    /// <param name="fieldCenter">Centro do campo.</param>
    /// <param name="fieldLength">Comprimento (eixo Z).</param>
    /// <param name="fieldWidth">Largura (eixo X).</param>
    /// <param name="attackingPositiveZ">Se o time ataca no sentido +Z (true) ou -Z (false).</param>
    public static List<(Role role, Vector3 pos)> Build433(
        Vector3 fieldCenter, float fieldLength, float fieldWidth, bool attackingPositiveZ)
    {
        var list = new List<(Role, Vector3)>(11);
        float halfL = fieldLength * 0.5f;
        float dir = attackingPositiveZ ? 1f : -1f; // sentido de ataque

        // Profundidades (fração do meio-campo a partir do CENTRO; negativo = recuado)
        // O time ocupa a metade do próprio lado e avança até o meio.
        float gkZ   = fieldCenter.z - dir * (halfL * 0.92f);
        float defZ  = fieldCenter.z - dir * (halfL * 0.55f);
        float midZ  = fieldCenter.z - dir * (halfL * 0.15f);
        float fwdZ  = fieldCenter.z + dir * (halfL * 0.25f);

        float w = fieldWidth * 0.5f;

        // Goleiro
        list.Add((Role.Goalkeeper, new Vector3(fieldCenter.x, fieldCenter.y, gkZ)));

        // 4 defensores
        float[] defX = { -0.32f, -0.11f, 0.11f, 0.32f };
        foreach (var x in defX)
            list.Add((Role.Defender, new Vector3(fieldCenter.x + x * fieldWidth, fieldCenter.y, defZ)));

        // 3 meias
        float[] midX = { -0.26f, 0f, 0.26f };
        foreach (var x in midX)
            list.Add((Role.Midfielder, new Vector3(fieldCenter.x + x * fieldWidth, fieldCenter.y, midZ)));

        // 3 atacantes
        float[] fwdX = { -0.26f, 0f, 0.26f };
        foreach (var x in fwdX)
            list.Add((Role.Forward, new Vector3(fieldCenter.x + x * fieldWidth, fieldCenter.y, fwdZ)));

        return list;
    }
}
