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
        [Tooltip("Optional second pipe end. If empty, it is mirrored from Exit " +
                 "across Capture Point, so existing pipes work bidirectionally.")]
        [SerializeField] private Transform entrance;
        [Tooltip("One end of the pipe. Its forward direction is used when a bomb " +
                 "leaves from this end.")]
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
        private bool releaseFromExit;

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
            // Capture immediately moves the projectile to the route center, so
            // remember which end it touched before changing its transform.
            Vector3 entryPosition = hit.Projectile.transform.position;
            if (!hit.Projectile.Capture(anchor))
            {
                return BombHitResult.Ignore;
            }

            capturedBomb = hit.Projectile;
            capturedBomb.Completed += HandleCapturedBombCompleted;
            // A bomb entering through one end must leave through the other.
            // Compare the actual hit/projectile position with both pipe ends.
            Vector3 entrancePosition = GetEntrancePosition(anchor.position);
            releaseFromExit =
                (entryPosition - entrancePosition).sqrMagnitude <=
                (entryPosition - exit.position).sqrMagnitude;
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

            Transform anchor = capturePoint != null ? capturePoint : transform;
            Vector3 releasePosition;
            Vector3 direction;

            if (releaseFromExit)
            {
                releasePosition = exit.position;
                direction = exit.forward.sqrMagnitude > 0.001f
                    ? exit.forward.normalized
                    : (exit.position - anchor.position).normalized;
            }
            else
            {
                releasePosition = GetEntrancePosition(anchor.position);
                direction = GetEntranceDirection(anchor.position);
            }

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.forward;
            }

            bomb.Release(
                releasePosition,
                Quaternion.LookRotation(direction, Vector3.up),
                direction * exitSpeed);
            onBombReleased?.Invoke();
        }

        private Vector3 GetEntrancePosition(Vector3 routeCenter)
        {
            if (entrance != null)
            {
                return entrance.position;
            }

            return routeCenter * 2f - exit.position;
        }

        private Vector3 GetEntranceDirection(Vector3 routeCenter)
        {
            if (entrance != null && entrance.forward.sqrMagnitude > 0.001f)
            {
                return entrance.forward.normalized;
            }

            return (GetEntrancePosition(routeCenter) - routeCenter).normalized;
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

            Vector3 center = capturePoint != null
                ? capturePoint.position
                : transform.position;
            Vector3 entrancePosition = GetEntrancePosition(center);
            Gizmos.color = state == BombPipeState.Occupied
                ? Color.yellow
                : Color.cyan;
            Gizmos.DrawLine(entrancePosition, exit.position);
            Gizmos.DrawWireSphere(entrancePosition, 0.15f);
            Gizmos.DrawRay(
                entrancePosition,
                GetEntranceDirection(center) * 1.5f);
            Gizmos.DrawWireSphere(exit.position, 0.15f);
            Gizmos.DrawRay(exit.position, exit.forward * 1.5f);
        }
    }
}
