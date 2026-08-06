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
        [Min(0f)]
        [SerializeField] private float retriggerDelay = 0.5f;
        [Tooltip("Invoked when falling into the void requests the unified " +
                 "death/respawn flow.")]
        [SerializeField] private UnityEvent onVoidRespawned;

        private float nextAllowedRespawnTime;
        private PlayerHealth playerHealth;
        private BombPlayerRespawn playerRespawn;
        private PlayerCheckpointManager checkpointManager;

        private void Awake()
        {
            playerHealth = GetComponent<PlayerHealth>();
            playerRespawn = GetComponent<BombPlayerRespawn>();
            checkpointManager = GetComponent<PlayerCheckpointManager>();
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

            if (playerHealth == null)
            {
                playerHealth = GetComponent<PlayerHealth>();
            }
            if (playerRespawn == null)
            {
                playerRespawn = GetComponent<BombPlayerRespawn>();
            }
            if (checkpointManager == null)
            {
                checkpointManager = GetComponent<PlayerCheckpointManager>();
            }

            if (playerHealth != null)
            {
                if (playerHealth.IsDead)
                {
                    return;
                }

                playerHealth.Kill(PlayerDamageType.Void, gameObject);
                onVoidRespawned?.Invoke();
                return;
            }

            // Compatibility fallback for unusual player setups. Position is
            // still owned by the unified respawn/checkpoint systems; this
            // component never stores or selects a respawn point itself.
            if (playerRespawn != null)
            {
                playerRespawn.RequestRespawn();
                onVoidRespawned?.Invoke();
                return;
            }

            if (checkpointManager != null && checkpointManager.HasCheckpoint)
            {
                checkpointManager.RestoreLatestCheckpoint();
                onVoidRespawned?.Invoke();
            }
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
