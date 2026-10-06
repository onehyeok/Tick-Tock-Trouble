using UnityEngine;
using UnityEngine.Events;

public class WinderAnimation : MonoBehaviour
{
    private Animator animator;
    private WinderMove move;

    [SerializeField] private UnityEvent _OnJump;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<WinderMove>();
    }

    void Update()
    {
        Attack();
        HitFaintedTickToy();
        Jump();

 

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

    void Attack()
    {
        if (move.IsDead)
            return;

        AnimatorStateInfo state =
            animator.GetCurrentAnimatorStateInfo(0);

        if (Input.GetMouseButton(0))
        {
            if (!state.IsName("Attack"))
            {
                animator.SetTrigger("Attack");
            }
        }
    }

    void HitFaintedTickToy()
    {
        if (move.IsDead || !animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
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