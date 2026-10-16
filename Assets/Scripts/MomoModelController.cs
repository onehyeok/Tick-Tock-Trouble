using UnityEngine;

public class MomoModelController : MonoBehaviour
{
    [SerializeField] private float neckLiftDegrees = 0f;

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

    void Awake()
    {
        move = GetComponent<TickToyMove>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
            return;

        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void LateUpdate()
    {
        if (neck == null || move == null)
            return;

        if (!move.IsWinding)
        {
            if (animator.IsInTransition(0) || !animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
                return;

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
            return;
        }

        if (!hasRestPose)
            return;

        neck.localPosition = neckPosition;
        neck.localRotation = neckRotation;
        neck.localScale = neckScale;

        if (head != null)
        {
            head.localPosition = headPosition;
            head.localRotation = headRotation;
            head.localScale = headScale;
        }

        neck.Rotate(transform.right, -neckLiftDegrees, Space.World);
    }
}
