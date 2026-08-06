using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    public sealed class VoidFallRespawn : MonoBehaviour
    {
        [Header("Void detection")]
        [Tooltip("The player respawns after falling below this world-space Y position.")]
        [SerializeField] private float voidHeight = -10f;

        [Header("Respawn")]
        [Tooltip("Optional. When empty, the player's position at game start is used.")]
        [SerializeField] private Transform respawnPoint;
        [Min(0f)]
        [SerializeField] private float retriggerDelay = 0.5f;
        [SerializeField] private UnityEvent onVoidRespawned;

        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Rigidbody[] bodies;
        private float nextAllowedRespawnTime;

        private void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            bodies = GetComponentsInChildren<Rigidbody>();
        }

        private void Update()
        {
            if (transform.position.y < voidHeight &&
                Time.time >= nextAllowedRespawnTime)
            {
                RespawnFromVoid();
            }
        }

        [ContextMenu("Respawn From Void")]
        public void RespawnFromVoid()
        {
            nextAllowedRespawnTime = Time.time + retriggerDelay;

            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.Kill(PlayerDamageType.Void, gameObject);
                onVoidRespawned?.Invoke();
                return;
            }

            foreach (Rigidbody body in bodies)
            {
                if (body == null)
                {
                    continue;
                }

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Vector3 targetPosition = respawnPoint != null
                ? respawnPoint.position
                : initialPosition;
            Quaternion targetRotation = respawnPoint != null
                ? respawnPoint.rotation
                : initialRotation;

            transform.SetPositionAndRotation(targetPosition, targetRotation);
            Physics.SyncTransforms();
            onVoidRespawned?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.7f);
            Vector3 center = transform.position;
            center.y = voidHeight;
            Gizmos.DrawWireCube(center, new Vector3(20f, 0.05f, 20f));
        }
    }
}
