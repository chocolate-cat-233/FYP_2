using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum BombPipeState
    {
        Empty,
        Occupied
    }

    [DisallowMultipleComponent]
    public sealed class BombPipe : MonoBehaviour, IBombDirectHitReceiver
    {
        [Header("Pipe route")]
        [Tooltip("Optional position where the captured bomb waits.")]
        [SerializeField] private Transform capturePoint;
        [SerializeField] private Transform exit;
        [Min(0f)]
        [SerializeField] private float travelDuration = 1f;
        [Min(0f)]
        [SerializeField] private float exitSpeed = 8f;
        [Min(0f)]
        [Tooltip("Prevents a bomb released inside the pipe trigger from being " +
                 "captured again immediately.")]
        [SerializeField] private float releasedBombIgnoreDuration = 0.25f;
        [SerializeField] private BombOwnerMask acceptedOwners = BombOwnerMask.All;

        [Header("Events")]
        [SerializeField] private UnityEvent onBombEntered;
        [SerializeField] private UnityEvent onBombReleased;

        [SerializeField, HideInInspector] private BombPipeState state;
        private BombProjectile capturedBomb;
        private BombProjectile lastReleasedBomb;
        private float ignoreReleasedBombUntil;
        private Coroutine travelRoutine;

        public BombPipeState State => state;
        public BombProjectile CapturedBomb => capturedBomb;

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (!acceptedOwners.Contains(hit.OwnerType) ||
                state == BombPipeState.Occupied ||
                hit.Projectile == null ||
                hit.Projectile == lastReleasedBomb &&
                Time.time < ignoreReleasedBombUntil ||
                exit == null)
            {
                return BombHitResult.Ignore;
            }

            Transform anchor = capturePoint != null ? capturePoint : transform;
            if (!hit.Projectile.Capture(anchor))
            {
                return BombHitResult.Ignore;
            }

            capturedBomb = hit.Projectile;
            capturedBomb.Completed += HandleCapturedBombCompleted;
            state = BombPipeState.Occupied;
            onBombEntered?.Invoke();
            travelRoutine = StartCoroutine(TravelRoutine());
            return BombHitResult.Capture;
        }

        private IEnumerator TravelRoutine()
        {
            if (travelDuration > 0f)
            {
                yield return new WaitForSeconds(travelDuration);
            }

            travelRoutine = null;
            ReleaseBomb();
        }

        [ContextMenu("Release Captured Bomb")]
        public void ReleaseBomb()
        {
            if (capturedBomb == null || state != BombPipeState.Occupied)
            {
                ClearOccupiedState();
                return;
            }

            BombProjectile bomb = capturedBomb;
            bomb.Completed -= HandleCapturedBombCompleted;
            ClearOccupiedState();
            lastReleasedBomb = bomb;
            ignoreReleasedBombUntil =
                Time.time + releasedBombIgnoreDuration;

            Vector3 direction = exit.forward.sqrMagnitude > 0.001f
                ? exit.forward.normalized
                : Vector3.forward;
            bomb.Release(
                exit.position,
                Quaternion.LookRotation(direction, Vector3.up),
                direction * exitSpeed);
            onBombReleased?.Invoke();
        }

        private void HandleCapturedBombCompleted(BombProjectile bomb)
        {
            if (bomb == capturedBomb)
            {
                ClearOccupiedState();
            }
        }

        private void ClearOccupiedState()
        {
            if (travelRoutine != null)
            {
                StopCoroutine(travelRoutine);
                travelRoutine = null;
            }

            capturedBomb = null;
            state = BombPipeState.Empty;
        }

        private void OnDisable()
        {
            if (capturedBomb != null)
            {
                capturedBomb.Completed -= HandleCapturedBombCompleted;
                capturedBomb.Disarm();
            }

            ClearOccupiedState();
        }

        private void OnDrawGizmosSelected()
        {
            if (exit == null)
            {
                return;
            }

            Vector3 start = capturePoint != null
                ? capturePoint.position
                : transform.position;
            Gizmos.color = state == BombPipeState.Occupied
                ? Color.yellow
                : Color.cyan;
            Gizmos.DrawLine(start, exit.position);
            Gizmos.DrawWireSphere(exit.position, 0.15f);
            Gizmos.DrawRay(exit.position, exit.forward * 1.5f);
        }
    }
}
