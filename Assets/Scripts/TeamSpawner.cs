using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Fase 6 — Instancia os dois times em 4-3-3 (Goalkeeper VR).
///
/// Cria 10 jogadores de campo por time (o goleiro aliado é o HUMANO, então não
/// é instanciado) + 1 goleiro inimigo estático. Total de 21 agentes de IA.
///
/// O TeamSpawner é chamado pelo MatchManager (ou roda no Start) e devolve as
/// listas de jogadores já posicionados, coloridos e com papéis atribuídos.
///
/// Pré-requisitos:
///   - playerPrefab: seu modelo simples de jogador. PRECISA conter (ou o spawner
///     adiciona): NavMeshAgent e SoccerPlayer. Tenha um Renderer para a cor.
///   - NavMesh já assada cobrindo o campo.
/// </summary>
public class TeamSpawner : MonoBehaviour
{
    [Header("Prefabs por time (um modelo por time)")]
    [Tooltip("Modelo do time ALIADO (ex.: prefab 'Attacker'). NavMeshAgent/SoccerPlayer são adicionados se faltarem.")]
    public GameObject allyPrefab;
    [Tooltip("Modelo do time INIMIGO (ex.: prefab 'Attacker Enemy'). Se vazio, usa o allyPrefab.")]
    public GameObject enemyPrefab;
    [Tooltip("Fallback legado: usado se allyPrefab/enemyPrefab estiverem vazios.")]
    public GameObject playerPrefab;

    [Header("Campo")]
    [Tooltip("Centro do campo.")]
    public Transform fieldCenter;
    [Tooltip("Comprimento do campo (eixo Z).")]
    public float fieldLength = 30f;
    [Tooltip("Largura do campo (eixo X).")]
    public float fieldWidth = 18f;

    [Tooltip("O time ALIADO ataca no sentido +Z? (o inimigo ataca o gol do jogador no -Z)")]
    public bool allyAttacksPositiveZ = true;

    [Header("Goleiro inimigo (estático, sem IA)")]
    [Tooltip("Se você já tem um modelo de goleiro inimigo na cena, arraste aqui. Senão, é instanciado do prefab.")]
    public GameObject enemyGoalkeeper;

    [Header("Tingimento (opcional)")]
    [Tooltip("Se DESLIGADO (padrão): cada time usa o material do próprio modelo — sem mudar cor de camisa/pele. Ligue só se usar o mesmo modelo para os dois times.")]
    public bool tintTeams = false;

    [Header("Cores dos times (camisa) — usado só se 'Tint Teams' ligado")]
    public Color allyColor = new Color(0.2f, 0.4f, 1f);   // azul
    public Color enemyColor = new Color(1f, 0.25f, 0.2f); // vermelho

    [Header("Tons de pele (sorteados) — usado só se 'Tint Teams' ligado")]
    public Color[] skinTones = new[]
    {
        new Color(0.96f, 0.80f, 0.69f),
        new Color(0.86f, 0.66f, 0.52f),
        new Color(0.71f, 0.52f, 0.40f),
        new Color(0.55f, 0.39f, 0.29f),
        new Color(0.40f, 0.27f, 0.20f),
    };

    [Header("Velocidade")]
    public float playerSpeed = 5f;

    // Resultado do spawn
    [HideInInspector] public List<SoccerPlayer> allyPlayers = new List<SoccerPlayer>();
    [HideInInspector] public List<SoccerPlayer> enemyPlayers = new List<SoccerPlayer>();

    /// <summary>
    /// Instancia ambos os times. Retorna true se ok.
    /// O goleiro aliado (humano) NÃO é instanciado.
    /// </summary>
    /// <summary>Prefab efetivo de um time (com fallback para o playerPrefab legado).</summary>
    private GameObject PrefabFor(Team team)
    {
        GameObject specific = team == Team.Ally ? allyPrefab : enemyPrefab;
        if (specific != null) return specific;
        if (allyPrefab != null) return allyPrefab; // se só o aliado foi atribuído, usa-o p/ ambos
        return playerPrefab;
    }

    public bool SpawnTeams()
    {
        if (PrefabFor(Team.Ally) == null || PrefabFor(Team.Enemy) == null)
        {
            Debug.LogError("[TeamSpawner] Prefabs de time não atribuídos (Ally Prefab / Enemy Prefab). Não é possível spawnar times.");
            return false;
        }

        Vector3 center = fieldCenter != null ? fieldCenter.position : transform.position;

        allyPlayers.Clear();
        enemyPlayers.Clear();

        // Ally: ataca no sentido escolhido. Não instancia o goleiro (é o humano).
        var allyFormation = AISoccerBrain.Build433(center, fieldLength, fieldWidth, allyAttacksPositiveZ);
        foreach (var (role, pos) in allyFormation)
        {
            if (role == Role.Goalkeeper) continue; // humano
            var p = SpawnOne(Team.Ally, role, pos, allyColor);
            if (p != null) allyPlayers.Add(p);
        }

        // Enemy: ataca no sentido oposto (em direção ao gol do jogador).
        var enemyFormation = AISoccerBrain.Build433(center, fieldLength, fieldWidth, !allyAttacksPositiveZ);
        foreach (var (role, pos) in enemyFormation)
        {
            if (role == Role.Goalkeeper)
            {
                SetupEnemyGoalkeeper(pos);
                continue;
            }
            var p = SpawnOne(Team.Enemy, role, pos, enemyColor);
            if (p != null) enemyPlayers.Add(p);
        }

        Debug.Log($"[TeamSpawner] Spawn ok. Aliados={allyPlayers.Count}, Inimigos={enemyPlayers.Count} (+1 GK inimigo). Tint={tintTeams}.");
        return true;
    }

    private SoccerPlayer SpawnOne(Team team, Role role, Vector3 pos, Color color)
    {
        // "Gruda" o spawn na NavMesh
        Vector3 spawnPos = pos;
        if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
            spawnPos = hit.position;

        var go = Instantiate(PrefabFor(team), spawnPos, Quaternion.identity, transform);

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent == null) agent = go.AddComponent<NavMeshAgent>();
        agent.speed = playerSpeed;
        agent.radius = 0.3f;
        agent.height = 1.8f;
        agent.angularSpeed = 720f;
        agent.acceleration = 20f;

        var sp = go.GetComponent<SoccerPlayer>();
        if (sp == null) sp = go.AddComponent<SoccerPlayer>();

        if (tintTeams)
        {
            // Modo legado: mesmo modelo p/ os dois times, tingido por instância.
            Color skin = (skinTones != null && skinTones.Length > 0)
                ? skinTones[Random.Range(0, skinTones.Length)]
                : new Color(0.86f, 0.66f, 0.52f);
            sp.Init(team, role, spawnPos, color, skin, playerSpeed);
        }
        else
        {
            // Um modelo por time: NÃO mexe em cor de camisa/pele (cada prefab já tem a sua).
            sp.InitNoTint(team, role, spawnPos, playerSpeed);
        }

        return sp;
    }

    private void SetupEnemyGoalkeeper(Vector3 pos)
    {
        Vector3 gkPos = pos;
        if (enemyGoalkeeper != null)
        {
            enemyGoalkeeper.transform.position = gkPos;
            return;
        }
        // Instancia um goleiro estático simples (sem NavMeshAgent ativo / sem IA)
        var go = Instantiate(PrefabFor(Team.Enemy), gkPos, Quaternion.identity, transform);
        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false; // estático
        if (tintTeams)
        {
            var sp = go.GetComponent<SoccerPlayer>();
            if (sp != null) sp.SetTeamColor(enemyColor);
        }
        go.name = "Enemy_Goalkeeper(static)";
        enemyGoalkeeper = go;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 c = fieldCenter != null ? fieldCenter.position : transform.position;
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(c, new Vector3(fieldWidth, 0.1f, fieldLength));
    }
#endif
}
