using UnityEngine;
using UnityEngine.Events;

public class WinderAnimation : MonoBehaviour
{
    private Animator animator;
    
    [SerializeField] UnityEvent _OnJump;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        Attack();
        Jump();
    }

    public void SetMove(bool isMove)
    {
        if(isMove)
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
            _OnJump.Invoke();
            animator.SetTrigger("Jump");
        }
    }
}