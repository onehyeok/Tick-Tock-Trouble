using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TickToyAnimation : MonoBehaviour
{
    private Animator animator;
    private TickToyMove move;
    private GameObject[] spineSprings;

    [SerializeField] private UnityEvent _OnJump;
    [SerializeField] private float windLegSpread = 8f;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<TickToyMove>();
        CacheSpineSprings();
        SetSpineSpringsActive(false);
    }

    void Update()
    {
        Jump();
        UpdateWind();

        // 테스트용 사망
        if (Input.GetKeyDown(KeyCode.K))
        {
            move.IsDead = true;

            animator.SetTrigger("Die");
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            animator.SetTrigger("WindPause");
        }
    }

    void UpdateWind()
    {
        bool isWinding =
            Input.GetMouseButton(0) && !move.IsDead && move.MoveInput.sqrMagnitude < 0.01f && !Input.GetKey(KeyCode.Space) && !Input.GetKey(KeyCode.Z);

        animator.SetBool("IsWinding", isWinding);
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
        if (!animator.GetBool("IsWinding"))
            return;

        SpreadLeg(HumanBodyBones.LeftUpperLeg, -windLegSpread);
        SpreadLeg(HumanBodyBones.RightUpperLeg, windLegSpread);
    }

    void SpreadLeg(HumanBodyBones bone, float degrees)
    {
        Transform leg = animator.GetBoneTransform(bone);
        if (leg == null)
            return;

        leg.rotation = Quaternion.AngleAxis(degrees, transform.forward) * leg.rotation;
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