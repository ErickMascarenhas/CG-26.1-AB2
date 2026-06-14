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
        return true;
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
        var starter = NearestOfTeam(Team.Enemy, center);
        if (starter != null) GivePossession(starter);

        _playing = true;
        Debug.Log("[MatchManager] Kickoff — inimigo com a posse.");
    }

    public void SetPlaying(bool value) => _playing = value;
    public bool IsPlaying => _playing;

    /// <summary>Reinício após defesa/gol no gol do jogador (chamado pelo GameManager).</summary>
    public void ResetForNextRound() => KickOff();

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

        HandlePossession();
        DriveDecisions();
        CheckFieldEvents();
        UpdateDebug();
    }

    // -------------------------------------------------------------------------
    // Posse
    // -------------------------------------------------------------------------
    private void HandlePossession()
    {
        Vector3 ballPos = ball.transform.position;

        if (_owner == null)
        {
            // Bola livre: jogador mais próximo (exceto lastKicker em cooldown) ganha
            SoccerPlayer nearest = null; float best = float.MaxValue;
            foreach (var p in _all)
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

            // Roubo: adversário dentro do raio de tackle troca a posse.
            // Aliados têm raio reduzido (nerf) → inimigo dribla/passa por eles.
            Team tacklingTeam = _owner.team == Team.Ally ? Team.Enemy : Team.Ally;
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
        if (shotIndicator != null) shotIndicator.Show(target);

        yield return new WaitForSeconds(shootWindup);

        if (_owner == shooter && ball != null)
        {
            shooter.KickBall(ball, target, kickSpeed, true);
            PlaySfx(kickClip);
            ReleaseBall();
            if (atPlayerGoal && gameManager != null) gameManager.NotifyShot();
            Debug.Log($"[MatchManager] {shooter.name} CHUTOU.");
        }

        if (shotIndicator != null) shotIndicator.Hide();
        _shooting = false;
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

        // Time sem a bola: os N mais próximos perseguem; resto, formação.
        // Equilíbrio defensivo: se a bola está no nosso terço, mais defensores pressionam.
        bool isChaser = false;
        if (!teamHasBall)
        {
            var team = p.team == Team.Ally ? spawner.allyPlayers : spawner.enemyPlayers;
            int chasers = chasersPerTeam;
            float fieldHalf = spawner.fieldLength * 0.5f;
            if (Vector3.Distance(ballPos, ownGoal) < fieldHalf * 0.5f)
                chasers += extraChasersWhenDeep;
            isChaser = IsAmongNearestInList(p, ballPos, team, chasers);
        }

        Decision d = AISoccerBrain.DecideOffBall(p, ballPos, ownGoal, isChaser);
        p.MoveTo(d.targetPosition);
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
