using UnityEngine;

/// <summary>
/// Fase 7 — Indicador de mira no céu (Goalkeeper VR).
///
/// Mostra ONDE a bola vai antes do chute, dando tempo de reação ao goleiro.
/// Desenha um marcador no ponto-alvo + um feixe vertical do céu até o alvo.
///
/// Se nenhum prefab for atribuído, cria os visuais em runtime (esfera + cilindro).
/// O MatchManager chama Show(alvo) no início do windup e Hide() após o chute.
/// </summary>
public class ShotIndicator : MonoBehaviour
{
    [Header("Visual (opcional)")]
    [Tooltip("Prefab do marcador. Se vazio, é criado em runtime (esfera + feixe).")]
    public GameObject markerPrefab;

    [Header("Aparência")]
    public Color color = new Color(1f, 0.85f, 0.1f, 1f);
    public float markerSize = 0.6f;
    [Tooltip("Altura do feixe vertical acima do alvo.")]
    public float beamHeight = 6f;
    public float beamWidth = 0.12f;
    [Tooltip("Velocidade da pulsação para chamar atenção.")]
    public float pulseSpeed = 6f;
    public float pulseAmount = 0.25f;

    private GameObject _root;
    private Transform _marker;
    private Transform _beam;
    private bool _visible;
    private float _baseScale;

    private void Awake()
    {
        BuildVisualsIfNeeded();
        SetVisible(false);
    }

    private void BuildVisualsIfNeeded()
    {
        if (markerPrefab != null)
        {
            _root = Instantiate(markerPrefab, transform);
            _marker = _root.transform;
            _baseScale = markerSize;
            return;
        }

        // Cria visuais simples em runtime
        _root = new GameObject("ShotIndicatorVisual");
        _root.transform.SetParent(transform, false);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat.shader == null) mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

        // Marcador (esfera)
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(_root.transform, false);
        sphere.transform.localScale = Vector3.one * markerSize;
        sphere.GetComponent<Renderer>().sharedMaterial = mat;
        _marker = sphere.transform;

        // Feixe vertical (cilindro)
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(beam.GetComponent<Collider>());
        beam.transform.SetParent(_root.transform, false);
        beam.transform.localScale = new Vector3(beamWidth, beamHeight * 0.5f, beamWidth);
        beam.transform.localPosition = Vector3.up * (beamHeight * 0.5f);
        beam.GetComponent<Renderer>().sharedMaterial = mat;
        _beam = beam.transform;

        _baseScale = markerSize;
    }

    public void Show(Vector3 worldTarget)
    {
        if (_root == null) BuildVisualsIfNeeded();
        _root.transform.position = worldTarget;
        SetVisible(true);
    }

    public void Hide() => SetVisible(false);

    private void SetVisible(bool v)
    {
        _visible = v;
        if (_root != null) _root.SetActive(v);
    }

    private void Update()
    {
        if (!_visible || _marker == null) return;
        // Pulsa o marcador para chamar atenção
        float s = _baseScale * (1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount);
        _marker.localScale = Vector3.one * s;
    }
}
