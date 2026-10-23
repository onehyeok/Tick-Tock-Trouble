using UnityEngine;

public class JephyModelController : MonoBehaviour
{
    void Awake()
    {
        Animator animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
            return;

        ArmpitSeparator.Apply(animator);
    }
}
