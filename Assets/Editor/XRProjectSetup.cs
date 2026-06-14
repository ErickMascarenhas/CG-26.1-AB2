#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

// As APIs do OpenXR só existem se o pacote "OpenXR Plugin" estiver instalado.
// Protegemos esse trecho com um define para o script compilar mesmo antes da instalação.
#if USE_OPENXR
using UnityEngine.XR.OpenXR;
#endif

/// <summary>
/// Editor script: configura o projeto Unity para Goalkeeper VR (Quest 3S standalone).
/// Acesse via: Tools → Goalkeeper VR → Setup XR Project
///
/// IMPORTANTE: este arquivo DEVE ficar dentro de uma pasta chamada "Editor"
/// (ex.: Assets/Editor/). Caso contrário, o Unity tenta compilá-lo no build
/// do jogo e as APIs de UnityEditor não existem em runtime.
///
/// Sobre o OpenXR: o trecho que configura render mode/depth fica atrás do
/// define USE_OPENXR. Depois de instalar o pacote "OpenXR Plugin", adicione
/// USE_OPENXR em Project Settings → Player → Scripting Define Symbols (Android)
/// para habilitá-lo. A ativação de Meta Quest + Hand Tracking é feita manualmente
/// na UI (instruções no diálogo final) — é o caminho mais confiável entre versões.
/// </summary>
public static class XRProjectSetup
{
    private const string MenuPath = "Tools/Goalkeeper VR/Setup XR Project";

    [MenuItem(MenuPath)]
    public static void Run()
    {
        Debug.Log("[XRProjectSetup] Iniciando configuração do projeto...");

        SetBuildTarget();
        ConfigurePlayerSettings();
        ConfigureQualitySettings();
        ConfigureXRPluginManagement();
        ConfigureOpenXRFeatures();

        AssetDatabase.SaveAssets();
        Debug.Log("[XRProjectSetup] ✅ Configuração concluída! Verifique o console para avisos.");
        EditorUtility.DisplayDialog(
            "Goalkeeper VR — Setup Concluído",
            "Projeto configurado para Quest 3S.\n\n" +
            "Passos manuais restantes:\n" +
            "1. Confirme os pacotes instalados (Package Manager):\n" +
            "   • XR Plugin Management\n" +
            "   • OpenXR Plugin\n" +
            "   • XR Interaction Toolkit\n" +
            "   • XR Hands\n" +
            "   • AI Navigation\n\n" +
            "2. Project Settings → XR Plug-in Management → Android → marque OpenXR.\n" +
            "3. Em OpenXR (Android), adicione os feature groups:\n" +
            "   • Meta Quest Support\n" +
            "   • Hand Tracking Subsystem\n" +
            "4. (Opcional) Adicione 'USE_OPENXR' em Player → Scripting Define\n" +
            "   Symbols (Android) para ativar a config de render mode por código.\n" +
            "5. Substitua a Main Camera por XR Origin (VR) e adicione os\n" +
            "   XRHandSkeletonDriver (mãos E/D).",
            "OK");
    }

    // -------------------------------------------------------------------------
    // BUILD TARGET
    // -------------------------------------------------------------------------
    private static void SetBuildTarget()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[XRProjectSetup] Trocando Build Target para Android...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
        }
        else
        {
            Debug.Log("[XRProjectSetup] Build Target já é Android. OK.");
        }
    }

    // -------------------------------------------------------------------------
    // PLAYER SETTINGS
    // -------------------------------------------------------------------------
    private static void ConfigurePlayerSettings()
    {
        PlayerSettings.SetApplicationIdentifier(
            NamedBuildTarget.Android, "com.GoalkeeperVR.Quest");
        PlayerSettings.companyName = "GoalkeeperVR";
        PlayerSettings.productName = "Goalkeeper VR";

        // API mínima: Android 10 (API 29) — requisito do Quest 3S
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // Orientação landscape (headset)
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

        // Graphics API: Vulkan apenas (mais eficiente no Quest 3S)
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

        PlayerSettings.MTRendering = true;
        PlayerSettings.SetManagedStrippingLevel(
            NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
        PlayerSettings.runInBackground = false;

        Debug.Log("[XRProjectSetup] Player Settings configurados.");
    }

    // -------------------------------------------------------------------------
    // QUALITY SETTINGS
    // -------------------------------------------------------------------------
    private static void ConfigureQualitySettings()
    {
        // Quest 3S roda a 72 Hz; o OpenXR gerencia o frame rate real.
        QualitySettings.vSyncCount = 0;          // OpenXR gerencia o swap chain
        QualitySettings.antiAliasing = 2;        // MSAA 2x (sweet spot p/ Quest)
        QualitySettings.shadows = ShadowQuality.HardOnly;
        QualitySettings.shadowDistance = 15f;    // cobre a área da baliza
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.lodBias = 1.5f;

        Debug.Log("[XRProjectSetup] Quality Settings configurados.");
    }

    // -------------------------------------------------------------------------
    // XR PLUGIN MANAGEMENT
    // -------------------------------------------------------------------------
    private static void ConfigureXRPluginManagement()
    {
        var generalSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
            BuildTargetGroup.Android);

        if (generalSettings == null)
        {
            Debug.LogWarning(
                "[XRProjectSetup] XRGeneralSettings não encontrado para Android. " +
                "Instale 'XR Plugin Management' via Package Manager e rode de novo.");
            return;
        }

        generalSettings.InitManagerOnStart = true;

        var managerSettings = generalSettings.Manager;
        if (managerSettings == null)
        {
            Debug.LogWarning("[XRProjectSetup] XRManagerSettings nulo. Verifique a instalação do pacote.");
            return;
        }

        // Ativa OpenXR como loader (string evita dependência direta do tipo)
        bool ok = XRPackageMetadataStore.AssignLoader(
            managerSettings,
            "UnityEngine.XR.OpenXR.OpenXRLoader",
            BuildTargetGroup.Android);

        if (ok)
            Debug.Log("[XRProjectSetup] XR Plugin Management: OpenXR ativado para Android.");
        else
            Debug.LogWarning("[XRProjectSetup] Não foi possível atribuir o OpenXRLoader. " +
                             "Instale o pacote 'OpenXR Plugin' e rode de novo.");
    }

    // -------------------------------------------------------------------------
    // OPENXR FEATURES (só compila com USE_OPENXR definido)
    // -------------------------------------------------------------------------
    private static void ConfigureOpenXRFeatures()
    {
#if USE_OPENXR
        var openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (openXRSettings == null)
        {
            Debug.LogWarning("[XRProjectSetup] OpenXRSettings não encontrado.");
            return;
        }

        openXRSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        openXRSettings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;

        Debug.Log("[XRProjectSetup] OpenXR: Single Pass Instanced + Depth16 configurados.");
#else
        Debug.Log("[XRProjectSetup] OpenXR config por código pulada (USE_OPENXR não definido). " +
                  "Configure render mode = Single Pass Instanced manualmente em " +
                  "Project Settings → XR Plug-in Management → OpenXR (Android).");
#endif
        Debug.Log("[XRProjectSetup] ⚠ Ative manualmente 'Meta Quest Support' e " +
                  "'Hand Tracking Subsystem' nos feature groups do OpenXR (Android).");
    }

    // -------------------------------------------------------------------------
    // VALIDATE
    // -------------------------------------------------------------------------
    [MenuItem("Tools/Goalkeeper VR/Validate Setup")]
    public static void ValidateSetup()
    {
        var issues = new List<string>();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            issues.Add("• Build Target não é Android");

        if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel29)
            issues.Add("• Min SDK < 29 (Quest 3S requer 29+)");

        var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        bool hasVulkan = false;
        foreach (var api in apis)
            if (api == GraphicsDeviceType.Vulkan) { hasVulkan = true; break; }
        if (!hasVulkan)
            issues.Add("• Vulkan não está na lista de Graphics APIs");

        if (QualitySettings.vSyncCount != 0)
            issues.Add("• VSync está ligado (deve ser 0 para XR)");

        EditorUtility.DisplayDialog(
            "Validate Setup",
            issues.Count == 0 ? "✅ Tudo OK!" :
                string.Join("\n", issues) + "\n\nRode 'Setup XR Project' para corrigir.",
            "OK");
    }
}
#endif
