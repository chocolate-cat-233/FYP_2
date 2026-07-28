using UnityEngine;

public sealed class LeverVisualController : MonoBehaviour
{
    [SerializeField] private Animator animator;

    private static readonly int IsOn =
        Animator.StringToHash("IsOn");

    public void Activate()
    {
        animator.SetBool(IsOn, true);
    }

    public void Deactivate()
    {
        animator.SetBool(IsOn, false);
    }
}