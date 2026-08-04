using UnityEngine;
using UnityEngine.InputSystem;

namespace SuperFinalFuse.PlatformReveal
{
    /// <summary>
    /// Standalone test helper — drop this on an empty GameObject,
    /// place it near a platform, and use the context menu or press T
    /// to fire a reveal without needing the real explosion system.
    /// </summary>
    public sealed class TestRevealTrigger : MonoBehaviour
    {
        [Header("Reveal params")]
        [Min(0.1f)]
        [SerializeField] private float radius = 5f;
        [Min(0f)]
        [SerializeField] private float holdDuration = 2f;
        [Min(0.01f)]
        [SerializeField] private float fadeDuration = 1.5f;

        [Header("Input")]
        [Tooltip("Press this key in Play mode to fire a reveal at this object's position.")]
        [SerializeField] private Key triggerKey = Key.T;

        private void Update()
        {
            if (Keyboard.current != null &&
                Keyboard.current[triggerKey].wasPressedThisFrame)
            {
                Fire();
            }
        }

        [ContextMenu("Fire Reveal")]
        public void Fire()
        {
            Vector3 pos = transform.position;
            Debug.Log(
                $"[TestRevealTrigger] Firing reveal — pos: {pos}, "
                + $"radius: {radius}, hold: {holdDuration}s, fade: {fadeDuration}s",
                this);

            PlatformRevealManager.Instance.RegisterReveal(
                pos, radius, holdDuration, fadeDuration);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
