using UnityEngine;
using UnityEngine.Events;

public class TickToyAnimation : MonoBehaviour
{
    private Animator animator;
    private TickToyMove move;

    [SerializeField] private UnityEvent _OnJump;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<TickToyMove>();
    }

    void Update()
    {
        Jump();

       

        // 테스트용 사망
        if (Input.GetKeyDown(KeyCode.K))
        {
            move.IsDead = true;

            animator.SetTrigger("Die");
        }
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