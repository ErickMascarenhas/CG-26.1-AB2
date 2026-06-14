using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Console de debug DENTRO do mundo VR (Goalkeeper VR).
///
/// Captura TODOS os Debug.Log / LogWarning / LogError de qualquer script e os
/// mostra num painel TextMeshPro em World-Space, com FPS no topo. Permite
/// diagnosticar tudo direto no headset, sem acesso ao console do Unity.
///
/// Como usar:
///   1. Crie um Canvas World-Space grande, posicionado num canto visível
///      (ex.: à sua direita/esquerda, fora da área do gol).
///   2. Dentro dele, um Text - TextMeshPro alto e estreito. Fonte pequena,
///      alinhamento Top-Left, Overflow = Truncate (ou Masking).
///   3. Crie um GameObject "VR Debug Console", adicione este script, e arraste
///      o TextMeshPro em "Log Text".
///   4. Pronto: qualquer Debug.Log aparece no painel automaticamente.
///
/// Dica: para valores CONTÍNUOS (ex.: fechamento da mão a cada frame), use os
/// campos "Debug Text" dos componentes (GoalCalibrator/HandTrackingCatch/EnemyShooter),
/// que sobrescrevem o texto a cada frame. Este console é para EVENTOS e ERROS.
/// </summary>
public class VRDebugConsole : MonoBehaviour
{
    public static VRDebugConsole Instance { get; private set; }

    [Header("Referências")]
    [Tooltip("TextMeshPro World-Space onde os logs aparecem.")]
    public TMP_Text logText;
    [Tooltip("(Opcional) TextMeshPro separado só para o FPS. Se vazio, o FPS vai no topo do log.")]
    public TMP_Text fpsText;

    [Header("Configuração")]
    [Tooltip("Quantas linhas de log manter visíveis.")]
    public int maxLines = 18;
    [Tooltip("Mostrar stack trace nos erros (verboso).")]
    public bool showStackTraceOnError = true;
    [Tooltip("Atualização do FPS em segundos.")]
    public float fpsRefreshInterval = 0.5f;

    [Header("Cores")]
    public Color colorLog = new Color(0.85f, 0.85f, 0.85f);
    public Color colorWarning = new Color(1f, 0.85f, 0.2f);
    public Color colorError = new Color(1f, 0.35f, 0.35f);

    private readonly Queue<string> _lines = new Queue<string>();
    private readonly StringBuilder _sb = new StringBuilder();

    // FPS
    private float _fpsAccum = 0f;
    private int _fpsFrames = 0;
    private float _fpsTimer = 0f;
    private float _currentFps = 0f;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()  => Application.logMessageReceived += HandleLog;
    private void OnDisable() => Application.logMessageReceived -= HandleLog;

    private void Start()
    {
        AddLine("<b>== VR Debug Console iniciado ==</b>", colorLog);
        AddLine($"Unity {Application.unityVersion} · target {Application.targetFrameRate} fps", colorLog);
    }

    private void Update()
    {
        // FPS
        _fpsAccum += Time.unscaledDeltaTime;
        _fpsFrames++;
        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= fpsRefreshInterval)
        {
            _currentFps = _fpsFrames / _fpsAccum;
            _fpsAccum = 0f;
            _fpsFrames = 0;
            _fpsTimer = 0f;
            RefreshFpsDisplay();
        }
    }

    // -------------------------------------------------------------------------
    // Captura de logs
    // -------------------------------------------------------------------------
    private void HandleLog(string message, string stackTrace, LogType type)
    {
        Color c;
        string prefix;
        switch (type)
        {
            case LogType.Warning: c = colorWarning; prefix = "[!] "; break;
            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert:  c = colorError;   prefix = "[X] "; break;
            default:              c = colorLog;      prefix = "- "; break;
        }

        AddLine(prefix + message, c);

        if (showStackTraceOnError &&
            (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
            !string.IsNullOrEmpty(stackTrace))
        {
            // só a primeira linha do stack para não poluir
            string firstLine = stackTrace.Split('\n')[0];
            AddLine("   " + firstLine, c);
        }

        RefreshLogDisplay();
    }

    private void AddLine(string text, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGB(color);
        _lines.Enqueue($"<color=#{hex}>{Sanitize(text)}</color>");
        while (_lines.Count > maxLines)
            _lines.Dequeue();
    }

    /// <summary>
    /// Troca símbolos que a fonte padrão (LiberationSans) não tem por equivalentes ASCII,
    /// evitando o spam de "character not found". Cobre os símbolos usados nos logs.
    /// </summary>
    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s
            .Replace("→", "->").Replace("←", "<-")
            .Replace("—", "-").Replace("–", "-")
            .Replace("×", "x").Replace("·", "-")
            .Replace("≥", ">=").Replace("≤", "<=")
            .Replace("⚠", "[!]").Replace("✖", "[X]")
            .Replace("✅", "[OK]").Replace("•", "-");
    }

    private void RefreshLogDisplay()
    {
        if (logText == null) return;

        _sb.Clear();
        if (fpsText == null)
            _sb.AppendLine(FpsLine());
        foreach (var line in _lines)
            _sb.AppendLine(line);
        logText.text = _sb.ToString();
    }

    private void RefreshFpsDisplay()
    {
        if (fpsText != null)
            fpsText.text = FpsLine();
        else
            RefreshLogDisplay(); // FPS embutido no topo do log
    }

    private string FpsLine()
    {
        string hex = _currentFps >= 70f ? "55FF55" : _currentFps >= 60f ? "FFD23A" : "FF5959";
        return $"<b>FPS: <color=#{hex}>{_currentFps:F0}</color></b>";
    }

    // -------------------------------------------------------------------------
    // API pública (opcional) — para logar diretamente sem passar por Debug.Log
    // -------------------------------------------------------------------------
    public static void Print(string message)
    {
        if (Instance != null) Instance.AddLine("- " + message, Instance.colorLog);
        if (Instance != null) Instance.RefreshLogDisplay();
    }

    /// <summary>Limpa o console.</summary>
    public void Clear()
    {
        _lines.Clear();
        RefreshLogDisplay();
    }
}
