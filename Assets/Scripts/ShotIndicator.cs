using UnityEngine;

/// <summary>
/// Indicador de mira (Goalkeeper VR).
///
/// Um CÍRCULO semi-transparente que aparece no ponto EXATO onde a bola vai chegar,
/// um pouco ANTES do chute (durante o windup), e permanece visível até a jogada ser
/// resolvida (defesa/gol) — ou, como segurança, até o tempo estimado de voo da bola.
///
/// Billboard: o círculo sempre encara o goleiro (câmera) para boa leitura.
/// Se nenhum prefab for atribuído, o visual é criado em runtime.
/// </summary>
public class ShotIndicator : MonoBehaviour
{
    [Header("Visual (opcional)")]
    [Tooltip("Prefab do marcador (deve já encarar +Z). Se vazio, cria um círculo em runtime.")]
    public GameObject markerPrefab;

    [Header("Aparência")]
    [Tooltip("Cor do círculo (o alpha define a transparência).")]
    public Color color = new Color(1f, 0.85f, 0.1f, 0.45f);
    [Tooltip("Diâmetro do círculo em metros.")]
    public float size = 0.7f;
    [Tooltip("Pulsação sutil para chamar atenção (0 = parado).")]
    public float pulseAmount = 0.12f;
    public float pulseSpeed = 5f;

    [Header("Comportamento")]
    [Tooltip("Encarar a câmera (billboard).")]
    public bool faceCamera = true;

    private GameObject _root;
    private Transform _marker;
    private Transform _camera;
    private bool _visible;
    private float _autoHideAt = -1f;   // tempo (Time.time) para sumir sozinho; -1 = nunca

    private void Awake()
    {
        BuildVisualsIfNeeded();
        SetVisible(false);
        if (Camera.main != null) _camera = Camera.main.transform;
    }

    private void BuildVisualsIfNeeded()
    {
        if (_root != null) return;

        if (markerPrefab != null)
        {
            _root = Instantiate(markerPrefab, transform);
            _marker = _root.transform;
            return;
        }

        _root = new GameObject("ShotIndicatorVisual");
        _root.transform.SetParent(transform, false);

        // Material transparente unlit
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        var mat = new Material(shader);
        SetupTransparent(mat, color);

        // Círculo: um Quad com a malha padrão + textura de disco gerada
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(_root.transform, false);
        quad.transform.localScale = Vector3.one * size;
        var rend = quad.GetComponent<Renderer>();
        mat.mainTexture = CreateCircleTexture();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        _marker = quad.transform;
    }

    private static void SetupTransparent(Material mat, Color c)
    {
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        // Configura blending alpha no URP/Unlit
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);     // Alpha
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    /// <summary>Gera uma textura de anel/círculo (branco com borda) com canal alpha.</summary>
    private static Texture2D CreateCircleTexture(int res = 128)
    {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(res * 0.5f, res * 0.5f);
        float outer = res * 0.48f;
        float inner = res * 0.34f; // anel; aumente 'inner' para disco mais fino
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a;
                if (d > outer) a = 0f;
                else if (d < inner) a = 0.25f;        // miolo levemente preenchido
                else a = 1f;                          // anel sólido
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>Mostra o indicador no ponto-alvo. autoHideAfter &gt; 0 some sozinho após X s.</summary>
    public void Show(Vector3 worldTarget, float autoHideAfter = -1f)
    {
        BuildVisualsIfNeeded();
        _root.transform.position = worldTarget;
        _autoHideAt = autoHideAfter > 0f ? Time.time + autoHideAfter : -1f;
        SetVisible(true);
    }

    public void Hide()
    {
        _autoHideAt = -1f;
        SetVisible(false);
    }

    private void SetVisible(bool v)
    {
        _visible = v;
        if (_root != null) _root.SetActive(v);
    }

    private void Update()
    {
        if (!_visible) return;

        // Auto-hide pelo tempo de voo (segurança, caso a jogada não resolva).
        if (_autoHideAt > 0f && Time.time >= _autoHideAt)
        {
            Hide();
            return;
        }

        if (_marker == null) return;

        // Billboard: encara a câmera.
        if (faceCamera)
        {
            if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
            if (_camera != null)
                _root.transform.rotation = Quaternion.LookRotation(
                    _root.transform.position - _camera.position, Vector3.up);
        }

        // Pulsação.
        float s = size * (1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount);
        _marker.localScale = Vector3.one * s;
    }
}
