#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Greeting = 상체 마스크 레이어 / Dance1·2 = 풀바디 레이어(마스크 없음).
/// </summary>
[InitializeOnLoad]
public static class TickToyEmoteLayerSetup
{
    const string ControllerPath = "Assets/Animations/Animator/TickToyAnimator.controller";
    const string MaskPath = "Assets/Animations/Mask/TickToyUpperMask.mask";
    const string GreetingPath = "Assets/Animations/Greeting.fbx";
    const string Dance1Path = "Assets/Animations/T_Dance_1/T_Dance_1/T_Dance_1.fbx";
    const string Dance2Path = "Assets/Animations/T_Dance_2/T_Dance_2/T_Dance_2.fbx";
    const string UpperLayerName = "Emote Upper Layer";
    const string FullLayerName = "Emote Full Layer";
    const string LegacyLayerName = "Emote Layer";

    static TickToyEmoteLayerSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    [MenuItem("TickTock/Setup TickToy Emote Layer")]
    public static void Ensure()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (controller == null)
            return;

        EnsureTrigger(controller, "Greeting");
        EnsureTrigger(controller, "Dance1");
        EnsureTrigger(controller, "Dance2");

        // 예전 통합 Emote Layer 제거
        RemoveLayerIfExists(controller, LegacyLayerName);

        int upperIndex = EnsureLayer(controller, UpperLayerName, mask, 1f);
        int fullIndex = EnsureLayer(controller, FullLayerName, null, 1f);

        AnimatorControllerLayer[] layers = controller.layers;
        SetupUpperLayer(layers[upperIndex].stateMachine, controller);
        SetupFullLayer(layers[fullIndex].stateMachine, controller);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void RemoveLayerIfExists(AnimatorController controller, string name)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name != name)
                continue;
            controller.RemoveLayer(i);
            return;
        }
    }

    static int EnsureLayer(AnimatorController controller, string name, AvatarMask mask, float weight)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name != name)
                continue;

            layers[i].avatarMask = mask;
            layers[i].defaultWeight = weight;
            layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            return i;
        }

        AnimatorStateMachine sm = new AnimatorStateMachine
        {
            name = name,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(sm, controller);

        AnimatorControllerLayer layer = new AnimatorControllerLayer
        {
            name = name,
            defaultWeight = weight,
            blendingMode = AnimatorLayerBlendingMode.Override,
            avatarMask = mask,
            stateMachine = sm
        };
        controller.AddLayer(layer);
        return controller.layers.Length - 1;
    }

    static void SetupUpperLayer(AnimatorStateMachine sm, AnimatorController controller)
    {
        if (sm == null)
            return;

        AnimatorState empty = FindOrCreateState(sm, "Empty", null);
        AnimatorState greeting = FindOrCreateState(sm, "Greeting", LoadClip(GreetingPath));
        sm.defaultState = empty;

        // 댄스 상태가 상체 레이어에 남아 있으면 제거
        RemoveStateIfExists(sm, "Dance1");
        RemoveStateIfExists(sm, "Dance2");

        EnsureAnyTransition(sm, greeting, "Greeting");
        EnsureExitToEmpty(greeting, empty);
    }

    static void SetupFullLayer(AnimatorStateMachine sm, AnimatorController controller)
    {
        if (sm == null)
            return;

        AnimatorState empty = FindOrCreateState(sm, "Empty", null);
        AnimatorState dance1 = FindOrCreateState(sm, "Dance1", LoadClip(Dance1Path));
        AnimatorState dance2 = FindOrCreateState(sm, "Dance2", LoadClip(Dance2Path));
        sm.defaultState = empty;

        RemoveStateIfExists(sm, "Greeting");

        EnsureAnyTransition(sm, dance1, "Dance1");
        EnsureAnyTransition(sm, dance2, "Dance2");
        EnsureExitToEmpty(dance1, empty);
        EnsureExitToEmpty(dance2, empty);
    }

    static void RemoveStateIfExists(AnimatorStateMachine sm, string name)
    {
        ChildAnimatorState[] children = sm.states;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].state == null || children[i].state.name != name)
                continue;
            sm.RemoveState(children[i].state);
            return;
        }
    }

    static void EnsureTrigger(AnimatorController controller, string name)
    {
        for (int i = 0; i < controller.parameters.Length; i++)
        {
            if (controller.parameters[i].name == name)
                return;
        }

        controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
    }

    static AnimatorState FindOrCreateState(AnimatorStateMachine sm, string name, Motion motion)
    {
        ChildAnimatorState[] children = sm.states;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].state != null && children[i].state.name == name)
            {
                if (motion != null && children[i].state.motion != motion)
                    children[i].state.motion = motion;
                return children[i].state;
            }
        }

        AnimatorState state = sm.AddState(name);
        state.motion = motion;
        return state;
    }

    static void EnsureAnyTransition(AnimatorStateMachine sm, AnimatorState dest, string trigger)
    {
        AnimatorStateTransition[] anys = sm.anyStateTransitions;
        for (int i = 0; i < anys.Length; i++)
        {
            if (anys[i] != null && anys[i].destinationState == dest)
                return;
        }

        AnimatorStateTransition t = sm.AddAnyStateTransition(dest);
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.canTransitionToSelf = true;
    }

    static void EnsureExitToEmpty(AnimatorState from, AnimatorState empty)
    {
        AnimatorStateTransition[] transitions = from.transitions;
        for (int i = 0; i < transitions.Length; i++)
        {
            if (transitions[i] != null && transitions[i].destinationState == empty)
                return;
        }

        AnimatorStateTransition t = from.AddTransition(empty);
        t.hasExitTime = true;
        t.exitTime = 0.95f;
        t.duration = 0.15f;
        t.hasFixedDuration = true;
    }

    static AnimationClip LoadClip(string assetPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__"))
                continue;
            return clip;
        }

        return null;
    }
}
#endif
