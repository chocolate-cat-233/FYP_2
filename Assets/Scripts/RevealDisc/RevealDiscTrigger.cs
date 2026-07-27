using UnityEngine;

namespace SuperFinalFuse.RevealDisc
{
    /// <summary>
    /// Place on a GameObject with a Trigger Collider at hidden-hole
    /// locations. When the player enters, the reveal disc activates;
    /// when the player leaves, it deactivates (unless one-shot).
    ///
    /// Can also be controlled manually via ActivateOnce() so other
    /// gameplay systems (cutscenes, puzzles) can trigger the disc.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class RevealDiscTrigger : MonoBehaviour
    {
        [Header("Trigger Settings")]
        [Tooltip("Tag of the player GameObject.")]
        [SerializeField]
        private string playerTag = "Player";

        [Tooltip("If true, the disc stays active forever after the first trigger.")]
        [SerializeField]
        private bool oneShot;

        [Tooltip("Optional: automatically deactivate when the player leaves the trigger.")]
        [SerializeField]
        private bool deactivateOnExit = true;

        [Header("Override Radius")]
        [Tooltip("If > 0, overrides the controller's default disc radius while inside this trigger.")]
        [SerializeField, Min(0f)]
        private float overrideRadius;

        [Header("Debug")]
        [SerializeField]
        private bool showTriggerGizmo = true;

        // ── State ─────────────────────────────────────────────────────────
        private RevealDiscController cachedController;
        private bool hasTriggered;

        // ── Unity messages ────────────────────────────────────────────────
        private void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (!col.isTrigger)
            {
                col.isTrigger = true;
                Debug.Log($"RevealDiscTrigger on '{name}': Collider set to IsTrigger = true.", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (oneShot && hasTriggered) return;

            RevealDiscController controller = ResolveController(other);
            if (controller == null) return;

            if (overrideRadius > 0f)
                controller.SetRadius(overrideRadius);

            controller.Activate();
            hasTriggered = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (oneShot || !deactivateOnExit) return;

            RevealDiscController controller = ResolveController(other);
            if (controller != null)
                controller.Deactivate();
        }

        // ── Public API ────────────────────────────────────────────────────
        /// <summary>
        /// Force-activates the disc. Call this from other systems
        /// (cutscenes, dialogue, puzzle solves) to trigger the reveal.
        /// </summary>
        public void ActivateOnce()
        {
            if (cachedController != null)
            {
                if (overrideRadius > 0f)
                    cachedController.SetRadius(overrideRadius);
                cachedController.Activate();
                hasTriggered = true;
                return;
            }

            // Try to find the player dynamically
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                cachedController = player.GetComponent<RevealDiscController>();
                if (cachedController != null)
                {
                    if (overrideRadius > 0f)
                        cachedController.SetRadius(overrideRadius);
                    cachedController.Activate();
                    hasTriggered = true;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private RevealDiscController ResolveController(Collider playerCollider)
        {
            if (cachedController == null)
            {
                cachedController = playerCollider.GetComponent<RevealDiscController>();
                if (cachedController == null)
                {
                    Debug.LogWarning(
                        $"Player '{playerCollider.name}' entered RevealDiscTrigger " +
                        $"'{name}' but has no {nameof(RevealDiscController)} attached.",
                        this);
                }
            }
            return cachedController;
        }

        private void OnDrawGizmos()
        {
            if (!showTriggerGizmo) return;

            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.25f);

            if (col is BoxCollider box)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position,
                                              transform.rotation,
                                              box.size);
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position,
                                              transform.rotation,
                                              Vector3.one);
                Gizmos.DrawSphere(sphere.center, sphere.radius);
            }
            else
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position,
                                              transform.rotation,
                                              Vector3.one);
                Gizmos.DrawCube(Vector3.zero, Vector3.one);
            }
        }
    }
}
