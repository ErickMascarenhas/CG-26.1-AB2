using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

/// <summary>
/// Fase 2 — Agarre e Espalmar por Hand Tracking (Goalkeeper VR)
///
/// ABORDAGEM SIMPLIFICADA (sem precisar de ossos individuais nem do Hand Visualizer):
/// este script lê a pose da palma direto do XRHandSubsystem e FAZ O PRÓPRIO
/// GameObject seguir a palma a cada frame. O SphereCollider trigger (criado
/// automaticamente se não atribuído) acompanha a mão e detecta a bola.
///
/// Como usar:
///   1. Crie um GameObject vazio "LeftHandCatcher" e outro "RightHandCatcher".
///      (podem ficar na raiz da cena ou sob o XR Origin — a posição é controlada por código)
///   2. Adicione este script em cada um; marque isLeftHand no esquerdo.
///   3. NÃO precisa adicionar Rigidbody nem SphereCollider à mão: o script cria.
///   4. A bola precisa de Rigidbody (não-cinemático) e Tag = "Ball".
///   5. Conecte os eventos OnCatch/OnParry/OnThrow ao GameManager.
///
/// Lógica:
///   - Bola entra no trigger da palma + mão fechando ≥ catchStartThreshold → agarre.
///   - Janela de tolerância: mantém o agarre por até toleranceWindow segundos mesmo
///     com perda momentânea de tracking (usa a última pose conhecida da palma).
///   - Solta ao abrir a mão (< releaseThreshold) ou em gesto de arremesso (velocidade).
///   - Bola toca a palma com a mão aberta → evento OnParry (espalmar); a física nativa rebate.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HandTrackingCatch : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Configuração da mão")]
    public bool isLeftHand = false;

    [Header("Espaço de rastreamento")]
    [Tooltip("Transform do XR Origin (Camera Offset). As poses das mãos vêm nesse espaço; " +
             "se vazio, é encontrado automaticamente. Necessário para a mão ficar no lugar certo no mundo.")]
    public Transform xrOriginTransform;

    [Header("Collider da palma")]
    [Tooltip("SphereCollider trigger da palma. Se vazio, é criado automaticamente.")]
    public SphereCollider palmTrigger;
    [Tooltip("Raio do collider da palma (m) se criado automaticamente. Aumente se estiver difícil agarrar.")]
    public float palmTriggerRadius = 0.16f;

    [Header("Rebatida física (espalmar de verdade)")]
    [Tooltip("Collider SÓLIDO que defletie a bola com a mão aberta. Auto-criado se vazio.")]
    public SphereCollider parryCollider;
    [Tooltip("Raio do collider de rebatida (fração do raio do trigger).")]
    public float parryRadiusFactor = 0.85f;
    [Tooltip("Quanto da velocidade da mão é transferida ao rebater (efeito 'soco').")]
    public float parryPunch = 1.0f;
    [Tooltip("Quique mínimo da rebatida (velocidade de saída mínima, m/s).")]
    public float parryMinSpeed = 3f;
    [Tooltip("Fração da velocidade de entrada preservada ao rebater.")]
    public float parryBounciness = 0.6f;

    [Header("Thresholds")]
    [Range(0f, 1f)]
    [Tooltip("Fechamento mínimo para iniciar o agarre (0=aberta, 1=punho).")]
    public float catchStartThreshold = 0.55f;
    [Range(0f, 1f)]
    [Tooltip("Fechamento abaixo do qual a bola é solta.")]
    public float releaseThreshold = 0.30f;
    [Tooltip("Velocidade mínima (m/s) para considerar gesto de arremesso.")]
    public float throwSpeedThreshold = 2.5f;
    [Tooltip("Janela de tolerância de tracking (s) — mantém o agarre com perda momentânea.")]
    public float toleranceWindow = 0.3f;
    [Tooltip("Multiplicador de força aplicado à velocidade de arremesso.")]
    public float throwForceMultiplier = 1.2f;

    [Header("Feedback (Fase 4)")]
    [Tooltip("Intensidade do 'squash' da bola ao agarrar (0 = desligado).")]
    [Range(0f, 0.5f)] public float squashAmount = 0.2f;
    [Tooltip("Duração do squash em segundos.")]
    public float squashDuration = 0.15f;

    [Header("Eventos")]
    public UnityEvent OnCatch;
    public UnityEvent OnParry;
    public UnityEvent<Vector3> OnThrow;

    [Header("DEBUG (opcional)")]
    [Tooltip("TextMeshPro World-Space para status ao vivo no headset.")]
    public TMP_Text debugText;

    // -------------------------------------------------------------------------
    // Estado interno
    // -------------------------------------------------------------------------
    private XRHandSubsystem _handSubsystem;
    private Rigidbody _rb;

    private Rigidbody _caughtBall;
    private bool _isHolding = false;

    private Rigidbody _ballInRange;   // bola sobreposta à palma neste momento
    private bool _parryFiredThisTouch; // evita disparar parry repetido no mesmo toque

    private float _trackingLossTimer = 0f;
    private bool _trackingLost = false;
    private Pose _lastKnownPalmPose;
    private bool _hasPalmPose = false;

    private Vector3 _prevPalmPos;
    private Vector3 _currentPalmPos;

    private const float OpenRatio   = 2.0f;
    private const float ClosedRatio = 0.9f;

    private static readonly XRHandJointID _palmJoint = XRHandJointID.Palm;
    private static readonly XRHandJointID[] _fingertips =
    {
        XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
        XRHandJointID.RingTip,  XRHandJointID.LittleTip
    };
    private static readonly XRHandJointID[] _fingerBases =
    {
        XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal,
        XRHandJointID.RingProximal,  XRHandJointID.LittleProximal
    };

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity  = false;

        // Cria o collider da palma (trigger) automaticamente se não foi atribuído
        if (palmTrigger == null)
        {
            palmTrigger = gameObject.AddComponent<SphereCollider>();
            palmTrigger.isTrigger = true;
            palmTrigger.radius = palmTriggerRadius;
        }
        else
        {
            palmTrigger.isTrigger = true;
        }

        // Cria o collider SÓLIDO de rebatida (espalmar físico)
        if (parryCollider == null)
        {
            parryCollider = gameObject.AddComponent<SphereCollider>();
        }
        parryCollider.isTrigger = false;
        parryCollider.radius = palmTriggerRadius * parryRadiusFactor;
        parryCollider.enabled = false; // ligado só quando a mão está aberta
    }

    private void Start()
    {
        TryAcquireSubsystem();
        ResolveTrackingSpace();
    }

    /// <summary>
    /// Descobre o transform que converte as poses (espaço do XR Origin) para o MUNDO.
    /// As juntas das mãos vêm relativas ao Camera Offset do XR Origin; sem isso o
    /// collider nasceria perto da origem do mundo, "solto" da mão.
    /// </summary>
    private void ResolveTrackingSpace()
    {
        if (xrOriginTransform != null) return;

        var origin = FindObjectOfType<XROrigin>();
        if (origin != null)
        {
            xrOriginTransform = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject.transform
                : origin.transform;
        }
        else
        {
            Debug.LogWarning("[HandTrackingCatch] XR Origin não encontrado — " +
                             "atribua 'XR Origin Transform' manualmente (Camera Offset).");
        }
    }

    /// <summary>Converte uma pose do espaço do XR Origin para o espaço de mundo.</summary>
    private Pose ToWorld(Pose local)
    {
        if (xrOriginTransform == null) return local;
        return new Pose(
            xrOriginTransform.TransformPoint(local.position),
            xrOriginTransform.rotation * local.rotation);
    }

    private void TryAcquireSubsystem()
    {
        var list = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(list);
        if (list.Count > 0) { _handSubsystem = list[0]; return; }

        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        _handSubsystem = loader?.GetLoadedSubsystem<XRHandSubsystem>();
    }

    private void Update()
    {
        if (_handSubsystem == null)
        {
            TryAcquireSubsystem();
            UpdateDebug(false, 0f);
            return;
        }

        XRHand hand = isLeftHand ? _handSubsystem.leftHand : _handSubsystem.rightHand;

        // ── Tracking ────────────────────────────────────────────────────────
        if (hand.isTracked && hand.GetJoint(_palmJoint).TryGetPose(out Pose palmPoseLocal))
        {
            // Converte do espaço do XR Origin para o MUNDO (corrige o collider "solto").
            Pose palmPose = ToWorld(palmPoseLocal);

            _trackingLost = false;
            _trackingLossTimer = 0f;
            _hasPalmPose = true;

            _prevPalmPos = _currentPalmPos;
            _currentPalmPos = palmPose.position;
            _lastKnownPalmPose = palmPose;

            // O objeto da mão SEGUE a palma → o trigger/collider acompanham a mão real
            transform.SetPositionAndRotation(palmPose.position, palmPose.rotation);
        }
        else
        {
            if (!_trackingLost) { _trackingLost = true; _trackingLossTimer = 0f; }
            _trackingLossTimer += Time.deltaTime;

            if (_isHolding && _trackingLossTimer <= toleranceWindow)
                MoveBallToLastPose();                       // mantém agarre na janela
            else if (_isHolding && _trackingLossTimer > toleranceWindow)
                DropBall();                                 // perdeu por tempo demais

            // Sem tracking, não rebate (mão "congelada" não deve socar a bola).
            if (parryCollider != null) parryCollider.enabled = false;

            UpdateDebug(false, 0f);
            return;
        }

        // ── Closedness ──────────────────────────────────────────────────────
        float closedness = ComputeHandClosedness(hand);
        UpdateDebug(true, closedness);

        // Collider sólido de rebatida LIGADO só com a mão ABERTA (e sem bola).
        // Ao fechar para agarrar, ele desliga e a bola entra no trigger de agarre.
        bool wantSolid = !_isHolding && closedness < catchStartThreshold;
        if (parryCollider != null && parryCollider.enabled != wantSolid)
            parryCollider.enabled = wantSolid;

        if (_isHolding)
        {
            MoveBallToLastPose();

            Vector3 handVelocity = (_currentPalmPos - _prevPalmPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            if (handVelocity.magnitude >= throwSpeedThreshold && closedness < catchStartThreshold)
                ThrowBall(handVelocity);
            else if (closedness < releaseThreshold)
                DropBall();
        }
        else if (_ballInRange != null && closedness >= catchStartThreshold)
        {
            // CATCH-ASSIST: a bola está na palma e a mão fechou → agarra agora,
            // independente de em qual frame a bola entrou no trigger.
            CatchBall(_ballInRange);
        }
    }

    // -------------------------------------------------------------------------
    // Rebatida física (espalmar) — colisão do collider sólido com a bola
    // -------------------------------------------------------------------------
    private void OnCollisionEnter(Collision collision)
    {
        if (_isHolding) return;
        if (!collision.collider.CompareTag("Ball")) return;
        var ballRb = collision.rigidbody;
        if (ballRb == null) return;

        // Velocidade atual da mão (para "socar" a bola na direção do movimento).
        Vector3 handVel = (_currentPalmPos - _prevPalmPos) / Mathf.Max(Time.deltaTime, 0.0001f);

        // Direção de saída = da palma para a bola (sempre para longe da mão), com leve alça.
        Vector3 pushDir = ballRb.transform.position - transform.position;
        pushDir.y = Mathf.Max(pushDir.y, 0.05f);
        if (pushDir.sqrMagnitude < 0.0001f) pushDir = Vector3.up;
        pushDir.Normalize();

        float incoming = collision.relativeVelocity.magnitude;
        float speed = Mathf.Max(incoming * parryBounciness, parryMinSpeed);

        ballRb.linearVelocity = pushDir * speed + handVel * parryPunch;

        if (!_parryFiredThisTouch)
        {
            _parryFiredThisTouch = true;
            OnParry?.Invoke();
        }
        Debug.Log("[HandTrackingCatch] Rebatida (espalmar físico).");
    }

    // -------------------------------------------------------------------------
    // Trigger da palma
    // -------------------------------------------------------------------------
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;
        var ballRb = other.attachedRigidbody;
        if (ballRb == null) return;

        _ballInRange = ballRb;
        _parryFiredThisTouch = false;

        if (_isHolding) return;

        float closedness = ComputeCurrentClosedness();
        if (closedness >= catchStartThreshold)
        {
            CatchBall(ballRb);
        }
        else if (!_parryFiredThisTouch)
        {
            // Mão aberta tocou a bola → espalmar. (O agarre ainda é possível pelo
            // catch-assist no Update se a mão fechar enquanto a bola continua na palma.)
            _parryFiredThisTouch = true;
            OnParry?.Invoke();
            Debug.Log("[HandTrackingCatch] Espalmar detectado.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Ball")) return;
        var ballRb = other.attachedRigidbody;
        if (ballRb != null && ballRb == _ballInRange)
        {
            _ballInRange = null;
            _parryFiredThisTouch = false;
        }
    }

    // -------------------------------------------------------------------------
    // Agarre / soltar / arremesso
    // -------------------------------------------------------------------------
    private void CatchBall(Rigidbody ballRb)
    {
        _caughtBall = ballRb;
        _isHolding  = true;

        _caughtBall.isKinematic = true;
        _caughtBall.transform.SetParent(transform);
        if (_hasPalmPose) _caughtBall.transform.position = _lastKnownPalmPose.position;

        if (squashAmount > 0f)
            StartCoroutine(SquashBall(_caughtBall.transform));

        OnCatch?.Invoke();
        Debug.Log("[HandTrackingCatch] Bola agarrada.");
    }

    private void DropBall()
    {
        if (_caughtBall == null) { _isHolding = false; return; }

        _caughtBall.transform.SetParent(null);
        _caughtBall.isKinematic = false;
        _caughtBall = null;
        _isHolding  = false;
        Debug.Log("[HandTrackingCatch] Bola solta (mão aberta).");
    }

    private void ThrowBall(Vector3 handVelocity)
    {
        if (_caughtBall == null) { _isHolding = false; return; }

        _caughtBall.transform.SetParent(null);
        _caughtBall.isKinematic = false;

        Vector3 throwVelocity = handVelocity * throwForceMultiplier;
        _caughtBall.linearVelocity = throwVelocity;

        OnThrow?.Invoke(throwVelocity);
        Debug.Log($"[HandTrackingCatch] Arremesso! vel={throwVelocity.magnitude:F2} m/s");

        _caughtBall = null;
        _isHolding  = false;
    }

    private void MoveBallToLastPose()
    {
        if (_caughtBall == null || !_hasPalmPose) return;
        _caughtBall.transform.SetPositionAndRotation(
            _lastKnownPalmPose.position, _lastKnownPalmPose.rotation);
    }

    /// <summary>Squash-and-stretch da bola ao agarrar (achata e volta).</summary>
    private System.Collections.IEnumerator SquashBall(Transform ballT)
    {
        if (ballT == null) yield break;

        Vector3 original = ballT.localScale;
        Vector3 squashed = new Vector3(
            original.x * (1f + squashAmount),
            original.y * (1f - squashAmount),
            original.z * (1f + squashAmount));

        float half = Mathf.Max(0.01f, squashDuration * 0.5f);

        float t = 0f;
        while (t < half && ballT != null)
        {
            t += Time.deltaTime;
            ballT.localScale = Vector3.Lerp(original, squashed, t / half);
            yield return null;
        }
        t = 0f;
        while (t < half && ballT != null)
        {
            t += Time.deltaTime;
            ballT.localScale = Vector3.Lerp(squashed, original, t / half);
            yield return null;
        }
        if (ballT != null) ballT.localScale = original;
    }

    // -------------------------------------------------------------------------
    // Closedness (mesma métrica do GoalCalibrator; polegar excluído)
    // -------------------------------------------------------------------------
    private float ComputeCurrentClosedness()
    {
        if (_handSubsystem == null) return 0f;
        XRHand hand = isLeftHand ? _handSubsystem.leftHand : _handSubsystem.rightHand;
        return ComputeHandClosedness(hand);
    }

    private float ComputeHandClosedness(XRHand hand)
    {
        if (!hand.GetJoint(_palmJoint).TryGetPose(out Pose palmPose)) return 0f;

        float total = 0f;
        int count = 0;
        for (int i = 0; i < _fingertips.Length; i++)
        {
            if (!hand.GetJoint(_fingertips[i]).TryGetPose(out Pose tipPose))   continue;
            if (!hand.GetJoint(_fingerBases[i]).TryGetPose(out Pose basePose)) continue;

            float baseToPalm = Vector3.Distance(basePose.position, palmPose.position);
            if (baseToPalm < 0.001f) continue;

            float ratio  = Vector3.Distance(tipPose.position, palmPose.position) / baseToPalm;
            float closed = Mathf.Clamp01((OpenRatio - ratio) / (OpenRatio - ClosedRatio));
            total += closed;
            count++;
        }
        return count > 0 ? total / count : 0f;
    }

    private void UpdateDebug(bool tracked, float closedness)
    {
        if (debugText == null) return;
        string hand = isLeftHand ? "Esquerda" : "Direita";
        if (_handSubsystem == null)
        {
            debugText.text = $"[{hand}] SUBSYSTEM: <color=red>NAO</color>";
            return;
        }
        string t = tracked ? "<color=green>OK</color>" : "<color=red>NAO</color>";
        debugText.text =
            $"[{hand}] rastreada: {t}\n" +
            $"Fechamento: {closedness:F2}  (agarra >= {catchStartThreshold:F2})\n" +
            $"Segurando: {_isHolding}";
    }

    // -------------------------------------------------------------------------
    // API pública
    // -------------------------------------------------------------------------
    public void ForceDrop() => DropBall();
    public bool IsHolding => _isHolding;
}
