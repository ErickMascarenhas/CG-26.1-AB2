#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor tool: monta automaticamente o Animator dos jogadores (Goalkeeper VR).
/// Acesse via: Tools → Goalkeeper VR → Build Player Animators
///
/// O que faz, em um clique:
///   1. Garante que as clips de IDLE e CORRIDA sejam LOOP (loopTime = true).
///   2. Cria um AnimatorController em Assets/Animations/PlayerController.controller com:
///        - Parâmetros: bool 'IsRunning', trigger 'Shoot', trigger 'Pass'.
///        - Estados: Idle (default) ⇄ Run (por IsRunning); Shoot e Pass (via AnyState,
///          voltam para Idle ao terminar).
///   3. Atribui o controller + Avatar a um Animator no ROOT de cada prefab
///      (Attacker e Attacker Enemy) e liga o campo SoccerPlayer.animator.
///
/// Como as clips Mixamo são Humanoid, um único controller retarget-a para os dois
/// modelos. Se quiser clips diferentes por time, duplique o controller e ajuste.
///
/// IMPORTANTE: precisa ficar dentro de uma pasta "Editor".
/// </summary>
public static class PlayerAnimatorBuilder
{
    private const string MenuPath = "Tools/Goalkeeper VR/Build Player Animators";

    // Pasta de origem das clips (modelo aliado; Humanoid retarget para ambos).
    private const string ClipFolder = "Assets/Models/Attacker";

    // Avatares por time (cada prefab usa o avatar do seu próprio FBX).
    private const string AllyAvatarFbx  = "Assets/Models/Attacker/Attacker.fbx";
    private const string EnemyAvatarFbx = "Assets/Models/Attacker Enemy/Attacker Enemy.fbx";

    private const string OutputFolder     = "Assets/Animations";
    private const string ControllerPath   = "Assets/Animations/PlayerController.controller";

    private const string AllyPrefab  = "Assets/Prefabs/Attacker.prefab";
    private const string EnemyPrefab = "Assets/Prefabs/Attacker Enemy.prefab";

    // Nome do clip (sem extensão) preferido para cada estado, com fallbacks.
    private static readonly string[] IdleCandidates  = { "offensive idle", "goalkeeper idle", "idle", "goalkeeper idle (2)" };
    private static readonly string[] RunCandidates   = { "jog forward", "strike foward jog", "jog forward diagonal" };
    private static readonly string[] ShootCandidates = { "kick soccerball", "soccer penalty kick", "kick up soccerball" };
    private static readonly string[] PassCandidates  = { "kick soccerball (2)", "receive soccerball", "kick soccerball" };

    [MenuItem(MenuPath)]
    public static void Build()
    {
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);
        AssetDatabase.Refresh();

        // 1) Clips
        AnimationClip idle  = FindClip(IdleCandidates,  loop: true);
        AnimationClip run   = FindClip(RunCandidates,   loop: true);
        AnimationClip shoot = FindClip(ShootCandidates, loop: false);
        AnimationClip pass  = FindClip(PassCandidates,  loop: false);

        if (idle == null || run == null)
        {
            EditorUtility.DisplayDialog("Goalkeeper VR — Animator",
                "Não encontrei as clips essenciais (idle / jog forward) em " + ClipFolder +
                ".\nConfira se os FBX do Mixamo estão importados e são Humanoid.", "OK");
            return;
        }

        // 2) Controller
        var controller = BuildController(idle, run, shoot, pass);

        // 3) Atribui aos prefabs
        int ok = 0;
        ok += AssignToPrefab(AllyPrefab,  controller, AllyAvatarFbx)  ? 1 : 0;
        ok += AssignToPrefab(EnemyPrefab, controller, EnemyAvatarFbx) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PlayerAnimatorBuilder] Controller: {ControllerPath} | idle={idle?.name} run={run?.name} " +
                  $"shoot={shoot?.name} pass={pass?.name} | prefabs ok={ok}/2");
        EditorUtility.DisplayDialog("Goalkeeper VR — Animator",
            "Animator montado!\n\n" +
            $"• Idle: {idle?.name}\n• Run: {run?.name}\n• Shoot: {shoot?.name}\n• Pass: {pass?.name}\n\n" +
            $"Atribuído a {ok}/2 prefabs (Attacker / Attacker Enemy).", "OK");
    }

    // -------------------------------------------------------------------------
    // Construção do AnimatorController
    // -------------------------------------------------------------------------
    private static AnimatorController BuildController(
        AnimationClip idle, AnimationClip run, AnimationClip shoot, AnimationClip pass)
    {
        // (Re)cria do zero para ficar idempotente.
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Pass",  AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        var idleState = sm.AddState("Idle");
        idleState.motion = idle;
        sm.defaultState = idleState;

        var runState = sm.AddState("Run");
        runState.motion = run;

        // Idle ⇄ Run por IsRunning (sem exit time, transições rápidas)
        var toRun = idleState.AddTransition(runState);
        toRun.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
        toRun.hasExitTime = false;
        toRun.duration = 0.1f;

        var toIdle = runState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");
        toIdle.hasExitTime = false;
        toIdle.duration = 0.1f;

        // Shoot / Pass via AnyState (interrompem o que estiver tocando).
        if (shoot != null)
            AddActionState(sm, idleState, shoot, "Shoot", "Shoot");
        if (pass != null)
            AddActionState(sm, idleState, pass, "Pass", "Pass");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>Estado de ação (chute/passe) disparado por trigger via AnyState; volta a Idle.</summary>
    private static void AddActionState(
        AnimatorStateMachine sm, AnimatorState backTo, AnimationClip clip, string stateName, string trigger)
    {
        var state = sm.AddState(stateName);
        state.motion = clip;

        var fromAny = sm.AddAnyStateTransition(state);
        fromAny.AddCondition(AnimatorConditionMode.If, 0, trigger);
        fromAny.hasExitTime = false;
        fromAny.duration = 0.05f;
        fromAny.canTransitionToSelf = false;

        // Ao terminar a animação, volta para Idle.
        var back = state.AddTransition(backTo);
        back.hasExitTime = true;
        back.exitTime = 0.9f;
        back.duration = 0.1f;
    }

    // -------------------------------------------------------------------------
    // Clips
    // -------------------------------------------------------------------------
    private static AnimationClip FindClip(string[] candidates, bool loop)
    {
        foreach (var name in candidates)
        {
            string path = $"{ClipFolder}/{name}.fbx";
            if (!File.Exists(path)) continue;

            if (loop) SetClipLoop(path, true);

            var clip = LoadClipFromFbx(path);
            if (clip != null) return clip;
        }
        return null;
    }

    private static AnimationClip LoadClipFromFbx(string fbxPath)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        return assets
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c != null && !c.name.StartsWith("__preview"));
    }

    private static void SetClipLoop(string fbxPath, bool loop)
    {
        var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (imp == null) return;

        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].loopTime != loop) { clips[i].loopTime = loop; changed = true; }
        }
        if (changed)
        {
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
    }

    // -------------------------------------------------------------------------
    // Prefabs
    // -------------------------------------------------------------------------
    private static bool AssignToPrefab(string prefabPath, AnimatorController controller, string avatarFbx)
    {
        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[PlayerAnimatorBuilder] Prefab não encontrado: {prefabPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            // Animator no root (os bones mixamorig são descendentes → o Avatar resolve).
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // o NavMeshAgent move; a anim só "veste".

            var avatar = LoadAvatar(avatarFbx);
            if (avatar != null) animator.avatar = avatar;

            // Liga o Animator no SoccerPlayer.
            var sp = root.GetComponent<SoccerPlayer>();
            if (sp != null) sp.animator = animator;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Avatar LoadAvatar(string fbxPath)
    {
        if (!File.Exists(fbxPath)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
    }
}
#endif
