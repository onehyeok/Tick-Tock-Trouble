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
    private Vector3 modelRestLocal;
    private bool modelShifted;
    private bool ignoreWindUntilRelease;

    [SerializeField] private UnityEvent _OnJump;
    [SerializeField] private float windLegSpread = 11f;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<TickToyMove>();
        if (animator != null)
            animator.applyRootMotion = false;
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
        if (Input.GetKeyDown(KeyCode.H))
            PlayGetHit();

        if (Input.GetKeyDown(KeyCode.K))
        {
            move.IsDead = true;
            animator.SetTrigger("Die");
        }

        switch (move.State)
        {
            case TickToyMove.TickToyState.Dead:
                return;
            case TickToyMove.TickToyState.Faint:
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
            WinderMove winder = move.FindWinderInReach();
            if (winder == null)
                return;

            int windLayer = animator.GetLayerIndex("Wind Layer");
            if (windLayer >= 0)
                animator.CrossFadeInFixedTime("WindPause", 0.05f, windLayer, 0f);
            else
                animator.SetTrigger("WindPause");
            winder.Freeze(5f);
        }
    }

    public void SetFaint(bool faint, bool run = false)
    {
        if (faint)
        {
            animator.SetBool("IsWinding", false);
            if (!IsInState("Die"))
                animator.CrossFadeInFixedTime("Die", 0.1f, 0, 0f);
            return;
        }

        if (move.IsDead || !IsInState("Die"))
            return;

        animator.ResetTrigger("Die");
        animator.SetBool("IsWinding", false);
        move.IsWinding = false;
        SetSpineSpringsActive(false);
        if (run)
            ApplyRunDirection(move.MoveInput);
        animator.SetInteger("State", run ? 1 : 0);
        animator.CrossFadeInFixedTime(run ? "Run" : "Idle", 0.15f, 0, 0f);
        if (Input.GetMouseButton(0))
            ignoreWindUntilRelease = true;
    }

    bool IsInState(string stateName)
    {
        if (animator.IsInTransition(0))
            return animator.GetNextAnimatorStateInfo(0).IsName(stateName);

        return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
    }

    void UpdateWind()
    {
        if (ignoreWindUntilRelease)
        {
            if (Input.GetMouseButton(0))
            {
                animator.SetBool("IsWinding", false);
                move.IsWinding = false;
                SetSpineSpringsActive(false);
                return;
            }

            ignoreWindUntilRelease = false;
        }

        bool isWinding =
            Input.GetMouseButton(0) && !move.IsDead && move.MoveInput.sqrMagnitude < 0.01f && !Input.GetKey(KeyCode.Space) && !Input.GetKey(KeyCode.LeftControl);

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
        if (animator == null)
            return;

        bool winding = leftUpperLeg != null && animator.GetBool("IsWinding");
        if (winding && !IsProne() && !move.IsDead && !move.IsFainted)
        {
            SpreadLegsOnSpot();
            move.FitProneCollider();
            return;
        }

        if (ShouldPlantFeet())
        {
            if (!modelShifted)
            {
                modelRestLocal = animator.transform.localPosition;
                modelShifted = true;
            }

            // Die/Faint는 등·엉덩이가 가장 낮음 — 발만 심으면 몸이 바닥에 파묻힘
            PlantFeet(IsProne() || move.IsDead || move.IsFainted);
            move.FitProneCollider();
            return;
        }

        RestoreModel();
        move.FitProneCollider();
    }

    void RestoreModel()
    {
        if (!modelShifted)
            return;

        animator.transform.localPosition = modelRestLocal;
        modelShifted = false;
    }

    void SpreadLegsOnSpot()
    {
        RestoreModel();

        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (leftFoot == null || rightFoot == null)
        {
            SpreadLeg(leftUpperLeg, leftLowerLeg, -animator.transform.right);
            SpreadLeg(rightUpperLeg, rightLowerLeg, animator.transform.right);
            return;
        }

        float before = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        SpreadLeg(leftUpperLeg, leftLowerLeg, -animator.transform.right);
        SpreadLeg(rightUpperLeg, rightLowerLeg, animator.transform.right);
        float rise = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - before;
        if (rise <= 0.001f)
            return;

        modelRestLocal = animator.transform.localPosition;
        modelShifted = true;
        animator.transform.position -= Vector3.up * rise;
    }

    bool IsProne()
    {
        return animator.GetBool("IsProne") || IsInState("Prone");
    }

    bool ShouldPlantFeet()
    {
        if (move.IsDead || move.IsFainted)
            return IsInState("Die");

        if (!move.IsGrounded)
            return false;

        return IsProne();
    }

    void PlantFeet(bool includeHands)
    {
        if (!animator.isHuman)
            return;

        bool downed = move != null && (move.IsDead || move.IsFainted);
        float contactY = float.PositiveInfinity;
        Vector3 sample = animator.transform.position;

        AccBoneY(HumanBodyBones.LeftFoot, ref contactY, ref sample);
        AccBoneY(HumanBodyBones.RightFoot, ref contactY, ref sample);
        if (includeHands || downed)
        {
            AccBoneY(HumanBodyBones.LeftHand, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.RightHand, ref contactY, ref sample);
        }

        if (downed)
        {
            AccBoneY(HumanBodyBones.Hips, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.Spine, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.Chest, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.Head, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.LeftUpperArm, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.RightUpperArm, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.LeftLowerLeg, ref contactY, ref sample);
            AccBoneY(HumanBodyBones.RightLowerLeg, ref contactY, ref sample);
        }

        if (float.IsPositiveInfinity(contactY))
            return;

        Vector3 origin = new Vector3(sample.x, contactY + 2f, sample.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float groundY = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform == transform || hits[i].collider.transform.IsChildOf(transform))
                continue;
            if (hits[i].point.y > groundY)
                groundY = hits[i].point.y;
        }

        if (groundY == float.NegativeInfinity)
            return;

        // 공중 낙하 중엔 메시만 따로 끌어내리지 않음
        if (downed && contactY > groundY + 0.9f)
            return;

        // 틱토이 Die 메시는 본보다 두꺼워 바닥에 파묻힘 → 살짝 띄움
        float pad = downed ? 0.14f : 0f;
        float gap = contactY - (groundY + pad);
        float maxGap = downed ? 2.5f : 1.2f;
        if (Mathf.Abs(gap) <= 0.015f || Mathf.Abs(gap) > maxGap)
            return;

        animator.transform.position -= Vector3.up * gap;
    }

    void AccBoneY(HumanBodyBones bone, ref float lowestY, ref Vector3 sample)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null)
            return;
        if (t.position.y < lowestY)
        {
            lowestY = t.position.y;
            sample = t.position;
        }
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
        if (isMove)
            ApplyRunDirection(move.MoveInput);
        animator.SetInteger("State", isMove ? 1 : 0);
    }

    void ApplyRunDirection(Vector2 input)
    {
        if (input.sqrMagnitude > 1f)
            input.Normalize();
        animator.SetFloat("MoveX", input.x);
        animator.SetFloat("MoveY", input.y);
    }

    public void SetProne(bool isProne)
    {
        animator.SetBool("IsProne", isProne);
    }

    public void PlayGetHit()
    {
        if (animator == null || move.IsDead)
            return;

        if (HasTrigger("GetHit"))
        {
            animator.ResetTrigger("GetHit");
            animator.SetTrigger("GetHit");
            return;
        }

        int layer = animator.GetLayerIndex("Hit Layer");
        if (layer < 0)
            layer = animator.GetLayerIndex("GetHit");
        if (layer >= 0)
            animator.CrossFadeInFixedTime("GetHit", 0.05f, layer, 0f);
    }

    public void PlayDie()
    {
        if (animator == null)
            return;

        animator.SetTrigger("Die");
    }

    bool HasTrigger(string name)
    {
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger
                && parameters[i].name == name)
                return true;
        }

        return false;
    }

    public bool IsFaintSettled()
    {
        if (animator.IsInTransition(0))
            return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsName("Die") && state.normalizedTime >= 0.45f;
    }

    void Jump()
    {
        if (move.IsDead)
            return;

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        if (Input.GetKey(KeyCode.Space)
            && move.CanJump)
        {
            if (!state.IsName("Jump"))
            {
                _OnJump.Invoke();
                animator.SetTrigger("Jump");
            }
        }
    }
}