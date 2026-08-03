using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Buffs;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    public sealed class BombPlayerRespawn : MonoBehaviour, IBombBlastReactive
    {
        [Header("Respawn")]
        [Min(0f)]
        [SerializeField] private float respawnDelay = 1.5f;
        [SerializeField] private Transform respawnPoint;
        [Tooltip("Optional movement scripts to disable while knocked out.")]
        [SerializeField] private Behaviour[] controlsToDisable;

        [Header("Knockout visual")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Vector3 knockedOutEuler =
            new Vector3(0f, 0f, 90f);
        [SerializeField] private UnityEvent onKnockedOut;
        [SerializeField] private UnityEvent onRespawned;

        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Quaternion visualInitialLocalRotation;
        private bool isRespawning;

        private void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            if (visualRoot != null)
            {
                visualInitialLocalRotation = visualRoot.localRotation;
            }
        }

        public void OnBombBlast(BombBlastHit hit)
        {
            BombBuffController buffs = GetComponent<BombBuffController>();
            if (buffs != null && buffs.IsBombBlastImmune)
            {
                return;
            }

            if (!isRespawning)
            {
                StartCoroutine(RespawnRoutine());
            }
        }

        private IEnumerator RespawnRoutine()
        {
            isRespawning = true;
            SetControlsEnabled(false);
            if (visualRoot != null)
            {
                visualRoot.localRotation =
                    Quaternion.Euler(knockedOutEuler) *
                    visualInitialLocalRotation;
            }

            onKnockedOut?.Invoke();
            // Respawn must complete even if another system pauses game time.
            yield return new WaitForSecondsRealtime(respawnDelay);

            transform.SetPositionAndRotation(
                respawnPoint != null ? respawnPoint.position : initialPosition,
                respawnPoint != null ? respawnPoint.rotation : initialRotation);

            if (visualRoot != null)
            {
                visualRoot.localRotation = visualInitialLocalRotation;
            }

            SetControlsEnabled(true);
            isRespawning = false;
            onRespawned?.Invoke();
        }

        private void SetControlsEnabled(bool value)
        {
            foreach (Behaviour control in controlsToDisable)
            {
                if (control == null)
                {
                    continue;
                }

                // BombThrower must remain enabled because its Update performs
                // projectile cleanup and stale-lock recovery.
                if (control is BombThrower bombThrower)
                {
                    bombThrower.SetInputBlocked(!value);
                    continue;
                }

                control.enabled = value;
            }
        }
    }
}
