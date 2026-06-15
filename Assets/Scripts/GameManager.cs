using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

/// <summary>
/// Fase 3/4 — Game Manager (Goalkeeper VR)
///
/// Orquestra o loop completo no modo ECOSSISTEMA (partida com os 20 agentes):
/// calibração → kickoff → defesa/gol → placar → feedback → dispersão → reset.
///
/// O atacante único legado (EnemyShooter) foi removido — a simulação completa
/// (MatchManager) é o único modo.
///
/// Ligações de eventos:
///   HandTrackingCatch.OnCatch       → OnBallCaught()
///   HandTrackingCatch.OnParry       → OnBallParried()
///   GoalCalibrator.OnGoalCalibrated → StartMatch()
/// </summary>
public class GameManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Referências da cena")]
    [Tooltip("Simulação de partida (modo ecossistema).")]
    public MatchManager matchManager;
    public Rigidbody ball;

    [Tooltip("Mãos do goleiro — usadas para soltar a bola ao reiniciar a jogada.")]
    public HandTrackingCatch leftHand;
    public HandTrackingCatch rightHand;

    [Header("Linha do gol")]
    [Tooltip("Plano Z da linha do gol. Gol detectado quando ball.z <= goalLine.z (sentido -Z).")]
    public Transform goalLine;
    [Tooltip("Trave esquerda. Limite X esquerdo.")]
    public Transform postLeft;
    [Tooltip("Trave direita. Limite X direito.")]
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

    [Header("Efeitos")]
    [Tooltip("Flash ao defender (CanvasGroup ou Image com alpha).")]
    public CanvasGroup saveFlash;
    public float flashDuration = 0.25f;
    [Tooltip("Gerência da torcida (comemora em gol/defesa).")]
    public CrowdManager crowdManager;

    [Header("Spawn ao iniciar")]
    [Tooltip("Se ligado, a bola fica oculta até a partida começar.")]
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

    private enum MatchState { WaitingCalibration, Live, RoundOver }
    private MatchState _matchState = MatchState.WaitingCalibration;
    private bool _roundResolved = false;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    private void Start()
    {
        ValidateReferences();
        UpdateUI();
        SetMessage("Abra os bracos e feche as maos para calibrar.");

        if (hideUntilStart && ball != null) ball.gameObject.SetActive(false);
    }

    /// <summary>Chamado pelo MatchManager quando um inimigo finaliza ao gol do jogador.</summary>
    public void NotifyShot() => PlayAudio(clipWhistle, 0.6f);

    private void Update()
    {
        if (_matchState != MatchState.Live || _roundResolved) return;

        // Detecta GOL pela bola "viva" cruzando o plano do gol (defesa vem do agarre).
        CheckBallReachedGoalPlane();
    }

    // -------------------------------------------------------------------------
    // Validação de referências (aparece no console VR)
    // -------------------------------------------------------------------------
    private void ValidateReferences()
    {
        if (matchManager == null) Debug.LogError("[GameManager] 'Match Manager' não atribuído.");
        if (ball == null)         Debug.LogError("[GameManager] 'Ball' não atribuída.");
        if (goalLine == null)     Debug.LogError("[GameManager] 'Goal Line' não atribuída.");
        if (postLeft == null || postRight == null)
            Debug.LogWarning("[GameManager] Traves não atribuídas — detecção de gol fica só por Z.");
        if (leftHand == null || rightHand == null)
            Debug.LogWarning("[GameManager] Mãos não atribuídas — bola pode não soltar no reset.");
        if (audioSource == null)  Debug.LogWarning("[GameManager] 'Audio Source' não atribuído — sem áudio.");
    }

    // -------------------------------------------------------------------------
    // Resolução do chute
    // -------------------------------------------------------------------------
    /// <summary>Verifica se a bola cruzou o plano do gol e resolve como GOL. Retorna true se resolveu.</summary>
    private bool CheckBallReachedGoalPlane()
    {
        if (ball == null || goalLine == null) return false;

        // Bola conduzida/agarrada (kinematic) não conta — só bola "viva" (chute/solta).
        if (ball.isKinematic) return false;

        Vector3 p = ball.transform.position;
        if (p.z > goalLine.position.z) return false; // ainda não chegou ao plano

        // Cruzou o plano — é gol (dentro dos limites)?
        bool insideWidth = true;
        if (postLeft != null && postRight != null)
        {
            float xMin = Mathf.Min(postLeft.position.x, postRight.position.x) - goalBoundsMargin;
            float xMax = Mathf.Max(postLeft.position.x, postRight.position.x) + goalBoundsMargin;
            insideWidth = p.x >= xMin && p.x <= xMax;
        }
        bool underBar = p.y <= goalLine.position.y + crossbarHeight + goalBoundsMargin;

        if (insideWidth && underBar) { RegisterGoal(); return true; }

        // Fora dos limites: o MatchManager trata a saída de campo (reinício).
        return false;
    }

    // -------------------------------------------------------------------------
    // Callbacks de eventos
    // -------------------------------------------------------------------------
    public void OnBallCaught()
    {
        if (_matchState != MatchState.Live || _roundResolved) return;
        Debug.Log("[GameManager] Bola agarrada → defesa.");
        RegisterSave();
    }

    public void OnBallParried()
    {
        if (_matchState != MatchState.Live || _roundResolved) return;
        // Feedback imediato; a resolução final vem do plano do gol.
        PlayAudio(clipSave, 0.7f);
        StartCoroutine(ShowFlash());
        Debug.Log("[GameManager] Espalmar — aguardando desfecho.");
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

    // -------------------------------------------------------------------------
    // Ciclo de jogadas
    // -------------------------------------------------------------------------
    public void StartMatch()
    {
        if (matchManager == null)
        {
            Debug.LogError("[GameManager] Não é possível iniciar: 'Match Manager' não atribuído.");
            return;
        }

        _saves = 0;
        _goals = 0;
        UpdateUI();
        SetMessage("");

        if (ball != null) ball.gameObject.SetActive(true);
        _roundResolved = false;
        _matchState = MatchState.Live; // "ao vivo": detecta gol/defesa todo frame
        matchManager.OnGoalCalibratedHandler(goalWidthForMatch());
        matchManager.KickOff();

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

        // Pausa a partida e DISPERSA os jogadores: eles se afastam do gol
        // (em vez de ficarem amontoados nele) durante o intervalo.
        matchManager.Disperse(true);
        DropBallFromHands();

        yield return new WaitForSeconds(resetDelay);

        SetMessage("");
        _roundResolved = false;
        matchManager.ResetForNextRound();   // recoloca bola + jogadores e dá a posse ao inimigo
        _matchState = MatchState.Live;
    }

    /// <summary>Solta a bola das mãos do goleiro (caso tenha sido agarrada) antes do reset.</summary>
    private void DropBallFromHands()
    {
        if (leftHand  != null) leftHand.ForceDrop();
        if (rightHand != null) rightHand.ForceDrop();
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
