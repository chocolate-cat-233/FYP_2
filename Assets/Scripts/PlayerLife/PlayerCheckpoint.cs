using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.PlayerLife
{
    [RequireComponent(typeof(Collider), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class PlayerCheckpoint : MonoBehaviour
    {
        [Tooltip("Optional exact respawn pose. This object's Transform is used " +
                 "when empty.")]
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private bool activateOnlyOnce = true;
        [SerializeField] private bool logActivation = true;
        [SerializeField] private UnityEvent onActivated;

        private bool activated;
        private Collider checkpointTrigger;
        private PlayerCheckpointManager overlappingManager;

        private void Awake()
        {
            checkpointTrigger = GetComponent<Collider>();
            checkpointTrigger.isTrigger = true;

            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
            {
                // Existing scene objects created before Rigidbody became a
                // requirement are upgraded safely at runtime.
                body = gameObject.AddComponent<Rigidbody>();
            }
            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
        }

        private void Update()
        {
            PlayerCheckpointManager manager = overlappingManager != null
                ? overlappingManager
                : FindFirstObjectByType<PlayerCheckpointManager>();
            if (manager == null || checkpointTrigger == null)
            {
                return;
            }

            bool overlapsPlayer = false;
            Collider[] playerColliders =
                manager.GetComponentsInChildren<Collider>();
            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null && playerCollider.enabled &&
                    !playerCollider.isTrigger &&
                    checkpointTrigger.bounds.Intersects(
                        playerCollider.bounds))
                {
                    overlapsPlayer = true;
                    break;
                }
            }

            if (overlapsPlayer)
            {
                if (overlappingManager == null)
                {
                    Activate(manager);
                }
                return;
            }

            // When Activate Only Once is disabled, leaving the volume arms it
            // for a future re-entry. Standing inside never saves every frame.
            if (overlappingManager == manager)
            {
                overlappingManager = null;
            }
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryActivate(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryActivate(other);
        }

        private void TryActivate(Collider other)
        {
            PlayerCheckpointManager manager =
                other.GetComponentInParent<PlayerCheckpointManager>();
            if (manager == null)
            {
                return;
            }

            Activate(manager);
        }

        private void Activate(PlayerCheckpointManager manager)
        {
            if (manager == null || overlappingManager == manager ||
                activateOnlyOnce && activated)
            {
                return;
            }

            overlappingManager = manager;

            Transform point = respawnPoint != null
                ? respawnPoint
                : transform;
            manager.CaptureCheckpoint(point);
            activated = true;
            if (logActivation)
            {
                Debug.Log(
                    $"Checkpoint '{name}' activated for '{manager.name}'. " +
                    $"Respawn position: {point.position}.",
                    this);
            }
            onActivated?.Invoke();
        }
    }
}
