using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Fase 3/4 — Game Manager (Goalkeeper VR)
///
/// Orquestra o loop completo: calibração → spawn → drible → chute → resolução
/// (agarrou / gol / fora / timeout) → placar → feedback → reset → próxima jogada.
///
/// Ligações de eventos:
///   HandTrackingCatch.OnCatch  → OnBallCaught()
///   HandTrackingCatch.OnParry  → OnBallParried()
///   EnemyShooter.OnShootExecuted → OnShotFired()
///   GoalCalibrator.OnGoalCalibrated → StartMatch()
/// </summary>
public class GameManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Referências da cena")]
    [Tooltip("Simulação de partida (modo ecossistema). Se atribuído, substitui o atacante único.")]
    public MatchManager matchManager;
    [Tooltip("Atacante único (modo legado). Usado só se Match Manager estiver vazio.")]
    public EnemyShooter enemyShooter;
    public Rigidbody ball;
    public Transform ballSpawnPoint;
    public Transform attackerSpawnPoint;

    [Tooltip("Mãos do goleiro — usadas para soltar a bola ao reiniciar a jogada.")]
    public HandTrackingCatch leftHand;
    public HandTrackingCatch rightHand;

    [Header("Linha do gol")]
    [Tooltip("Plano Z da linha do gol. Gol detectado quando ball.z <= goalLine.z (sentido -Z).")]
    public Transform goalLine;
    [Tooltip("Trave esquerda (mesma do EnemyShooter). Limite X esquerdo.")]
    public Transform postLeft;
    [Tooltip("Trave direita (mesma do EnemyShooter). Limite X direito.")]
    public Transform postRight;
    [Tooltip("Altura do travessão (m) a partir do chão.")]
    public float crossbarHeight = 2.6f;
    [Tooltip("Folga (m) nos limites para tolerância.")]
    public float goalBoundsMargin = 0.1f;

    [Header("UI")]
    public TextMeshProUGUI savesText;
    public TextMeshProUGUI goalsText;
    public TextMeshProUGUI messageText;

    [Header("Áudio")]
    public AudioSource audioSource;
    public AudioClip clipSave;
    public AudioClip clipGoal;
    public AudioClip clipCrowd;
    public AudioClip clipWhistle;

    [Header("Timing")]
    [Tooltip("Delay após resolver uma jogada antes de reiniciar.")]
    public float resetDelay = 3f;
    [Tooltip("Delay antes do atacante correr.")]
    public float preShotDelay = 1f;
    [Tooltip("Tempo máximo (s) de um chute sem resolver — então conta como defesa.")]
    public float shotTimeout = 4f;

    [Header("Efeitos")]
    [Tooltip("Flash ao defender (CanvasGroup ou Image com alpha).")]
    public CanvasGroup saveFlash;
    public float flashDuration = 0.25f;
    [Tooltip("Gerência da torcida (comemora em gol/defesa).")]
    public CrowdManager crowdManager;

    [Header("Spawn ao iniciar")]
    [Tooltip("Se ligado, atacante e bola ficam ocultos até a partida começar.")]
    public bool hideUntilStart = true;

    [Header("Eventos externos")]
    public UnityEvent<int> OnSaveCountChanged;
    public UnityEvent<int> OnGoalCountChanged;
    public UnityEvent OnMatchEnd;

    // -------------------------------------------------------------------------
    // Estado
    // -------------------------------------------------------------------------
    private int _saves = 0;
    private int _goals = 0;

    private enum MatchState { WaitingCalibration, BetweenShots, ShotInFlight, RoundOver }
    private MatchState _matchState = MatchState.WaitingCalibration;
    private bool _roundResolved = false;
    private float _shotTimer = 0f;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    private void Start()
    {
        ValidateReferences();
        UpdateUI();
        SetMessage("Abra os bracos e feche as maos para calibrar.");

        // Esconde atacante único e bola só no modo legado (sem MatchManager)
        if (hideUntilStart && matchManager == null)
        {
            if (enemyShooter != null) enemyShooter.gameObject.SetActive(false);
            if (ball != null) ball.gameObject.SetActive(false);
        }
    }

    private bool UseMatch => matchManager != null;

    /// <summary>Chamado pelo MatchManager quando um inimigo finaliza ao gol do jogador.</summary>
    public void NotifyShot() => PlayAudio(clipWhistle, 0.6f);

    private void Update()
    {
        if (_matchState != MatchState.ShotInFlight || _roundResolved) return;

        _shotTimer += Time.deltaTime;

        if (CheckBallReachedGoalPlane()) return;     // resolveu por gol ou fora

        // Timeout só faz sentido no modo legado (chute discreto). Na partida contínua
        // a defesa vem do agarre; bola que rola longe é tratada pelo MatchManager.
        if (!UseMatch && _shotTimer >= shotTimeout)
        {
            Debug.Log("[GameManager] Timeout do chute — contando como defesa.");
            RegisterSave();
        }
    }

    // -------------------------------------------------------------------------
    // Validação de referências (aparece no console VR)
    // -------------------------------------------------------------------------
    private void ValidateReferences()
    {
        if (matchManager == null && enemyShooter == null)
            Debug.LogError("[GameManager] Nem 'Match Manager' nem 'Enemy Shooter' atribuídos.");
        if (ball == null)         Debug.LogError("[GameManager] 'Ball' não atribuída.");
        if (goalLine == null)     Debug.LogError("[GameManager] 'Goal Line' não atribuída.");
        if (postLeft == null || postRight == null)
            Debug.LogWarning("[GameManager] Traves não atribuídas — detecção de gol fica só por Z.");
        if (leftHand == null || rightHand == null)
            Debug.LogWarning("[GameManager] Mãos não atribuídas — bola pode não soltar no reset.");
        if (ballSpawnPoint == null)     Debug.LogWarning("[GameManager] 'Ball Spawn Point' não atribuído.");
        if (attackerSpawnPoint == null) Debug.LogWarning("[GameManager] 'Attacker Spawn Point' não atribuído.");
        if (audioSource == null)        Debug.LogWarning("[GameManager] 'Audio Source' não atribuído — sem áudio.");
    }

    // -------------------------------------------------------------------------
    // Resolução do chute
    // -------------------------------------------------------------------------
    /// <summary>Verifica se a bola cruzou o plano do gol. Resolve como GOL ou FORA. Retorna true se resolveu.</summary>
    private bool CheckBallReachedGoalPlane()
    {
        if (ball == null || goalLine == null) return false;

        // Bola conduzida/agarrada (kinematic) não conta — só bola "viva" (chute/solta).
        if (ball.isKinematic) return false;

        Vector3 p = ball.transform.position;
        if (p.z > goalLine.position.z) return false; // ainda não chegou ao plano

        // Cruzou o plano — é gol (dentro dos limites) ou fora (largo/por cima)?
        bool insideWidth = true;
        if (postLeft != null && postRight != null)
        {
            float xMin = Mathf.Min(postLeft.position.x, postRight.position.x) - goalBoundsMargin;
            float xMax = Mathf.Max(postLeft.position.x, postRight.position.x) + goalBoundsMargin;
            insideWidth = p.x >= xMin && p.x <= xMax;
        }
        bool underBar = p.y <= goalLine.position.y + crossbarHeight + goalBoundsMargin;

        if (insideWidth && underBar) { RegisterGoal(); return true; }

        // Fora dos limites: no modo partida, deixa o MatchManager tratar (saída de campo).
        if (UseMatch) return false;
        RegisterMiss();
        return true;
    }

    // -------------------------------------------------------------------------
    // Callbacks de eventos
    // -------------------------------------------------------------------------
    public void OnBallCaught()
    {
        if (_matchState != MatchState.ShotInFlight || _roundResolved) return;
        Debug.Log("[GameManager] Bola agarrada → defesa.");
        RegisterSave();
    }

    public void OnBallParried()
    {
        if (_matchState != MatchState.ShotInFlight || _roundResolved) return;
        // Feedback imediato; a resolução final vem do plano do gol ou do timeout.
        PlayAudio(clipSave, 0.7f);
        StartCoroutine(ShowFlash());
        Debug.Log("[GameManager] Espalmar — aguardando desfecho.");
    }

    public void OnShotFired(Vector3 velocity)
    {
        _matchState = MatchState.ShotInFlight;
        _roundResolved = false;
        _shotTimer = 0f;
        PlayAudio(clipWhistle);
        Debug.Log($"[GameManager] Chute em andamento. vel={velocity.magnitude:F1} m/s");
    }

    // -------------------------------------------------------------------------
    // Registro de desfechos
    // -------------------------------------------------------------------------
    private void RegisterSave()
    {
        if (_roundResolved) return;
        _roundResolved = true;
        _saves++;

        PlayAudio(clipSave);
        PlayAudio(clipCrowd, 0.5f, 0.3f);
        StartCoroutine(ShowFlash());
        if (crowdManager != null) crowdManager.CelebrateSave();
        SetMessage("DEFESA!");
        UpdateUI();
        OnSaveCountChanged?.Invoke(_saves);
        Debug.Log($"[GameManager] DEFESA registrada. Total={_saves}");

        StartCoroutine(StartNextRound());
    }

    private void RegisterGoal()
    {
        if (_roundResolved) return;
        _roundResolved = true;
        _goals++;

        PlayAudio(clipGoal);
        PlayAudio(clipCrowd, 0.8f, 0.5f);
        if (crowdManager != null) crowdManager.CelebrateGoal();
        SetMessage("GOL!");
        UpdateUI();
        OnGoalCountChanged?.Invoke(_goals);
        Debug.Log($"[GameManager] GOL registrado. Total={_goals}");

        StartCoroutine(StartNextRound());
    }

    private void RegisterMiss()
    {
        if (_roundResolved) return;
        _roundResolved = true;

        PlayAudio(clipCrowd, 0.3f);
        SetMessage("FORA!");
        Debug.Log("[GameManager] Chute para fora — nem gol nem defesa.");

        StartCoroutine(StartNextRound());
    }

    // -------------------------------------------------------------------------
    // Ciclo de chutes
    // -------------------------------------------------------------------------
    public void StartMatch()
    {
        _saves = 0;
        _goals = 0;
        UpdateUI();
        SetMessage("");

        if (UseMatch)
        {
            // Modo ecossistema: a partida cuida de bola e jogadores.
            if (ball != null) ball.gameObject.SetActive(true);
            _roundResolved = false;
            _matchState = MatchState.ShotInFlight; // "ao vivo": detecta gol/defesa todo frame
            matchManager.OnGoalCalibratedHandler(goalWidthForMatch());
            matchManager.KickOff();
        }
        else
        {
            // Modo legado: atacante único
            _matchState = MatchState.BetweenShots;
            if (enemyShooter != null)
            {
                enemyShooter.gameObject.SetActive(true);
                if (attackerSpawnPoint != null)
                    enemyShooter.ResetToIdle(attackerSpawnPoint.position);
            }
            if (ball != null) ball.gameObject.SetActive(true);
            ResetBall();
            StartCoroutine(BeginShotSequence());
        }

        PlayAudio(clipWhistle);
        Debug.Log("[GameManager] Partida iniciada!");
    }

    private float goalWidthForMatch()
    {
        if (postLeft != null && postRight != null)
            return Mathf.Abs(postLeft.position.x - postRight.position.x);
        return 4f;
    }

    private IEnumerator StartNextRound()
    {
        _matchState = MatchState.RoundOver;
        if (UseMatch) matchManager.SetPlaying(false); // pausa a partida durante o intervalo
        yield return new WaitForSeconds(resetDelay);

        SetMessage("");

        if (UseMatch)
        {
            _roundResolved = false;
            matchManager.ResetForNextRound();   // recoloca bola + jogadores e dá a posse ao inimigo
            _matchState = MatchState.ShotInFlight;
        }
        else
        {
            ResetBall();
            ResetAttacker();
            yield return new WaitForSeconds(preShotDelay);
            StartCoroutine(BeginShotSequence());
        }
    }

    private IEnumerator BeginShotSequence()
    {
        _matchState = MatchState.BetweenShots;
        yield return new WaitForSeconds(0.5f);
        if (enemyShooter != null) enemyShooter.BeginRun();
    }

    // -------------------------------------------------------------------------
    // Reset
    // -------------------------------------------------------------------------
    private void ResetBall()
    {
        if (ball == null) return;

        if (leftHand  != null) leftHand.ForceDrop();
        if (rightHand != null) rightHand.ForceDrop();
        ball.transform.SetParent(null);

        // Para a física e reposiciona enquanto kinematic
        ball.isKinematic = true;
        ball.transform.position = ballSpawnPoint != null ? ballSpawnPoint.position : Vector3.zero;
        ball.transform.rotation = Quaternion.identity;

        // Só agora (não-kinematic) é seguro zerar as velocidades
        ball.isKinematic = false;
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;
    }

    private void ResetAttacker()
    {
        if (enemyShooter == null || attackerSpawnPoint == null) return;
        enemyShooter.ResetToIdle(attackerSpawnPoint.position);
    }

    // -------------------------------------------------------------------------
    // UI / Áudio / Efeitos
    // -------------------------------------------------------------------------
    private void UpdateUI()
    {
        if (savesText != null) savesText.text = $"Defesas: {_saves}";
        if (goalsText != null) goalsText.text = $"Gols: {_goals}";
    }

    private void SetMessage(string msg)
    {
        if (messageText != null) messageText.text = msg;
    }

    private void PlayAudio(AudioClip clip, float volume = 1f, float delay = 0f)
    {
        if (audioSource == null || clip == null) return;
        if (delay <= 0f) audioSource.PlayOneShot(clip, volume);
        else StartCoroutine(PlayDelayed(clip, volume, delay));
    }

    private IEnumerator PlayDelayed(AudioClip clip, float volume, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
    }

    private IEnumerator ShowFlash()
    {
        if (saveFlash == null) yield break;
        saveFlash.alpha = 1f;
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            elapsed += Time.deltaTime;
            saveFlash.alpha = Mathf.Lerp(1f, 0f, elapsed / flashDuration);
            yield return null;
        }
        saveFlash.alpha = 0f;
    }

    // -------------------------------------------------------------------------
    // API pública
    // -------------------------------------------------------------------------
    public int Saves => _saves;
    public int Goals => _goals;
}
