using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Fase 3 — Atacante que DRIBLA a bola até uma linha de chute e finaliza (Goalkeeper VR)
///
/// O atacante carrega a bola nos pés (kinematic) enquanto corre via NavMesh em direção
/// ao gol e finaliza ao cruzar a "linha de chute" (ou ao chegar perto do gol).
///
/// Robustez: valida que está sobre a NavMesh, "gruda" o destino na malha (SamplePosition),
/// e expõe um painel de debug para diagnóstico no build.
///
/// Estados: Idle → Dribbling → Shooting → WaitingForReset
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyShooter : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Referências")]
    public Rigidbody ball;
    public Transform postLeft;
    public Transform postRight;

    [Header("Linha de chute")]
    [Tooltip("Transform opcional: ao cruzar o Z deste ponto (sentido -Z), chuta. " +
             "Se vazio, usa shootingDistanceFromGoal.")]
    public Transform shootingLine;
    [Tooltip("Se shootingLine vazio: distância (m) à frente do gol onde o atacante chuta.")]
    public float shootingDistanceFromGoal = 7f;

    [Header("Drible")]
    public float runSpeed = 5f;
    [Tooltip("Distância (m) que a bola fica à frente dos pés durante o drible.")]
    public float dribbleOffset = 0.5f;
    [Tooltip("Altura do centro da bola acima do chão (raio da bola).")]
    public float ballRadius = 0.11f;

    [Header("Dificuldade do chute")]
    [Range(5f, 30f)] public float shotSpeed = 15f;
    [Range(0f, 0.5f)] public float horizontalVariance = 0.05f;
    public float minTargetHeight = 0.3f;
    public float maxTargetHeight = 1.8f;

    [Header("Eventos")]
    public UnityEvent OnShootStart;
    public UnityEvent<Vector3> OnShootExecuted;

    [Header("DEBUG (opcional)")]
    public TMP_Text debugText;

    // -------------------------------------------------------------------------
    // Estado
    // -------------------------------------------------------------------------
    private enum State { Idle, Dribbling, Shooting, WaitingForReset }
    private State _state = State.Idle;

    private NavMeshAgent _agent;
    private Vector3 _postLeftPos, _postRightPos;
    private bool _postsSet = false;
    private bool _startedBehindLine = false;

    private void Awake() => _agent = GetComponent<NavMeshAgent>();

    private void Start()
    {
        _agent.speed = runSpeed;
        CachePosts();
    }

    private void Update()
    {
        UpdateDebug();

        if (_state != State.Dribbling) return;

        DriveBallAtFeet();

        if (ShouldShoot())
            ExecuteShot();
    }

    // -------------------------------------------------------------------------
    // Drible
    // -------------------------------------------------------------------------
    private void DriveBallAtFeet()
    {
        if (ball == null) return;

        Vector3 fwd = _agent.velocity.sqrMagnitude > 0.01f
            ? _agent.velocity.normalized
            : transform.forward;

        Vector3 feet = transform.position;
        feet.y = 0f; // assume chão em y=0
        ball.transform.position = feet + fwd * dribbleOffset + Vector3.up * ballRadius;
    }

    private bool ShouldShoot()
    {
        if (_startedBehindLine)
            return transform.position.z <= LineZ();

        // Começou já à frente da linha → chuta ao chegar perto do destino (gol)
        return !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f;
    }

    private float LineZ() =>
        shootingLine != null ? shootingLine.position.z
                             : GoalCenter().z + shootingDistanceFromGoal;

    // -------------------------------------------------------------------------
    // Chute
    // -------------------------------------------------------------------------
    private void ExecuteShot()
    {
        _state = State.Shooting;
        if (_agent.isOnNavMesh) { _agent.ResetPath(); _agent.velocity = Vector3.zero; }

        if (ball == null || !_postsSet)
        {
            Debug.LogWarning("[EnemyShooter] Bola ou traves não configuradas.");
            _state = State.WaitingForReset;
            return;
        }

        OnShootStart?.Invoke();

        ball.isKinematic = false;
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;

        Vector3 target = CalculateTarget();
        Vector3 velocity = CalculateShotVelocity(ball.transform.position, target, shotSpeed);
        ball.AddForce(velocity, ForceMode.VelocityChange);

        OnShootExecuted?.Invoke(velocity);
        Debug.Log($"[EnemyShooter] Chute! alvo={target}, vel={velocity.magnitude:F1} m/s");

        _state = State.WaitingForReset;
    }

    private Vector3 CalculateTarget()
    {
        float t = Random.Range(0f + horizontalVariance, 1f - horizontalVariance);
        Vector3 target = Vector3.Lerp(_postLeftPos, _postRightPos, t);
        target.y = Mathf.Min(_postLeftPos.y, _postRightPos.y) + Random.Range(minTargetHeight, maxTargetHeight);
        return target;
    }

    private Vector3 CalculateShotVelocity(Vector3 origin, Vector3 target, float speed)
    {
        Vector3 direction = (target - origin).normalized;
        float dist = Vector3.Distance(origin, target);
        float tFlight = dist / Mathf.Max(speed, 0.1f);
        float gComp = 0.5f * Mathf.Abs(Physics.gravity.y) * tFlight;
        direction.y += gComp / speed;
        return direction.normalized * speed;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private void CachePosts()
    {
        if (postLeft != null)  _postLeftPos  = postLeft.position;
        if (postRight != null) _postRightPos = postRight.position;
        _postsSet = postLeft != null && postRight != null;
    }

    private Vector3 GoalCenter() => (_postLeftPos + _postRightPos) * 0.5f;

    private void UpdateDebug()
    {
        if (debugText == null) return;
        debugText.text =
            $"Estado: {_state}\n" +
            $"Na NavMesh: {(_agent != null && _agent.isOnNavMesh)}\n" +
            $"PathPending: {(_agent != null && _agent.pathPending)}\n" +
            $"Restante: {(_agent != null ? _agent.remainingDistance.ToString("F1") : "-")}\n" +
            $"Veloc: {(_agent != null ? _agent.velocity.magnitude.ToString("F1") : "-")}\n" +
            $"z={transform.position.z:F1}  lineZ={LineZ():F1}\n" +
            $"Traves OK: {_postsSet}";
    }

    // -------------------------------------------------------------------------
    // API pública
    // -------------------------------------------------------------------------
    public void BeginRun()
    {
        CachePosts();
        if (!_postsSet)
        {
            Debug.LogError("[EnemyShooter] Traves (Post Left/Right) não atribuídas. Atacante não corre.");
            return;
        }

        // Garante que o agente está sobre a NavMesh
        if (!_agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            else
            {
                Debug.LogError("[EnemyShooter] Atacante NÃO está sobre a NavMesh. " +
                               "Verifique o bake e a posição do spawn.");
                return;
            }
        }

        if (ball != null)
        {
            ball.transform.SetParent(null);
            ball.isKinematic = true;
        }

        _agent.speed = runSpeed;
        _agent.isStopped = false;

        // Destino: centro do gol, "grudado" na NavMesh (área do gol pode não ser caminhável)
        Vector3 dest = GoalCenter();
        dest.y = transform.position.y;
        if (NavMesh.SamplePosition(dest, out var dHit, 6f, NavMesh.AllAreas))
            dest = dHit.position;

        bool ok = _agent.SetDestination(dest);
        if (!ok)
            Debug.LogWarning("[EnemyShooter] SetDestination falhou — destino inalcançável na NavMesh.");

        _startedBehindLine = transform.position.z > LineZ();
        _state = State.Dribbling;
        Debug.Log($"[EnemyShooter] Drible iniciado. destino={dest}, startedBehindLine={_startedBehindLine}");
    }

    public void OnGoalCalibratedHandler(float newWidth)
    {
        CachePosts();
        Debug.Log($"[EnemyShooter] Traves atualizadas. Largura={newWidth:F2}m");
    }

    public void ResetToIdle(Vector3 spawnPosition)
    {
        _state = State.Idle;
        if (_agent != null && _agent.isOnNavMesh) _agent.Warp(spawnPosition);
        else transform.position = spawnPosition;
    }

    public bool IsWaitingForReset => _state == State.WaitingForReset;
    public bool IsIdle             => _state == State.Idle;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        float lineZ = shootingLine != null ? shootingLine.position.z
            : (postLeft != null && postRight != null
                ? ((postLeft.position + postRight.position) * 0.5f).z + shootingDistanceFromGoal
                : transform.position.z);
        Gizmos.DrawLine(new Vector3(transform.position.x - 10f, 0.05f, lineZ),
                        new Vector3(transform.position.x + 10f, 0.05f, lineZ));
    }
#endif
}
