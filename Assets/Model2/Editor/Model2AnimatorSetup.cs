using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// TickToyAnimator의 "Run 0", "Prone" 상태를 MoveX/MoveY 2D 블렌드 트리로 교체 (한 번만 자동 실행, 메뉴로 재실행 가능)
public static class Model2AnimatorSetup
{
    const string ControllerPath = "Assets/Animations/Animator/TickToyAnimator.controller";
    const string BackupPath = "Assets/Animations/Animator/TickToyAnimator_Backup.controller";
    const string Dir = "Assets/Model2/Animations/";
    static string Key => "Model2AnimatorSetup_v1_" + Application.dataPath;
    static double startTime;

    // 자동 실행 비활성화 (이미 적용됨). 다시 적용하려면 메뉴 Tools > Model2 사용
    static void Init()
    {
        if (EditorPrefs.GetBool(Key, false)) return;
        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += TryRun;
    }

    static void TryRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Setup())
        {
            EditorPrefs.SetBool(Key, true);
            EditorApplication.update -= TryRun;
        }
        else if (EditorApplication.timeSinceStartup - startTime > 300)
            EditorApplication.update -= TryRun;
    }

    [MenuItem("Tools/Model2/Setup Run & Prone Blend Trees")]
    static void Menu() => Setup();

    static AnimationClip Clip(string n) =>
        AssetDatabase.LoadAllAssetsAtPath(Dir + n + ".fbx").OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    static bool Setup()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) return false;
        string[] names = { "Run", "RunLeft", "RunRight", "RunBack", "ProneIdle", "ProneForward", "ProneBack", "ProneLeft", "ProneRight" };
        var clips = names.ToDictionary(n => n, Clip);
        if (clips.Values.Any(c => c == null)) return false;   // 아직 임포트 전이면 다음 프레임에 재시도

        if (AssetDatabase.LoadAssetAtPath<Object>(BackupPath) == null)
            AssetDatabase.CopyAsset(ControllerPath, BackupPath);

        var run = Find(ctrl, "Run 0");
        var prone = Find(ctrl, "Prone");
        if (run == null || prone == null)
        {
            Debug.LogError("[Model2] 'Run 0' 또는 'Prone' 상태를 찾지 못했습니다.");
            return true;
        }

        run.motion = MakeTree(ctrl, "RunBlend",
            (clips["Run"], 0f, 1f), (clips["RunBack"], 0f, -1f),
            (clips["RunLeft"], -1f, 0f), (clips["RunRight"], 1f, 0f));

        prone.motion = MakeTree(ctrl, "ProneBlend",
            (clips["ProneIdle"], 0f, 0f), (clips["ProneForward"], 0f, 1f), (clips["ProneBack"], 0f, -1f),
            (clips["ProneLeft"], -1f, 0f), (clips["ProneRight"], 1f, 0f));

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[Model2] TickToyAnimator: 'Run 0' → RunBlend, 'Prone' → ProneBlend 적용 완료 (백업: " + BackupPath + ")");
        return true;
    }

    static BlendTree MakeTree(AnimatorController ctrl, string name, params (Motion m, float x, float y)[] kids)
    {
        var bt = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveY",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(bt, ctrl);
        foreach (var k in kids) bt.AddChild(k.m, new Vector2(k.x, k.y));
        return bt;
    }

    static AnimatorState Find(AnimatorController c, string n)
    {
        foreach (var l in c.layers)
        {
            var s = Find(l.stateMachine, n);
            if (s != null) return s;
        }
        return null;
    }

    static AnimatorState Find(AnimatorStateMachine sm, string n)
    {
        foreach (var cs in sm.states) if (cs.state.name == n) return cs.state;
        foreach (var c in sm.stateMachines)
        {
            var s = Find(c.stateMachine, n);
            if (s != null) return s;
        }
        return null;
    }
}
