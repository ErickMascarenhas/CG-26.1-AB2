#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool: prepara a torcida (Goalkeeper VR).
/// Acesse via: Tools → Goalkeeper VR → Build Crowd
///
/// O que faz:
///   1. Garante que Crowd Idle.png / Crowd Cheer.png importem com transparência.
///   2. Cria materiais CrowdIdle.mat e CrowdCheer.mat (URP Unlit com alpha clip;
///      fallback para Unlit/Transparent fora do URP).
///   3. Se houver um CrowdSpawner na cena aberta, liga os materiais nele.
///
/// Depois, no CrowdSpawner: ajuste linhas/altura e dê Play (ou "Spawn Now").
///
/// IMPORTANTE: precisa ficar dentro de uma pasta "Editor".
/// </summary>
public static class CrowdBuilder
{
    private const string MenuPath = "Tools/Goalkeeper VR/Build Crowd";

    private const string IdlePng  = "Assets/Materials/Crowd/Crowd Idle.png";
    private const string CheerPng = "Assets/Materials/Crowd/Crowd Cheer.png";

    private const string IdleMat  = "Assets/Materials/Crowd/CrowdIdle.mat";
    private const string CheerMat = "Assets/Materials/Crowd/CrowdCheer.mat";

    [MenuItem(MenuPath)]
    public static void Build()
    {
        var idleTex  = PrepareTexture(IdlePng);
        var cheerTex = PrepareTexture(CheerPng);

        if (idleTex == null || cheerTex == null)
        {
            EditorUtility.DisplayDialog("Goalkeeper VR — Crowd",
                "Não encontrei as PNGs da torcida em Assets/Materials/Crowd/\n" +
                "(Crowd Idle.png e Crowd Cheer.png).", "OK");
            return;
        }

        var idleMat  = CreateUnlitMaterial(IdleMat,  idleTex);
        var cheerMat = CreateUnlitMaterial(CheerMat, cheerTex);

        // Liga no CrowdSpawner da cena, se existir.
        int wired = 0;
#if UNITY_2023_1_OR_NEWER
        var spawner = Object.FindFirstObjectByType<CrowdSpawner>();
#else
        var spawner = Object.FindObjectOfType<CrowdSpawner>();
#endif
        if (spawner != null)
        {
            spawner.idleMaterial = idleMat;
            spawner.cheerMaterial = cheerMat;
            EditorUtility.SetDirty(spawner);
            wired = 1;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CrowdBuilder] Materiais prontos: {IdleMat}, {CheerMat}. CrowdSpawner ligado: {wired}.");
        EditorUtility.DisplayDialog("Goalkeeper VR — Crowd",
            "Torcida preparada!\n\n" +
            "• Materiais: CrowdIdle.mat / CrowdCheer.mat\n" +
            (wired == 1
                ? "• CrowdSpawner da cena ligado.\nDê Play (ou use 'Spawn Now' no menu de contexto do CrowdSpawner)."
                : "• Nenhum CrowdSpawner na cena. Adicione um GameObject com CrowdSpawner e arraste os materiais."),
            "OK");
    }

    private static Texture2D PrepareTexture(string path)
    {
        if (!File.Exists(path)) return null;

        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool changed = false;
            if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; changed = true; }
            if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; changed = true; }
            if (imp.mipmapEnabled) { imp.mipmapEnabled = false; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material CreateUnlitMaterial(string matPath, Texture2D tex)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        bool urp = shader != null;
        if (!urp) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = shader;
        }

        if (urp)
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            // Alpha clipping (cutout) — bom para arquibancada e barato no Quest.
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else
        {
            mat.mainTexture = tex;
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
#endif
