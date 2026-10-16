using UnityEngine;

public class JephyModelController : MonoBehaviour
{
    private TickToyMove move;
    private Animator animator;
    private Transform neck;
    private Transform head;
    private Vector3 neckPosition;
    private Quaternion neckRotation;
    private Vector3 neckScale;
    private Vector3 headPosition;
    private Quaternion headRotation;
    private Vector3 headScale;
    private bool hasRestPose;
    private readonly ArmRest[] arms = new ArmRest[2];
    private bool hasArmRest;

    void Awake()
    {
        move = GetComponent<TickToyMove>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
            return;

        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        BindArm(0, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        BindArm(1, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
    }

    void BindArm(int index, HumanBodyBones shoulder, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
    {
        arms[index].shoulder = animator.GetBoneTransform(shoulder);
        arms[index].upper = animator.GetBoneTransform(upper);
        arms[index].lower = animator.GetBoneTransform(lower);
        arms[index].hand = animator.GetBoneTransform(hand);
    }

    void LateUpdate()
    {
        if (neck == null || move == null)
            return;

        if (InJump())
        {
            LimitArmReach();
            return;
        }

        bool idle = !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle");
        if (idle && !move.IsWinding)
        {
            neckPosition = neck.localPosition;
            neckRotation = neck.localRotation;
            neckScale = neck.localScale;
            if (head != null)
            {
                headPosition = head.localPosition;
                headRotation = head.localRotation;
                headScale = head.localScale;
            }

            hasRestPose = true;
            CacheArms();
            return;
        }

        if (!move.IsWinding)
            return;

        if (!hasRestPose)
            return;

        neck.localPosition = neckPosition;
        neck.localRotation = neckRotation;
        neck.localScale = neckScale;

        if (head == null)
            return;

        head.localPosition = headPosition;
        head.localRotation = headRotation;
        head.localScale = headScale;
    }

    bool InJump()
    {
        int layer = animator.GetLayerIndex("Jump Layer");
        if (layer < 0)
            layer = 0;

        if (IsJumpState(animator.GetCurrentAnimatorStateInfo(layer)))
            return true;

        return animator.IsInTransition(layer) && IsJumpState(animator.GetNextAnimatorStateInfo(layer));
    }

    static bool IsJumpState(AnimatorStateInfo state)
    {
        return state.IsName("HumanM@Jump01") || state.IsName("Jump");
    }

    void CacheArms()
    {
        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i].upper == null || arms[i].hand == null)
                return;

            Transform root = arms[i].shoulder != null ? arms[i].shoulder : arms[i].upper;
            arms[i].upperLocal = arms[i].upper.localRotation;
            if (arms[i].lower != null)
            {
                arms[i].lowerLocal = arms[i].lower.localRotation;
                arms[i].lowerPosition = arms[i].lower.localPosition;
            }

            arms[i].handPosition = arms[i].hand.localPosition;
            arms[i].reach = Vector3.Distance(root.position, arms[i].hand.position);
        }

        hasArmRest = true;
    }

    void LimitArmReach()
    {
        if (!hasArmRest)
            return;

        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i].upper == null || arms[i].hand == null)
                continue;

            if (arms[i].lower != null)
                arms[i].lower.localPosition = arms[i].lowerPosition;
            arms[i].hand.localPosition = arms[i].handPosition;

            arms[i].upper.localRotation = Quaternion.Slerp(arms[i].upper.localRotation, arms[i].upperLocal, 0.55f);
            if (arms[i].lower != null)
                arms[i].lower.localRotation = Quaternion.Slerp(arms[i].lower.localRotation, arms[i].lowerLocal, 0.4f);
        }
    }

    struct ArmRest
    {
        public Transform shoulder;
        public Transform upper;
        public Transform lower;
        public Transform hand;
        public Quaternion upperLocal;
        public Quaternion lowerLocal;
        public Vector3 lowerPosition;
        public Vector3 handPosition;
        public float reach;
    }
}
