using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Fase 5/6/7 — Agente jogador de futebol (Goalkeeper VR).
///
/// Reúne: NavMeshAgent driver (move para alvos), condução de bola (drible),
/// passe/chute, cor de time e hooks de animação. Um por jogador de campo.
///
/// O MatchManager decide; este componente EXECUTA.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class SoccerPlayer : MonoBehaviour
{
    [Header("Identidade (preenchido pelo TeamSpawner)")]
    public Team team;
    public Role role;
    public Vector3 HomePosition;

    [Header("Movimento")]
    public float baseSpeed = 5f;

    [Header("Drible")]
    [Tooltip("Distância da bola à frente dos pés ao conduzir.")]
    public float dribbleOffset = 0.5f;
    public float ballRadius = 0.11f;

    [Header("Animação (opcional)")]
    [Tooltip("Animator do modelo. Se vazio, hooks de animação são ignorados.")]
    public Animator animator;
    public string runBool = "IsRunning";
    public string shootTrigger = "Shoot";
    public string passTrigger = "Pass";

    [Header("Aparência (2 cores: camisa + pele)")]
    [Tooltip("Renderer do modelo. Se vazio, usa o primeiro encontrado.")]
    public Renderer teamRenderer;
    [Tooltip("Índice do material/slot da CAMISA no modelo universal.")]
    public int shirtMaterialIndex = 0;
    [Tooltip("Índice do material/slot da PELE no modelo universal (-1 = não usar).")]
    public int skinMaterialIndex = 1;

    // -------------------------------------------------------------------------
    private NavMeshAgent _agent;

    public NavMeshAgent Agent => _agent;
    public bool HasBall { get; set; }

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = baseSpeed;
        if (teamRenderer == null) teamRenderer = GetComponentInChildren<Renderer>();
    }

    // -------------------------------------------------------------------------
    // Inicialização (chamada pelo TeamSpawner)
    // -------------------------------------------------------------------------
    public void Init(Team t, Role r, Vector3 home, Color shirtColor, Color skinColor, float speed)
    {
        team = t;
        role = r;
        HomePosition = home;
        baseSpeed = speed;
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        _agent.speed = speed;

        WarpTo(home);
        SetAppearance(shirtColor, skinColor);
        name = $"{t}_{r}";
    }

    /// <summary>
    /// Inicialização SEM tingir (um modelo por time): mantém os materiais do prefab.
    /// </summary>
    public void InitNoTint(Team t, Role r, Vector3 home, float speed)
    {
        team = t;
        role = r;
        HomePosition = home;
        baseSpeed = speed;
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        _agent.speed = speed;

        WarpTo(home);
        name = $"{t}_{r}";
    }

    // -------------------------------------------------------------------------
    // NavMesh driver
    // -------------------------------------------------------------------------
    /// <summary>Define o destino do agente, "grudando" na NavMesh.</summary>
    public void MoveTo(Vector3 worldTarget)
    {
        if (_agent == null || !_agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(worldTarget, out var hit, 4f, NavMesh.AllAreas))
            worldTarget = hit.position;

        _agent.isStopped = false;
        _agent.SetDestination(worldTarget);
        UpdateRunAnim(true);
    }

    public void StopMoving()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        UpdateRunAnim(false);
    }

    public void WarpTo(Vector3 pos)
    {
        if (_agent != null && _agent.isOnNavMesh) _agent.Warp(pos);
        else transform.position = pos;
        UpdateRunAnim(false);
    }

    public float CurrentSpeed => _agent != null ? _agent.velocity.magnitude : 0f;

    // -------------------------------------------------------------------------
    // Bola: drible / chute / passe
    // -------------------------------------------------------------------------
    /// <summary>Mantém a bola nos pés enquanto conduz.</summary>
    public void DribbleBall(Rigidbody ball)
    {
        if (ball == null) return;

        Vector3 fwd = _agent != null && _agent.velocity.sqrMagnitude > 0.01f
            ? _agent.velocity.normalized
            : transform.forward;

        Vector3 feet = transform.position; feet.y = 0f;
        ball.transform.position = feet + fwd * dribbleOffset + Vector3.up * ballRadius;
    }

    /// <summary>Chuta/passa a bola com uma velocidade calculada para um alvo.</summary>
    public void KickBall(Rigidbody ball, Vector3 target, float speed, bool isShot)
    {
        if (ball == null) return;

        ball.transform.SetParent(null);
        ball.isKinematic = false;
        ball.linearVelocity = Vector3.zero;
        ball.angularVelocity = Vector3.zero;

        Vector3 origin = ball.transform.position;
        Vector3 dir = (target - origin);
        float dist = dir.magnitude;
        dir = dir.normalized;

        // Compensação de gravidade simples para o alvo elevado
        float tFlight = dist / Mathf.Max(speed, 0.1f);
        dir.y += (0.5f * Mathf.Abs(Physics.gravity.y) * tFlight) / Mathf.Max(speed, 0.1f);

        ball.AddForce(dir.normalized * speed, ForceMode.VelocityChange);

        HasBall = false;
        if (isShot) PlayShoot(); else PlayPass();
    }

    // -------------------------------------------------------------------------
    // Aparência
    // -------------------------------------------------------------------------
    /// <summary>Define camisa e pele (modelo universal de 2 materiais).</summary>
    public void SetAppearance(Color shirt, Color skin)
    {
        if (teamRenderer == null) return;
        SetMaterialColor(shirtMaterialIndex, shirt);
        SetMaterialColor(skinMaterialIndex, skin);
    }

    /// <summary>Atalho: só a cor do time (camisa). Mantido por compatibilidade.</summary>
    public void SetTeamColor(Color color) => SetMaterialColor(shirtMaterialIndex, color);

    private void SetMaterialColor(int index, Color color)
    {
        if (teamRenderer == null || index < 0) return;
        if (index >= teamRenderer.sharedMaterials.Length) return;

        // MaterialPropertyBlock por slot — evita instanciar materiais
        var mpb = new MaterialPropertyBlock();
        teamRenderer.GetPropertyBlock(mpb, index);
        mpb.SetColor("_BaseColor", color); // URP Lit
        mpb.SetColor("_Color", color);     // fallback built-in/legacy
        teamRenderer.SetPropertyBlock(mpb, index);
    }

    // -------------------------------------------------------------------------
    // Animação (no-op se animator == null)
    // -------------------------------------------------------------------------
    private void UpdateRunAnim(bool running)
    {
        if (animator == null || string.IsNullOrEmpty(runBool)) return;
        animator.SetBool(runBool, running);
    }
    public void PlayShoot()
    {
        if (animator != null && !string.IsNullOrEmpty(shootTrigger)) animator.SetTrigger(shootTrigger);
    }
    public void PlayPass()
    {
        if (animator != null && !string.IsNullOrEmpty(passTrigger)) animator.SetTrigger(passTrigger);
    }

    // Mantém a animação de corrida coerente mesmo sem novos MoveTo
    private void Update()
    {
        if (animator != null && !string.IsNullOrEmpty(runBool))
            animator.SetBool(runBool, CurrentSpeed > 0.15f);
    }
}
