using UnityEngine;

public class IdleFootClose
{
    const float Degrees = 8f;

    readonly Animator animator;
    readonly Transform leftUpper;
    readonly Transform leftLower;
    readonly Transform rightUpper;
    readonly Transform rightLower;

    public IdleFootClose(Animator animator)
    {
        this.animator = animator;
        leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
    }

    public void Apply(TickToyMove move, Transform body)
    {
        if (animator == null || move == null || body == null)
            return;
        if (move.IsWinding || move.IsDead || move.IsFainted)
            return;
        if (animator.GetBool("IsProne") || JumpArmEase.IsJumping(animator))
            return;
        if (animator.IsInTransition(0) || !animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
            return;

        Close(leftUpper, leftLower, body.right);
        Close(rightUpper, rightLower, -body.right);
    }

    static void Close(Transform upper, Transform lower, Vector3 inward)
    {
        if (upper == null || lower == null)
            return;

        Vector3 leg = lower.position - upper.position;
        Vector3 axis = Vector3.Cross(leg, inward);
        if (axis.sqrMagnitude < 0.0001f)
            return;

        upper.rotation = Quaternion.AngleAxis(Degrees, axis.normalized) * upper.rotation;
    }
}
