using UnityEngine;

public class MomoModelController : MonoBehaviour
{
    private TickToyMove move;
    private Animator animator;
    private IdleFootClose idleFeet;

    void Awake()
    {
        move = GetComponent<TickToyMove>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
            return;

        ArmpitSeparator.Apply(animator);
        idleFeet = new IdleFootClose(animator);
    }

    void LateUpdate()
    {
        if (idleFeet != null)
            idleFeet.Apply(move, transform);
    }
}
