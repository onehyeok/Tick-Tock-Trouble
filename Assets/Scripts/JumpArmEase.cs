using UnityEngine;

public class JumpArmEase
{
    readonly Transform[] bones;
    readonly Quaternion[] idle;
    readonly float[] weight;
    bool ready;

    public JumpArmEase(Animator animator)
    {
        bones = new[]
        {
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder),
            animator.GetBoneTransform(HumanBodyBones.RightShoulder),
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
            animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
            animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
        };
        idle = new Quaternion[bones.Length];
        weight = new[] { 0.72f, 0.72f, 0.82f, 0.82f, 0.6f, 0.6f };
    }

    public void Cache()
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null)
                idle[i] = bones[i].localRotation;
        }

        ready = true;
    }

    public bool Apply(Animator animator)
    {
        float time;
        if (!InJump(animator, out time))
            return false;

        if (!ready)
            return true;

        float amount = Mathf.InverseLerp(0.1f, 0.35f, time);
        if (amount <= 0.001f)
            return true;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null)
                continue;

            bones[i].localRotation = Quaternion.Slerp(
                bones[i].localRotation,
                idle[i],
                amount * weight[i]);
        }

        return true;
    }

    static bool InJump(Animator animator, out float time)
    {
        time = 0f;
        int layer = animator.GetLayerIndex("Jump Layer");
        if (layer < 0)
            layer = 0;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
        if (animator.IsInTransition(layer))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(layer);
            if (IsJump(next))
                state = next;
            else if (!IsJump(state))
                return false;
        }
        else if (!IsJump(state))
        {
            return false;
        }

        time = state.normalizedTime;
        return true;
    }

    static bool IsJump(AnimatorStateInfo state)
    {
        return state.IsName("HumanM@Jump01") || state.IsName("Jump");
    }
}
