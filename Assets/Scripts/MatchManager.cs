using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Fase 5/6 — Simulação central da partida (Goalkeeper VR).
///
/// Orquestra os 21 agentes: posse de bola, perseguição, passes, chutes, troca de
/// posse com cooldown, e TIME-SLICING (só os mais próximos da bola decidem todo
/// frame; o resto, a cada N frames). O time inimigo ataca o gol do jogador; o
/// aliado (humano = goleiro) é "nerfado" pelo TeamNerfManager para perder a posse.
///
/// Integração: o gol do JOGADOR (defesa/gol) é detectado pelo GameManager.
/// Aqui tratamos gol no lado inimigo e bola fora → reinício (kickoff).
/// </summary>
public class MatchManager : MonoBehaviour
{
    [Header("Referências")]
    public TeamSpawner spawner;
    public Rigidbody ball;
    public TeamNerfManager nerf;
    public GameManager gameManager;

    [Header("Gols")]
    [Tooltip("Centro do gol do JOGADOR (defendido pelo humano).")]
    public Transform playerGoalCenter;
    [Tooltip("Centro do gol INIMIGO (defendido pelo goleiro estático).")]
    public Transform enemyGoalCenter;
    [Tooltip("Largura do gol do jogador (atualizada pela calibração).")]
    public float playerGoalWidth = 4f;
    [Tooltip("Largura do gol inimigo.")]
    public float enemyGoalWidth = 5f;

    [Header("Parâmetros de jogo")]
    [Tooltip("Raio para ganhar a posse de uma bola livre.")]
    public float controlRadius = 0.8f;
    [Tooltip("Raio em que um adversário rouba a bola do portador.")]
    public float tackleRadius = 0.9f;
    [Tooltip("Cooldown (s) após chute/passe antes do MESMO jogador poder repegar.")]
    public float grabCooldown = 0.5f;
    [Tooltip("Distância do gol para o portador finalizar.")]
    public float shootingRange = 9f;
    public float passSearchRange = 12f;
    public float passLaneClearance = 1.2f;
    public float kickSpeed = 14f;
    public float passSpeed = 9f;

    [Header("Time-slicing")]
    [Tooltip("Quantos jogadores mais próximos da bola decidem TODO frame.")]
    public int activeBrainCount = 4;
    [Tooltip("Os demais decidem a cada N frames.")]
    public int brainSliceInterval = 6;
    [Tooltip("Quantos defensores do time sem a bola perseguem (base).")]
    public int chasersPerTeam = 1;
    [Tooltip("Chasers extras quando o ataque chega ao terço defensivo (equilíbrio defensivo).")]
    public int extraChasersWhenDeep = 2;

    [Header("Chute (windup + indicador)")]
    [Tooltip("Indicador de mira no céu.")]
    public ShotIndicator shotIndicator;
    [Tooltip("Tempo (s) que o atacante para para finalizar (telegrafa o chute).")]
    public float shootWindup = 0.6f;

    [Header("Ataque scriptado")]
    [Tooltip("Quão longe o dribleador inimigo desvia lateralmente do defensor mais próximo.")]
    public float dribbleEvadeStrength = 2.0f;

    [Header("Modo pênalti")]
    [Range(0f, 1f)]
    [Tooltip("Chance de um pênalti acontecer a cada gol/defesa.")]
    public float penaltyChance = 0.10f;
    [Tooltip("Distância (m) do gol do jogador até o ponto de pênalti.")]
    public float penaltyDistance = 9f;
    [Tooltip("Distância (m) que os outros jogadores ficam assistindo (atrás da bola).")]
    public float penaltyWatchDistance = 6f;

    [Header("Formação (anti-agrupamento)")]
    [Tooltip("Quanto o BLOCO desliza lateralmente (X) rumo à bola (0..1). Menor = mais espalhado.")]
    [Range(0f, 1f)] public float blockLateralShift = 0.45f;
    [Tooltip("Quanto o bloco sobe/recua em profundidade (Z) ao atacar/defender, em metros.")]
    public float blockDepthShift = 3.0f;
    [Tooltip("Distância mínima desejada entre companheiros (separação anti-empilhamento).")]
    public float separationRadius = 2.4f;
    [Tooltip("Força do empurrão de separação entre companheiros.")]
    public float separationStrength = 1.2f;

    [Header("Dispersão pós-gol")]
    [Tooltip("Distância que os jogadores se afastam do gol ao comemorar/reagir a um gol.")]
    public float disperseDistance = 6f;

    [Header("Áudio")]
    public AudioSource sfx;
    public AudioClip kickClip;
    public AudioClip passClip;

    [Header("DEBUG")]
    public TMP_Text debugText;

    // -------------------------------------------------------------------------
    private readonly List<SoccerPlayer> _all = new List<SoccerPlayer>();
    private SoccerPlayer _owner;          // dono da bola (null = livre)
    private SoccerPlayer _lastKicker;     // quem chutou/passou por último
    private float _grabCooldownTimer;     // bloqueia repegar do lastKicker
    private float _ownerDecisionTimer;    // para o delay de decisão (nerf aliado)
    private int _frame;
    private bool _playing;
    private bool _spawned;
    private bool _shooting;               // windup de chute em andamento
    private bool _penaltyMode;            // jogada de pênalti em andamento
    private SoccerPlayer _penaltyTaker;   // cobrador do pênalti

    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------
    public bool EnsureSpawned()
    {
        if (_spawned) return true;
        if (spawner == null) { Debug.LogError("[MatchManager] 'Spawner' não atribuído."); return false; }
        if (!spawner.SpawnTeams()) return false;

        _all.Clear();
        _all.AddRange(spawner.allyPlayers);
        _all.AddRange(spawner.enemyPlayers);
        _spawned = true;

        if (ball == null) Debug.LogError("[MatchManager] 'Ball' não atribuída.");
        if (playerGoalCenter == null || enemyGoalCenter == null)
            Debug.LogError("[MatchManager] Centros de gol não atribuídos.");

        IgnoreAllyBallCollisions(); // aliados nunca colidem fisicamente com a bola
        return true;
    }

    /// <summary>
    /// Faz a bola IGNORAR fisicamente todos os colisores dos jogadores aliados.
    /// Assim um chute/bola solta nunca bate num aliado — só o time inimigo
    /// (e suas mãos) interage com a bola.
    /// </summary>
    private void IgnoreAllyBallCollisions()
    {
        if (ball == null) return;
        Collider ballCol = ball.GetComponent<Collider>();
        if (ballCol == null) ballCol = ball.GetComponentInChildren<Collider>();
        if (ballCol == null) return;

        foreach (var p in spawner.allyPlayers)
        {
            if (p == null) continue;
            foreach (var c in p.GetComponentsInChildren<Collider>())
                if (c != null) Physics.IgnoreCollision(ballCol, c, true);
        }
    }

    /// <summary>Coloca tudo na formação e dá a bola ao inimigo (gera ataque ao jogador).</summary>
    public void KickOff()
    {
        if (!EnsureSpawned()) return;

        // Jogadores para casa
        foreach (var p in _all) if (p != null) p.WarpTo(p.HomePosition);

        // Bola no centro
        Vector3 center = spawner.fieldCenter != null ? spawner.fieldCenter.position : transform.position;
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.isKinematic = true;
            ball.transform.position = center + Vector3.up * 0.11f;
            ball.isKinematic = false;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
        }

        // Dá a posse ao atacante inimigo mais próximo do centro
        _owner = null; _lastKicker = null; _grabCooldownTimer = 0f; _ownerDecisionTimer = 0f;
        _penaltyMode = false; _penaltyTaker = null;
        var starter = NearestOfTeam(Team.Enemy, center);
        if (starter != null) GivePossession(starter);

        _playing = true;

        // Movimento IMEDIATO: todos recebem um alvo já no primeiro frame.
        ForceAllDecisions();

        Debug.Log("[MatchManager] Kickoff — inimigo com a posse.");
    }

    /// <summary>Roda uma decisão para todos os jogadores agora (sem esperar o time-slicing).</summary>
    private void ForceAllDecisions()
    {
        if (ball == null) return;
        Vector3 ballPos = ball.transform.position;
        if (_owner != null && !_shooting) DecideOwner();
        foreach (var p in _all)
            if (p != null && p != _owner) DecideOffBall(p, ballPos);
    }

    public void SetPlaying(bool value) => _playing = value;
    public bool IsPlaying => _playing;

    /// <summary>
    /// Reinício após defesa/gol. Sorteia o modo PÊNALTI (penaltyChance); senão, kickoff normal.
    /// Chamado pelo GameManager.
    /// </summary>
    public void ResetForNextRound()
    {
        if (!EnsureSpawned()) return;
        if (Random.value < penaltyChance) StartPenalty();
        else KickOff();
    }

    // -------------------------------------------------------------------------
    // Modo pênalti
    // -------------------------------------------------------------------------
    /// <summary>Um inimigo cobra do ponto de pênalti; os demais assistem de longe.</summary>
    private void StartPenalty()
    {
        Vector3 goal = GoalPos(playerGoalCenter);
        Vector3 fieldCenter = spawner.fieldCenter != null ? spawner.fieldCenter.position : transform.position;

        // Direção do gol do jogador para o campo (onde fica a bola do pênalti).
        Vector3 toField = (fieldCenter - goal); toField.y = 0f;
        if (toField.sqrMagnitude < 0.01f) toField = Vector3.forward;
        toField.Normalize();

        Vector3 spot = goal + toField * penaltyDistance;
        spot.y = 0f;

        // Escolhe o cobrador: atacante inimigo mais próximo do ponto.
        _penaltyTaker = NearestOfTeam(Team.Enemy, spot);

        // Bola no ponto de pênalti, conduzida pelo cobrador.
        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.isKinematic = true;
            ball.transform.position = spot + Vector3.up * 0.11f;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
        }

        // Posiciona todos: cobrador atrás da bola; o resto assistindo de longe.
        Vector3 behind = spot + toField * 1.2f;
        foreach (var p in _all)
        {
            if (p == null) continue;
            if (p == _penaltyTaker) { p.WarpTo(behind); continue; }
            // Demais ficam num arco atrás do ponto, só observando.
            Vector3 watch = spot + toField * penaltyWatchDistance + Random.insideUnitSphere * 3f;
            watch.y = 0f;
            p.WarpTo(watch);
            p.StopMoving();
        }

        _owner = null; _lastKicker = null; _grabCooldownTimer = 0f; _ownerDecisionTimer = 0f;
        if (_penaltyTaker != null) GivePossession(_penaltyTaker);

        _penaltyMode = true;
        _playing = true;
        Debug.Log("[MatchManager] PÊNALTI! Um inimigo vai cobrar.");
    }

    /// <summary>Atualiza a largura do gol do jogador após calibração.</summary>
    public void OnGoalCalibratedHandler(float width) => playerGoalWidth = width;

    // -------------------------------------------------------------------------
    // Loop
    // -------------------------------------------------------------------------
    private void Update()
    {
        if (!_playing || ball == null) { UpdateDebug(); return; }
        _frame++;

        if (_grabCooldownTimer > 0f) _grabCooldownTimer -= Time.deltaTime;

        if (_penaltyMode) { UpdatePenalty(); UpdateDebug(); return; }

        HandlePossession();
        DriveDecisions();
        CheckFieldEvents();
        UpdateDebug();
    }

    /// <summary>No pênalti, só o cobrador age; os demais ficam assistindo parados.</summary>
    private void UpdatePenalty()
    {
        if (_penaltyTaker == null) { KickOff(); return; }

        if (_owner == _penaltyTaker)
        {
            _owner.DribbleBall(ball);
            _ownerDecisionTimer += Time.deltaTime;
            if (!_shooting) DecideOwner(); // dentro do alcance → finaliza (windup)
        }

        CheckFieldEvents(); // trata bola fora (chute torto encerra o pênalti)
    }

    // -------------------------------------------------------------------------
    // Posse
    // -------------------------------------------------------------------------
    private void HandlePossession()
    {
        Vector3 ballPos = ball.transform.position;

        if (_owner == null)
        {
            // Bola livre: SÓ o time INIMIGO pode pegar. Aliados nunca tocam na bola
            // (mas continuam perseguindo/se movendo normalmente).
            SoccerPlayer nearest = null; float best = float.MaxValue;
            foreach (var p in spawner.enemyPlayers)
            {
                if (p == null) continue;
                if (p == _lastKicker && _grabCooldownTimer > 0f) continue;
                float d = Vector3.Distance(p.transform.position, ballPos);
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest != null && best <= controlRadius)
                GivePossession(nearest);
        }
        else
        {
            // Conduz a bola nos pés do dono
            _owner.DribbleBall(ball);
            _ownerDecisionTimer += Time.deltaTime;

            if (_shooting) return; // não rouba durante o windup do chute

            // Roubo: SÓ inimigos roubam. Como o dono é sempre inimigo, os aliados
            // nunca tomam a bola — eles pressionam mas passam "através" da jogada.
            Team tacklingTeam = _owner.team == Team.Ally ? Team.Enemy : Team.Ally;
            if (tacklingTeam != Team.Enemy) return; // dono inimigo → aliados não roubam

            float radius = nerf != null ? nerf.GetTackleRadius(tacklingTeam, tackleRadius) : tackleRadius;
            SoccerPlayer tackler = NearestOpponentWithin(_owner.team, ballPos, radius);
            if (tackler != null && _grabCooldownTimer <= 0f)
                GivePossession(tackler);
        }
    }

    private void GivePossession(SoccerPlayer p)
    {
        if (_owner != null) _owner.HasBall = false;
        _owner = p;
        _owner.HasBall = true;
        _ownerDecisionTimer = 0f;
        if (ball != null) ball.isKinematic = true; // conduzida
    }

    private void ReleaseBall()
    {
        if (_owner != null) _owner.HasBall = false;
        _lastKicker = _owner;
        _owner = null;
        _grabCooldownTimer = grabCooldown;
    }

    // -------------------------------------------------------------------------
    // Decisões (time-sliced)
    // -------------------------------------------------------------------------
    private void DriveDecisions()
    {
        Vector3 ballPos = ball.transform.position;

        // 1) Dono da bola decide todo frame (com delay de nerf p/ aliado).
        //    Durante o windup do chute, não toma novas decisões.
        if (_owner != null && !_shooting)
            DecideOwner();

        // 2) Demais jogadores: nearest K decidem todo frame; resto a cada N frames
        // Marca os "ativos" (mais próximos da bola)
        for (int i = 0; i < _all.Count; i++)
        {
            var p = _all[i];
            if (p == null || p == _owner) continue;

            bool active = IsAmongNearest(p, ballPos, activeBrainCount);
            bool due = active || ((_frame + i) % Mathf.Max(1, brainSliceInterval) == 0);
            if (!due) continue;

            DecideOffBall(p, ballPos);
        }
    }

    private void DecideOwner()
    {
        // Nerf: aliado só age após o delay de decisão
        if (_owner.team == Team.Ally && nerf != null && _ownerDecisionTimer < nerf.GetDecisionDelay(Team.Ally))
            return;

        // Nerf: aliado pode errar (perde a posse com chute fraco aleatório)
        if (_owner.team == Team.Ally && nerf != null && nerf.RollError(Team.Ally))
        {
            Vector3 bad = _owner.transform.position + Random.insideUnitSphere * 3f;
            bad.y = 0.11f;
            _owner.KickBall(ball, bad, passSpeed * 0.6f, false);
            ReleaseBall();
            Debug.Log("[MatchManager] Aliado errou (nerf) — perdeu a posse.");
            return;
        }

        Vector3 targetGoal = _owner.team == Team.Enemy ? GoalPos(playerGoalCenter) : GoalPos(enemyGoalCenter);
        float goalWidth = _owner.team == Team.Enemy ? playerGoalWidth : enemyGoalWidth;

        var teammates = _owner.team == Team.Ally ? spawner.allyPlayers : spawner.enemyPlayers;
        var opponents = _owner.team == Team.Ally ? spawner.enemyPlayers : spawner.allyPlayers;

        Decision d = AISoccerBrain.DecideOnBall(_owner, targetGoal, goalWidth,
            teammates, opponents, shootingRange, passSearchRange, passLaneClearance);

        switch (d.type)
        {
            case DecisionType.Shoot:
                bool atPlayerGoal = _owner.team == Team.Enemy;
                StartCoroutine(ShootSequence(_owner, d.shootTarget, atPlayerGoal));
                break;

            case DecisionType.Pass:
                if (d.passTarget != null)
                {
                    _owner.KickBall(ball, d.passTarget.transform.position, passSpeed, false);
                    PlaySfx(passClip);
                    ReleaseBall();
                }
                else _owner.MoveTo(targetGoal);
                break;

            case DecisionType.Dribble:
            default:
                Vector3 dribTarget = d.targetPosition;
                if (_owner.team == Team.Enemy) dribTarget = ApplyEvasion(_owner, dribTarget);
                _owner.MoveTo(dribTarget);
                break;
        }
    }

    /// <summary>Windup: o atacante para, telegrafa (anim + indicador) e finaliza.</summary>
    private IEnumerator ShootSequence(SoccerPlayer shooter, Vector3 target, bool atPlayerGoal)
    {
        _shooting = true;
        shooter.StopMoving();
        shooter.PlayShoot();

        // Indicador aparece ANTES do chute (durante o windup) e fica até a jogada
        // resolver (defesa/gol). Como segurança, some no tempo estimado de voo.
        if (shotIndicator != null)
        {
            float dist = Vector3.Distance(shooter.transform.position, target);
            float flight = dist / Mathf.Max(kickSpeed, 0.1f);
            shotIndicator.Show(target, shootWindup + flight + 0.6f);
        }

        yield return new WaitForSeconds(shootWindup);

        if (_owner == shooter && ball != null)
        {
            shooter.KickBall(ball, target, kickSpeed, true);
            PlaySfx(kickClip);
            ReleaseBall();
            if (atPlayerGoal && gameManager != null) gameManager.NotifyShot();
            Debug.Log($"[MatchManager] {shooter.name} CHUTOU.");
        }

        // NÃO escondemos aqui: o indicador permanece até o GameManager resolver
        // a jogada (HideShotIndicator) ou o auto-hide pelo tempo de voo.
        _shooting = false;
    }

    /// <summary>Esconde o indicador de mira (chamado pelo GameManager ao resolver a jogada).</summary>
    public void HideShotIndicator()
    {
        if (shotIndicator != null) shotIndicator.Hide();
    }

    /// <summary>Desvia o alvo de drible lateralmente para fugir do defensor mais próximo.</summary>
    private Vector3 ApplyEvasion(SoccerPlayer owner, Vector3 target)
    {
        SoccerPlayer threat = NearestOpponentWithin(owner.team, owner.transform.position, 3f);
        if (threat == null) return target;

        Vector3 toTarget = target - owner.transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return target;

        Vector3 right = Vector3.Cross(Vector3.up, toTarget.normalized);
        Vector3 toThreat = threat.transform.position - owner.transform.position; toThreat.y = 0f;
        float side = Vector3.Dot(toThreat, right) > 0f ? -1f : 1f; // foge para o lado oposto
        return target + right * side * dribbleEvadeStrength;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (sfx != null && clip != null) sfx.PlayOneShot(clip);
    }

    private void DecideOffBall(SoccerPlayer p, Vector3 ballPos)
    {
        bool teamHasBall = _owner != null && _owner.team == p.team;
        Vector3 ownGoal = p.team == Team.Enemy ? GoalPos(enemyGoalCenter) : GoalPos(playerGoalCenter);
        Vector3 fieldCenter = spawner.fieldCenter != null ? spawner.fieldCenter.position : transform.position;
        var team = p.team == Team.Ally ? spawner.allyPlayers : spawner.enemyPlayers;

        // Time sem a bola: os N mais próximos perseguem; resto, formação.
        // Equilíbrio defensivo: se a bola está no nosso terço, mais defensores pressionam.
        bool isChaser = false;
        if (!teamHasBall)
        {
            int chasers = chasersPerTeam;
            float fieldHalf = spawner.fieldLength * 0.5f;
            if (Vector3.Distance(ballPos, ownGoal) < fieldHalf * 0.5f)
                chasers += extraChasersWhenDeep;
            isChaser = IsAmongNearestInList(p, ballPos, team, chasers);
        }

        // Bloco coeso + lanes por papel + separação (corrige alinhamento/agrupamento).
        Decision d = AISoccerBrain.DecideOffBall(
            p, ballPos, ownGoal, fieldCenter, teamHasBall, isChaser, team,
            blockLateralShift, blockDepthShift, separationRadius, separationStrength);

        Vector3 moveTarget = d.targetPosition;

        // ANTI-FILA: perseguidores também recebem separação, para cercarem a bola
        // de ângulos diferentes em vez de correrem enfileirados atrás do portador.
        if (d.type == DecisionType.ChaseBall)
            moveTarget = ApplyChaserSeparation(p, team, moveTarget);

        p.MoveTo(moveTarget);
    }

    /// <summary>Empurra o alvo do perseguidor para longe de companheiros próximos (anti-fila).</summary>
    private Vector3 ApplyChaserSeparation(SoccerPlayer self, List<SoccerPlayer> team, Vector3 target)
    {
        if (team == null) return target;
        Vector3 push = Vector3.zero;
        foreach (var mate in team)
        {
            if (mate == null || mate == self) continue;
            Vector3 dvec = self.transform.position - mate.transform.position; dvec.y = 0f;
            float dist = dvec.magnitude;
            if (dist > 0.001f && dist < separationRadius)
                push += dvec.normalized * (separationRadius - dist) / separationRadius;
        }
        Vector3 result = target + push * separationStrength;
        result.y = target.y;
        return result;
    }

    // -------------------------------------------------------------------------
    // Dispersão após gol/defesa (jogadores se afastam do gol no intervalo)
    // -------------------------------------------------------------------------
    /// <summary>
    /// Espalha os jogadores para longe do gol indicado durante o intervalo pós-jogada.
    /// Chamado pelo GameManager antes do reset. Pausa a tomada de decisão da partida.
    /// </summary>
    public void Disperse(bool fromPlayerGoal = true)
    {
        if (!_spawned) return;
        _playing = false; // congela a lógica de partida durante a comemoração

        Vector3 goalToLeave = fromPlayerGoal ? GoalPos(playerGoalCenter) : GoalPos(enemyGoalCenter);
        Vector3 fieldCenter = spawner.fieldCenter != null ? spawner.fieldCenter.position : transform.position;

        DisperseTeam(spawner.allyPlayers, goalToLeave, fieldCenter);
        DisperseTeam(spawner.enemyPlayers, goalToLeave, fieldCenter);
    }

    private void DisperseTeam(List<SoccerPlayer> team, Vector3 goalToLeave, Vector3 fieldCenter)
    {
        if (team == null) return;
        for (int i = 0; i < team.Count; i++)
        {
            var p = team[i];
            if (p == null) continue;
            Vector3 target = AISoccerBrain.DisperseTarget(p, goalToLeave, fieldCenter, i, team.Count, disperseDistance);
            p.MoveTo(target);
        }
    }

    // -------------------------------------------------------------------------
    // Eventos de campo (gol inimigo / fora) → reinício
    // -------------------------------------------------------------------------
    private void CheckFieldEvents()
    {
        Vector3 b = ball.transform.position;
        Vector3 center = spawner.fieldCenter != null ? spawner.fieldCenter.position : transform.position;

        // Fora de campo (com margem) → kickoff
        float halfX = spawner.fieldWidth * 0.5f + 2f;
        float halfZ = spawner.fieldLength * 0.5f + 2f;
        if (Mathf.Abs(b.x - center.x) > halfX || Mathf.Abs(b.z - center.z) > halfZ)
        {
            Debug.Log("[MatchManager] Bola fora — reinício.");
            KickOff();
            return;
        }

        // Gol no lado INIMIGO (aliado marcou) → reinício (não conta no placar do jogador)
        if (enemyGoalCenter != null)
        {
            float dz = Mathf.Abs(b.z - enemyGoalCenter.position.z);
            float dx = Mathf.Abs(b.x - enemyGoalCenter.position.x);
            if (dz < 0.5f && dx < enemyGoalWidth * 0.5f + 0.3f && b.y < 2.6f)
            {
                Debug.Log("[MatchManager] Aliado marcou no gol inimigo — reinício.");
                KickOff();
            }
        }
        // Gol do JOGADOR é tratado pelo GameManager (defesa/gol/placar).
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private SoccerPlayer NearestOfTeam(Team team, Vector3 pos)
    {
        var list = team == Team.Ally ? spawner.allyPlayers : spawner.enemyPlayers;
        SoccerPlayer best = null; float bestD = float.MaxValue;
        foreach (var p in list)
        {
            if (p == null) continue;
            float d = Vector3.Distance(p.transform.position, pos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private SoccerPlayer NearestOpponentWithin(Team ownerTeam, Vector3 pos, float radius)
    {
        var opponents = ownerTeam == Team.Ally ? spawner.enemyPlayers : spawner.allyPlayers;
        SoccerPlayer best = null; float bestD = radius;
        foreach (var p in opponents)
        {
            if (p == null) continue;
            float d = Vector3.Distance(p.transform.position, pos);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private bool IsAmongNearest(SoccerPlayer p, Vector3 pos, int k)
        => IsAmongNearestInList(p, pos, _all, k);

    private bool IsAmongNearestInList(SoccerPlayer p, Vector3 pos, List<SoccerPlayer> list, int k)
    {
        if (p == null) return false;
        float myD = Vector3.Distance(p.transform.position, pos);
        int closer = 0;
        foreach (var o in list)
        {
            if (o == null || o == p) continue;
            if (Vector3.Distance(o.transform.position, pos) < myD) closer++;
            if (closer >= k) return false;
        }
        return true;
    }

    private static Vector3 GoalPos(Transform t) => t != null ? t.position : Vector3.zero;

    private void UpdateDebug()
    {
        if (debugText == null) return;
        string ownerStr = _owner != null ? $"{_owner.team}/{_owner.role}" : "LIVRE";
        debugText.text =
            $"Partida: {(_playing ? "ON" : "OFF")}  Agentes: {_all.Count}\n" +
            $"Posse: {ownerStr}\n" +
            $"Cooldown: {Mathf.Max(0f, _grabCooldownTimer):F1}";
    }
}
