using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

/// <summary>
/// Fase 1 — Calibração Dinâmica do Gol (Goalkeeper VR)
///
/// Como usar:
///   1. Crie um GameObject "GoalCalibrator" na cena.
///   2. Arraste este script nele.
///   3. Atribua goalRoot (Transform do prefab do gol, pivot no chão/centro).
///   4. Atribua instructionCanvas (Canvas World-Space com a instrução).
///   5. Ajuste os parâmetros no Inspector conforme necessário.
///
/// Lógica:
///   - A cada frame lê as joints das duas mãos via XRHandSubsystem.
///   - Calcula o "fechamento" (0 = aberta, 1 = punho) pela distância média
///     das pontas dos 5 dedos até a palma, normalizada pelo comprimento médio do dedo.
///   - Quando ambas as mãos ≥ closeThreshold por pelo menos calibrationHoldTime
///     segundos → captura posição das palmas, mede distância, aplica ao gol.
///   - Recalibração: segurar punhos por recalibrateHoldTime com o gol já calibrado.
/// </summary>
public class GoalCalibrator : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Referências")]
    [Tooltip("Root do prefab do gol (pivot no chão, centro da linha).")]
    public Transform goalRoot;

    [Tooltip("Canvas World-Space com instrução 'Abra os braços e feche as mãos'.")]
    public GameObject instructionCanvas;

    [Tooltip("GameObject(s) a ativar após calibração (campo, atacante, etc.).")]
    public GameObject[] objectsToEnableOnCalibration;

    [Header("Limites de largura (abertura entre as traves)")]
    [Tooltip("Largura mínima do gol em metros.")]
    public float minGoalWidth = 4f;
    [Tooltip("Largura máxima do gol em metros.")]
    public float maxGoalWidth = 10f;
    [Tooltip("Margem total (m) de geometria extra ALÉM das traves no modelo (scale=1). " +
             "Use para que a mão alcance as traves: a escala passa a usar a abertura, não a largura total. " +
             "Ex.: se o modelo tem 0.5 m de moldura de cada lado, use 1.0.")]
    public float widthMargin = 0f;

    [Header("Limites de altura (próprios, não por razão)")]
    [Tooltip("Altura mínima do gol em metros (quando o gol é o mais estreito).")]
    public float minGoalHeight = 2.0f;
    [Tooltip("Altura máxima do gol em metros (quando o gol é o mais largo). " +
             "Para altura fixa, deixe igual ao mínimo.")]
    public float maxGoalHeight = 2.6f;

    [Header("Gestos")]
    [Range(0f, 1f)]
    [Tooltip("Valor mínimo de 'fechamento' para considerar punho (0=aberta, 1=fechada).")]
    public float closeThreshold = 0.70f;

    [Tooltip("Segundos sustentando o punho para calibrar pela primeira vez.")]
    public float calibrationHoldTime = 0.4f;

    [Tooltip("Segundos sustentando o punho (com gol já calibrado) para recalibrar.")]
    public float recalibrateHoldTime = 2.0f;

    [Header("Eventos")]
    public UnityEvent<float> OnGoalCalibrated;   // largura resultante
    public UnityEvent OnRecalibrationStarted;

    [Header("DEBUG (arraste um TextMeshPro World-Space para ver dados ao vivo)")]
    [Tooltip("Texto World-Space para mostrar status do hand tracking no headset.")]
    public TMP_Text debugText;

    // -------------------------------------------------------------------------
    // Estado interno
    // -------------------------------------------------------------------------
    private XRHandSubsystem _handSubsystem;
    private bool _isCalibrated = false;
    private float _bothFistTimer = 0f;

    // IDs das joints que usamos
    // Pontas dos dedos: Index, Middle, Ring, Little, Thumb
    private static readonly XRHandJointID[] _fingertips =
    {
        XRHandJointID.IndexTip,
        XRHandJointID.MiddleTip,
        XRHandJointID.RingTip,
        XRHandJointID.LittleTip,
        XRHandJointID.ThumbTip
    };

    // Base dos dedos (para estimar comprimento "médio do dedo")
    private static readonly XRHandJointID[] _fingerBases =
    {
        XRHandJointID.IndexProximal,
        XRHandJointID.MiddleProximal,
        XRHandJointID.RingProximal,
        XRHandJointID.LittleProximal,
        XRHandJointID.ThumbProximal
    };

    private static readonly XRHandJointID _palmJoint = XRHandJointID.Palm;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------
    private void Start()
    {
        TryAcquireSubsystem();

        // Validação de referências (aparece no console VR)
        if (goalRoot == null)
            Debug.LogError("[GoalCalibrator] 'Goal Root' não atribuído — o gol não será redimensionado.");
        if (instructionCanvas == null)
            Debug.LogWarning("[GoalCalibrator] 'Instruction Canvas' não atribuído.");
        if (_handSubsystem == null)
            Debug.LogWarning("[GoalCalibrator] XRHandSubsystem ainda nulo no Start — " +
                             "largue os controles e confirme o Hand Tracking Subsystem.");

        // Garante estado inicial
        if (instructionCanvas != null) instructionCanvas.SetActive(true);
        foreach (var go in objectsToEnableOnCalibration)
            if (go != null) go.SetActive(false);
    }

    /// <summary>
    /// Tenta obter o XRHandSubsystem. Robusto: o subsistema pode não estar
    /// pronto no Start (a inicialização do XR é assíncrona), então também
    /// chamamos isto no Update enquanto for nulo.
    /// </summary>
    private void TryAcquireSubsystem()
    {
        // Método 1: via SubsystemManager (mais confiável)
        var list = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(list);
        if (list.Count > 0)
        {
            _handSubsystem = list[0];
            return;
        }

        // Método 2: via loader ativo (fallback)
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _handSubsystem = loader?.GetLoadedSubsystem<XRHandSubsystem>();
    }

    private void Update()
    {
        // Reaquisição contínua enquanto não tiver o subsystem
        if (_handSubsystem == null)
        {
            TryAcquireSubsystem();
            UpdateDebugHud(false, false, 0f, 0f, 0f);
            return;
        }

        var leftHand  = _handSubsystem.leftHand;
        var rightHand = _handSubsystem.rightHand;

        bool leftTracked  = leftHand.isTracked;
        bool rightTracked = rightHand.isTracked;

        float leftClose  = leftTracked  ? ComputeHandClosedness(leftHand)  : 0f;
        float rightClose = rightTracked ? ComputeHandClosedness(rightHand) : 0f;

        UpdateDebugHud(leftTracked, rightTracked, leftClose, rightClose, _bothFistTimer);

        // Só processa se ambas as mãos estão rastreadas
        if (!leftTracked || !rightTracked)
        {
            _bothFistTimer = 0f;
            return;
        }

        bool bothFist = leftClose >= closeThreshold && rightClose >= closeThreshold;

        if (bothFist)
        {
            _bothFistTimer += Time.deltaTime;
            float holdRequired = _isCalibrated ? recalibrateHoldTime : calibrationHoldTime;

            if (_bothFistTimer >= holdRequired)
            {
                _bothFistTimer = 0f;
                if (_isCalibrated) OnRecalibrationStarted?.Invoke();
                PerformCalibration(leftHand, rightHand);
            }
        }
        else
        {
            _bothFistTimer = 0f;
        }
    }

    /// <summary>Atualiza o painel de debug World-Space (visível no headset).</summary>
    private void UpdateDebugHud(bool leftTracked, bool rightTracked,
                                float leftClose, float rightClose, float timer)
    {
        if (debugText == null) return;

        if (_handSubsystem == null)
        {
            debugText.text = "<color=red>SUBSYSTEM: NAO ENCONTRADO</color>\n" +
                             "Largue os controles e ative o\nrastreamento de maos no Quest.";
            return;
        }

        string lT = leftTracked  ? "<color=green>OK</color>"  : "<color=red>NAO</color>";
        string rT = rightTracked ? "<color=green>OK</color>" : "<color=red>NAO</color>";

        debugText.text =
            $"SUBSYSTEM: <color=green>OK</color>\n" +
            $"Mao E rastreada: {lT}\n" +
            $"Mao D rastreada: {rT}\n" +
            $"Fechamento E: {leftClose:F2}\n" +
            $"Fechamento D: {rightClose:F2}\n" +
            $"Limite p/ punho: {closeThreshold:F2}\n" +
            $"Timer: {timer:F2}s  Calibrado: {_isCalibrated}";
    }

    // -------------------------------------------------------------------------
    // Lógica principal
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retorna um valor 0–1 representando o fechamento da mão.
    /// 0 = totalmente aberta, 1 = punho fechado.
    /// Método: distância média ponta→palma dividida pelo comprimento médio dos dedos.
    /// Quanto mais perto a ponta está da palma, mais fechada a mão → valor mais alto.
    /// </summary>
    // Constantes da razão ponta→palma / base→palma.
    // Dedo estendido: ponta longe da palma → razão alta (~2.0).
    // Dedo curvado: ponta perto da palma → razão baixa (~0.9).
    private const float OpenRatio   = 2.0f;
    private const float ClosedRatio = 0.9f;

    private float ComputeHandClosedness(XRHand hand)
    {
        if (!hand.GetJoint(_palmJoint).TryGetPose(out Pose palmPose))
            return 0f;

        float total = 0f;
        int count = 0;

        // Usa apenas Index, Middle, Ring, Little (índices 0–3). Pula o polegar (4).
        for (int i = 0; i < 4; i++)
        {
            if (!hand.GetJoint(_fingertips[i]).TryGetPose(out Pose tipPose))   continue;
            if (!hand.GetJoint(_fingerBases[i]).TryGetPose(out Pose basePose)) continue;

            float baseToPalm = Vector3.Distance(basePose.position, palmPose.position);
            if (baseToPalm < 0.001f) continue;

            float tipToPalm = Vector3.Distance(tipPose.position, palmPose.position);
            float ratio = tipToPalm / baseToPalm;

            // Mapeia a razão para 0 (aberta) → 1 (fechada).
            float closed = Mathf.Clamp01((OpenRatio - ratio) / (OpenRatio - ClosedRatio));
            total += closed;
            count++;
        }

        return count > 0 ? total / count : 0f;
    }

    /// <summary>
    /// Captura a posição das palmas, calcula a largura real e redimensiona o gol.
    /// </summary>
    private void PerformCalibration(XRHand leftHand, XRHand rightHand)
    {
        if (!leftHand.GetJoint(_palmJoint).TryGetPose(out Pose leftPalmPose)  ||
            !rightHand.GetJoint(_palmJoint).TryGetPose(out Pose rightPalmPose))
        {
            Debug.LogWarning("[GoalCalibrator] Não foi possível obter poses das palmas. Tente novamente.");
            return;
        }

        float measuredSpan = Vector3.Distance(leftPalmPose.position, rightPalmPose.position);
        float goalWidth    = Mathf.Clamp(measuredSpan, minGoalWidth, maxGoalWidth);

        // Altura com min/max próprios: interpola conforme a posição da largura no intervalo.
        // (largura no mínimo → altura mínima; largura no máximo → altura máxima)
        float t = (maxGoalWidth - minGoalWidth) > 0.001f
            ? Mathf.InverseLerp(minGoalWidth, maxGoalWidth, goalWidth)
            : 0f;
        float goalHeight = Mathf.Lerp(minGoalHeight, maxGoalHeight, t);

        ApplyGoalDimensions(goalWidth, goalHeight);

        Debug.Log($"[GoalCalibrator] Calibrado! Envergadura={measuredSpan:F2}m → " +
                  $"Gol {goalWidth:F2}m × {goalHeight:F2}m");

        _isCalibrated = true;
        HideInstructionShowField();
        OnGoalCalibrated?.Invoke(goalWidth);
    }

    private void ApplyGoalDimensions(float width, float height)
    {
        if (goalRoot == null) return;

        Vector3 scale = goalRoot.localScale;

        // Largura: usamos a ABERTURA entre as traves como referência, não a largura
        // total do modelo. Subtraímos a margem de geometria extra (widthMargin) da
        // largura de referência, então a abertura calibrada bate com a envergadura
        // e a mão alcança as traves.
        float openingReference = Mathf.Max(0.1f, goalPrefabReferenceWidth - widthMargin);
        scale.x = width / openingReference;

        // Altura: escala absoluta a partir da altura de referência do modelo.
        if (goalPrefabReferenceHeight > 0.001f)
            scale.y = height / goalPrefabReferenceHeight;

        goalRoot.localScale = scale;
    }

    [Header("Prefab (medidas com localScale = 1,1,1)")]
    [Tooltip("Largura TOTAL do modelo do gol quando localScale = (1,1,1), em metros.")]
    public float goalPrefabReferenceWidth = 7.32f; // padrão FIFA

    [Tooltip("Altura do modelo do gol quando localScale = (1,1,1), em metros.")]
    public float goalPrefabReferenceHeight = 2.44f; // padrão FIFA

    private void HideInstructionShowField()
    {
        if (instructionCanvas != null) instructionCanvas.SetActive(false);
        foreach (var go in objectsToEnableOnCalibration)
            if (go != null) go.SetActive(true);
    }

    // -------------------------------------------------------------------------
    // API pública
    // -------------------------------------------------------------------------

    /// <summary>Força recalibração programaticamente (ex.: botão de menu).</summary>
    public void ForceRecalibrate()
    {
        _isCalibrated = false;
        _bothFistTimer = 0f;
        if (instructionCanvas != null) instructionCanvas.SetActive(true);
    }

    /// <summary>Retorna se o gol já foi calibrado nesta sessão.</summary>
    public bool IsCalibrated => _isCalibrated;

    // -------------------------------------------------------------------------
    // Gizmos (Editor) — desenha os limites mín/máx no Scene View
    // -------------------------------------------------------------------------
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (goalRoot == null) return;

        Vector3 center = goalRoot.position;

        // Abertura mínima (largura × altura mínimas)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(
            center + Vector3.up * (minGoalHeight * 0.5f),
            new Vector3(minGoalWidth, minGoalHeight, 0.2f));

        // Abertura máxima (largura × altura máximas)
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(
            center + Vector3.up * (maxGoalHeight * 0.5f),
            new Vector3(maxGoalWidth, maxGoalHeight, 0.2f));

        UnityEditor.Handles.Label(center + Vector3.right * (maxGoalWidth * 0.5f + 0.3f),
            $"Max: {maxGoalWidth}×{maxGoalHeight}m");
        UnityEditor.Handles.Label(center + Vector3.right * (minGoalWidth * 0.5f + 0.3f),
            $"Min: {minGoalWidth}×{minGoalHeight}m");
    }
#endif
}
