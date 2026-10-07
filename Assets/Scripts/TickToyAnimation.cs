using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TickToyAnimation : MonoBehaviour
{
    private Animator animator;
    private TickToyMove move;
    private GameObject[] spineSprings;
    private Transform leftUpperLeg;
    private Transform rightUpperLeg;
    private Transform leftLowerLeg;
    private Transform rightLowerLeg;

    [SerializeField] private UnityEvent _OnJump;
    [SerializeField] private float windLegSpread = 18f;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<TickToyMove>();
        CacheSpineSprings();
        SetSpineSpringsActive(false);

        if (animator != null && animator.isHuman)
        {
            leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            move.IsDead = true;
            animator.SetTrigger("Die");
        }

        if (move.IsDead)
            return;

        if (move.IsFainted)
        {
            animator.SetBool("IsWinding", false);
            move.IsWinding = false;
            SetSpineSpringsActive(false);
            SetFaint(true);
            return;
        }

        Jump();
        UpdateWind();

        if (Input.GetKeyDown(KeyCode.E))
        {
            animator.SetTrigger("WindPause");
        }
    }

    public void SetFaint(bool faint)
    {
        if (faint)
        {
            animator.SetBool("IsWinding", false);
            if (!IsInState("Die"))
                animator.CrossFadeInFixedTime("Die", 0.1f, 0, 0f);
            return;
        }

        if (!move.IsDead && IsInState("Die"))
            animator.CrossFadeInFixedTime("Idle", 0.15f, 0);
    }

    bool IsInState(string stateName)
    {
        if (animator.IsInTransition(0))
            return animator.GetNextAnimatorStateInfo(0).IsName(stateName);

        return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
    }

    void UpdateWind()
    {
        bool isWinding =
            Input.GetMouseButton(0) && !move.IsDead && move.MoveInput.sqrMagnitude < 0.01f && !Input.GetKey(KeyCode.Space) && !Input.GetKey(KeyCode.Z);

        animator.SetBool("IsWinding", isWinding);
        move.IsWinding = isWinding;
        SetSpineSpringsActive(isWinding);
    }

    void CacheSpineSprings()
    {
        List<GameObject> springs = new List<GameObject>();
        Transform[] transforms = GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name != "Spine")
                continue;

            foreach (Transform child in transforms[i])
            {
                if (child.name.StartsWith("Spring"))
                    springs.Add(child.gameObject);
            }
        }

        spineSprings = springs.ToArray();
    }

    void SetSpineSpringsActive(bool active)
    {
        if (spineSprings == null)
            return;

        for (int i = 0; i < spineSprings.Length; i++)
        {
            if (spineSprings[i] != null && spineSprings[i].activeSelf != active)
                spineSprings[i].SetActive(active);
        }
    }

    void LateUpdate()
    {
        if (leftUpperLeg == null || !animator.GetBool("IsWinding"))
            return;

        SpreadLeg(leftUpperLeg, leftLowerLeg, -animator.transform.right);
        SpreadLeg(rightUpperLeg, rightLowerLeg, animator.transform.right);
    }

    void SpreadLeg(Transform upperLeg, Transform lowerLeg, Vector3 outward)
    {
        if (upperLeg == null || lowerLeg == null)
            return;

        Vector3 leg = lowerLeg.position - upperLeg.position;
        Vector3 axis = Vector3.Cross(leg, outward);
        if (axis.sqrMagnitude < 0.0001f)
            return;

        upperLeg.rotation = Quaternion.AngleAxis(windLegSpread, axis.normalized) * upperLeg.rotation;
    }

    public void SetMove(bool isMove)
    {
        animator.SetInteger("State", isMove ? 1 : 0);
    }

    public void SetProne(bool isProne)
    {
        animator.SetBool("IsProne", isProne);
    }

    void Jump()
    {
        if (move.IsDead)
            return;

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        if (Input.GetKey(KeyCode.Space)
            && move.IsGrounded)
        {
            if (!state.IsName("Jump"))
            {
                _OnJump.Invoke();
                animator.SetTrigger("Jump");
            }
        }
    }
}