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
    /// Se for o perseguidor designado do time, vai à bola; senão, ocupa a
    /// posição tática (blend home + viés para a bola + ficar entre bola e gol próprio).
    /// </summary>
    public static Decision DecideOffBall(
        SoccerPlayer self,
        Vector3 ballPos,
        Vector3 ownGoalCenter,
        bool isDesignatedChaser,
        float ballBias = 0.25f,
        float defendBias = 0.15f)
    {
        if (isDesignatedChaser)
        {
            return new Decision { type = DecisionType.ChaseBall, targetPosition = ballPos };
        }

        // Blend: começa na home, puxa um pouco para a bola e um pouco para a
        // linha entre a bola e o gol próprio (postura defensiva).
        Vector3 home = self.HomePosition;
        Vector3 towardBall = Vector3.Lerp(home, ballPos, ballBias);

        Vector3 betweenBallAndGoal = (ballPos + ownGoalCenter) * 0.5f;
        Vector3 target = Vector3.Lerp(towardBall, betweenBallAndGoal, defendBias);

        // Mantém a coordenada de profundidade (Z) próxima da home para não
        // bagunçar a formação (defensores não sobem demais).
        target.y = home.y;

        return new Decision { type = DecisionType.MoveToFormation, targetPosition = target };
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
