using UnityEngine;

public class WinderAnimation : MonoBehaviour
{
    private Animator animator;
    private WinderMove move;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        move = GetComponent<WinderMove>();
    }

    void Update()
    {
        UpdateAnimation();
        Attack();
        Jump();
    }

    void UpdateAnimation()
    {
        if (move.MoveInput.magnitude > 0.1f)
        {
            animator.SetInteger("State", 1);
        }
        else
        {
            animator.SetInteger("State", 0);
        }
    }

    void Attack()
    {
        if(Input.GetMouseButtonDown(0))
        {
            animator.SetTrigger("Attack");
        }
    }
    
    void Jump()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            animator.SetTrigger("Jump");
        }
    }
}