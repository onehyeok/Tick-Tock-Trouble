using UnityEngine;
using UnityEngine.Events;

public class WinderAnimation : MonoBehaviour
{
    private Animator animator;
    private WinderMove move;
    private bool attackArmed;
    private bool ignoreHeldClick;

    [SerializeField] private UnityEvent _OnJump;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<WinderMove>();
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
                HitFaintedTickToy();
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

    void HitFaintedTickToy()
    {
        if (!IsSwinging())
            return;

        Vector3 origin = transform.position + transform.forward * 0.7f + Vector3.up * 0.8f;
        Collider[] hits = Physics.OverlapSphere(origin, 0.9f);
        for (int i = 0; i < hits.Length; i++)
        {
            TickToyMove toy = hits[i].GetComponentInParent<TickToyMove>();
            if (toy != null)
                toy.BreakFaint();
        }
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