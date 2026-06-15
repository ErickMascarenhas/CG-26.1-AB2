using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fase 7 (assets) — Espalha a torcida na arquibancada (Goalkeeper VR).
///
/// Gera quads de torcedores em fileiras ao redor do campo (atrás dos dois gols e
/// nas duas laterais), cada um com um <see cref="CrowdMember"/> em modo MaterialSwap
/// usando os materiais idle/cheer (gerados a partir de Crowd Idle.png / Crowd Cheer.png).
///
/// Usar apenas 2 materiais (idle/cheer) compartilhados mantém o custo baixo no Quest:
/// trocar o material de um torcedor não duplica material nem afeta os demais.
///
/// Os torcedores são registrados no <see cref="CrowdManager"/> (campo 'members'),
/// que os divide em grupo GOL / DEFESA e comemora nos eventos certos.
///
/// Como usar:
///   1. Rode Tools → Goalkeeper VR → Build Crowd (gera os materiais e liga aqui).
///   2. Posicione este objeto no centro do campo (ou ligue 'Field Center').
///   3. Ajuste linhas/altura/afastamento e dê Play (ou botão "Spawn Now" no menu de contexto).
/// </summary>
public class CrowdSpawner : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Centro do campo. Se vazio, usa a posição deste objeto.")]
    public Transform fieldCenter;
    [Tooltip("CrowdManager que comemora. Se vazio, tenta achar na cena.")]
    public CrowdManager crowdManager;

    [Header("Materiais (Tools → Goalkeeper VR → Build Crowd)")]
    public Material idleMaterial;
    public Material cheerMaterial;

    [Header("Dimensões do campo (para posicionar a arquibancada)")]
    public float fieldLength = 30f; // eixo Z
    public float fieldWidth = 18f;  // eixo X
    [Tooltip("Distância da borda do campo até a primeira fileira.")]
    public float standOffset = 4f;

    [Header("Arquibancada")]
    [Tooltip("Número de fileiras em cada lado.")]
    public int rows = 6;
    [Tooltip("Espaçamento entre torcedores ao longo da fileira.")]
    public float seatSpacing = 1.4f;
    [Tooltip("Profundidade entre fileiras (recuo de cada degrau).")]
    public float rowDepth = 1.3f;
    [Tooltip("Quanto cada fileira sobe (degrau).")]
    public float rowRise = 0.9f;
    [Tooltip("Altura base da primeira fileira.")]
    public float baseHeight = 1f;

    [Header("Torcedor")]
    [Tooltip("Tamanho do quad (largura x altura), em metros.")]
    public Vector2 memberSize = new Vector2(1.0f, 1.6f);
    [Tooltip("Variação aleatória de tamanho (0 = uniforme).")]
    [Range(0f, 0.5f)] public float sizeJitter = 0.12f;

    [Header("Lados a preencher")]
    public bool behindPlayerGoal = true; // -Z
    public bool behindEnemyGoal = true;  // +Z
    public bool leftSide = true;         // -X
    public bool rightSide = true;        // +X

    [Header("Execução")]
    public bool spawnOnStart = true;

    private readonly List<CrowdMember> _spawned = new List<CrowdMember>();
    private Transform _root;

    private void Start()
    {
        if (spawnOnStart) Spawn();
    }

    [ContextMenu("Spawn Now")]
    public void Spawn()
    {
        Clear();

        if (idleMaterial == null || cheerMaterial == null)
        {
            Debug.LogError("[CrowdSpawner] Materiais idle/cheer não atribuídos. Rode Tools → Goalkeeper VR → Build Crowd.");
            return;
        }

        Vector3 center = fieldCenter != null ? fieldCenter.position : transform.position;

        _root = new GameObject("Crowd").transform;
        _root.SetParent(transform, false);
        _root.position = center;

        float halfL = fieldLength * 0.5f;
        float halfW = fieldWidth * 0.5f;

        // Atrás dos gols (fileiras ao longo de X, recuando em Z).
        if (behindPlayerGoal)
            BuildSide(center, axisAlongX: true, sign: -1f, edge: halfL, lineHalfLength: halfW);
        if (behindEnemyGoal)
            BuildSide(center, axisAlongX: true, sign: +1f, edge: halfL, lineHalfLength: halfW);

        // Laterais (fileiras ao longo de Z, recuando em X).
        if (leftSide)
            BuildSide(center, axisAlongX: false, sign: -1f, edge: halfW, lineHalfLength: halfL);
        if (rightSide)
            BuildSide(center, axisAlongX: false, sign: +1f, edge: halfW, lineHalfLength: halfL);

        RegisterInManager();
        Debug.Log($"[CrowdSpawner] {_spawned.Count} torcedores criados.");
    }

    /// <summary>
    /// Constrói um lado da arquibancada.
    /// </summary>
    /// <param name="axisAlongX">Fileira corre ao longo de X (atrás do gol) ou Z (lateral).</param>
    /// <param name="sign">Sentido do lado (-1 ou +1).</param>
    /// <param name="edge">Meia-dimensão do campo no eixo de recuo (atrás do gol: halfL; lateral: halfW).</param>
    /// <param name="lineHalfLength">Meio-comprimento da fileira.</param>
    private void BuildSide(Vector3 center, bool axisAlongX, float sign, float edge, float lineHalfLength)
    {
        int perRow = Mathf.Max(1, Mathf.RoundToInt((lineHalfLength * 2f) / seatSpacing));

        for (int r = 0; r < rows; r++)
        {
            float recede = edge + standOffset + r * rowDepth;
            float y = baseHeight + r * rowRise;

            for (int s = 0; s < perRow; s++)
            {
                float along = -lineHalfLength + s * seatSpacing + (r % 2) * (seatSpacing * 0.5f);
                if (along > lineHalfLength) continue;

                Vector3 pos = center;
                pos.y = center.y + y;

                if (axisAlongX)
                {
                    pos.x = center.x + along;
                    pos.z = center.z + sign * recede;
                }
                else
                {
                    pos.z = center.z + along;
                    pos.x = center.x + sign * recede;
                }

                CreateMember(pos, center);
            }
        }
    }

    private void CreateMember(Vector3 pos, Vector3 lookAtCenter)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Torcedor";
        quad.transform.SetParent(_root, true);
        quad.transform.position = pos;

        // Encara o centro do campo (na horizontal).
        Vector3 dir = lookAtCenter - pos; dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            quad.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);

        float jitter = 1f + Random.Range(-sizeJitter, sizeJitter);
        quad.transform.localScale = new Vector3(memberSize.x * jitter, memberSize.y * jitter, 1f);

        // Sem colisor (perf).
        var col = quad.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        var rend = quad.GetComponent<MeshRenderer>();
        rend.sharedMaterial = idleMaterial;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        var member = quad.AddComponent<CrowdMember>();
        member.mode = CrowdMember.Mode.MaterialSwap;
        member.targetRenderer = rend;
        member.idleMaterial = idleMaterial;
        member.cheerMaterial = cheerMaterial;

        _spawned.Add(member);
    }

    private void RegisterInManager()
    {
        if (crowdManager == null)
#if UNITY_2023_1_OR_NEWER
            crowdManager = Object.FindFirstObjectByType<CrowdManager>();
#else
            crowdManager = Object.FindObjectOfType<CrowdManager>();
#endif
        if (crowdManager == null) return;

        if (crowdManager.members == null) crowdManager.members = new List<CrowdMember>();
        crowdManager.members.AddRange(_spawned);
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        _spawned.Clear();
        if (_root != null)
        {
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
            _root = null;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 c = fieldCenter != null ? fieldCenter.position : transform.position;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(c, new Vector3(fieldWidth + standOffset * 2f, 0.2f, fieldLength + standOffset * 2f));
    }
#endif
}
