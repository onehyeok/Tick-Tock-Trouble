using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class WinderAnimation : MonoBehaviour
{
    private const int AttackDamage = 30;
    private static readonly Collider[] AttackHits = new Collider[16];

    private Animator animator;
    private WinderMove move;
    private BoxCollider hitPoint;
    private readonly HashSet<int> hitThisSwing = new HashSet<int>();
    private bool attackArmed;
    private bool ignoreHeldClick;

    [SerializeField] private UnityEvent _OnJump;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<WinderMove>();
        CacheHitPoint();
    }

    void CacheHitPoint()
    {
        Transform found = transform.Find("HitPoint");
        if (found != null)
            hitPoint = found.GetComponent<BoxCollider>();

        if (hitPoint == null)
        {
            BoxCollider[] boxes = GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (boxes[i] != null && boxes[i].name == "HitPoint")
                {
                    hitPoint = boxes[i];
                    break;
                }
            }
        }

        if (hitPoint == null)
            return;

        hitPoint.isTrigger = true;
        Vector3 size = hitPoint.size;
        if (Mathf.Abs(size.z) < 0.2f)
            size.z = 0.45f;
        hitPoint.size = size;
    }

    public void SetPaused(bool paused)
    {
        if (animator != null)
            animator.speed = paused ? 0f : 1f;
    }

    void Update()
    {
        if (move.IsFrozen)
        {
            if (Input.GetKeyDown(KeyCode.K))
            {
                move.IsDead = true;
                animator.SetTrigger("Die");
            }
            return;
        }

        switch (move.State)
        {
            case WinderMove.WinderState.Dead:
                break;
            case WinderMove.WinderState.Faint:
                SetFaint(true);
                break;
            default:
                Attack();
                HitTickToys();
                Jump();
                break;
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            move.IsDead = true;
            animator.SetTrigger("Die");
        }
    }

    public void SetMove(bool isMove)
    {
        if (isMove)
        {
            animator.SetInteger("State", 1);
        }
        else
        {
            animator.SetInteger("State", 0);
        }
    }

    public void SetProne(bool isProne)
    {
        animator.SetBool("IsProne", isProne);
    }

    public void SetFaint(bool faint)
    {
        if (faint)
        {
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

    void Attack()
    {
        bool held = Input.GetMouseButton(0);
        bool swinging = IsSwinging();

        if (swinging)
        {
            attackArmed = false;
            animator.ResetTrigger("Attack");
            if (Input.GetMouseButtonDown(0))
                ignoreHeldClick = true;
            if (!held)
                ignoreHeldClick = false;
            return;
        }

        if (attackArmed)
            return;

        if (!held)
        {
            ignoreHeldClick = false;
            return;
        }

        if (ignoreHeldClick)
            return;

        attackArmed = true;
        animator.SetTrigger("Attack");
    }

    bool IsSwinging()
    {
        int layer = animator.GetLayerIndex("Attack Layer");
        if (layer < 0)
            return false;

        if (animator.IsInTransition(layer))
        {
            if (animator.GetCurrentAnimatorStateInfo(layer).IsName("Attack 0"))
                return true;
            if (animator.GetNextAnimatorStateInfo(layer).IsName("Attack 0"))
                return true;
        }

        return animator.GetCurrentAnimatorStateInfo(layer).IsName("Attack 0");
    }

    void HitTickToys()
    {
        if (!IsSwinging())
        {
            hitThisSwing.Clear();
            return;
        }

        if (hitPoint == null)
            CacheHitPoint();
        if (hitPoint == null)
            return;

        Vector3 size = hitPoint.size;
        size.x = Mathf.Max(Mathf.Abs(size.x), 0.2f);
        size.y = Mathf.Max(Mathf.Abs(size.y), 0.2f);
        size.z = Mathf.Max(Mathf.Abs(size.z), 0.4f);
        Vector3 halfExtents = Vector3.Scale(size * 0.5f, hitPoint.transform.lossyScale);
        Vector3 center = hitPoint.transform.TransformPoint(hitPoint.center);
        int count = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            AttackHits,
            hitPoint.transform.rotation,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = AttackHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            TickToyMove toy = hit.GetComponentInParent<TickToyMove>();
            if (toy == null || toy.IsDead || !hitThisSwing.Add(toy.GetInstanceID()))
                continue;

            toy.TakeHit(AttackDamage);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsSwinging() || other == null)
            return;
        if (other.transform == transform || other.transform.IsChildOf(transform))
            return;

        TickToyMove toy = other.GetComponentInParent<TickToyMove>();
        if (toy == null || toy.IsDead || !hitThisSwing.Add(toy.GetInstanceID()))
            return;

        toy.TakeHit(AttackDamage);
    }

    void Jump()
    {
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