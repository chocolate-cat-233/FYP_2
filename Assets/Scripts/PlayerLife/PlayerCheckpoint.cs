using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.PlayerLife
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class PlayerCheckpoint : MonoBehaviour
    {
        [Tooltip("Optional exact respawn pose. This object's Transform is used " +
                 "when empty.")]
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private bool activateOnlyOnce = true;
        [SerializeField] private UnityEvent onActivated;

        private bool activated;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (activateOnlyOnce && activated)
            {
                return;
            }

            PlayerCheckpointManager manager =
                other.GetComponentInParent<PlayerCheckpointManager>();
            if (manager == null)
            {
                return;
            }

            Transform point = respawnPoint != null
                ? respawnPoint
                : transform;
            manager.CaptureCheckpoint(point);
            activated = true;
            onActivated?.Invoke();
        }
    }
}
