using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum BombPipeState
    {
        Empty,
        Occupied
    }

    [DisallowMultipleComponent]
    public sealed class BombPipe : MonoBehaviour,
        IBombDirectHitReceiver,
        ICheckpointStateParticipant
    {
        [Header("Portal ends")]
        [Tooltip("Optional hidden holding position while a bomb is travelling.")]
        [SerializeField] private Transform capturePoint;
        [Tooltip("Portal A. Its Forward direction is used only when a bomb exits A.")]
        [SerializeField] private Transform entrance;
        [Tooltip("Portal B. Its Forward direction is used only when a bomb exits B.")]
        [SerializeField] private Transform exit;

        [Header("Ordered route")]
        [Tooltip("Children are read automatically in hierarchy order. The route is A -> children -> B.")]
        [SerializeField] private Transform pathPointsRoot;
        [SerializeField, HideInInspector] private Transform[] pathPoints =
            System.Array.Empty<Transform>();

        [Header("Travel")]
        [Min(0.01f)]
        [Tooltip("Internal bomb speed. Travel duration = ordered route length / speed.")]
        [SerializeField] private float internalTravelSpeed = 5f;
        [Tooltip("Keep the bomb's incoming speed magnitude when it exits. The destination portal still controls direction.")]
        [SerializeField] private bool preserveEntrySpeed = true;
        [Min(0f)]
        [Tooltip("Fallback/fixed exit speed. Used when Preserve Entry Speed is disabled, or when the incoming bomb is nearly stationary.")]
        [SerializeField] private float exitSpeed = 8f;
        [SerializeField] private bool hideBombWhileTravelling = true;
        [Min(0f)]
        [SerializeField] private float releasedBombIgnoreDuration = 0.25f;
        [Min(0f)]
        [SerializeField] private float releasedBombIgnoreDistance = 1f;
        [SerializeField] private BombOwnerMask acceptedOwners = BombOwnerMask.All;

        [Header("Events")]
        [SerializeField] private UnityEvent onBombEntered;
        [SerializeField] private UnityEvent onBombReleased;

        [SerializeField, HideInInspector] private BombPipeState state;
        private BombProjectile capturedBomb;
        private Coroutine travelRoutine;
        private bool releaseFromExit;
        private bool checkpointWasOccupied;
        private float activeTravelDuration;
        private float capturedEntrySpeed;

        public BombPipeState State => state;
        public BombProjectile CapturedBomb => capturedBomb;
        public Transform PortalA => entrance;
        public Transform PortalB => exit;
        public Transform PathPointsRoot => pathPointsRoot;
        public float InternalTravelSpeed => internalTravelSpeed;
        public float PipeLength => CalculateRouteLength();
        public float CurrentTravelDuration => state == BombPipeState.Occupied
            ? activeTravelDuration
            : PipeLength / Mathf.Max(0.01f, internalTravelSpeed);

        private void Awake()
        {
            RefreshPathPointsFromHierarchy();
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (hit.Projectile != null &&
                hit.Projectile.IsPipeRecaptureLocked)
            {
                return BombHitResult.ContinueFlight;
            }

            if (hit.Projectile == null ||
                !acceptedOwners.Contains(hit.OwnerType) ||
                state == BombPipeState.Occupied ||
                entrance == null || exit == null)
            {
                return BombHitResult.Ignore;
            }

            bool enteredAtA = DetermineEnteredAtA(
                hit.Collider,
                hit.Projectile.transform.position);
            Transform anchor = capturePoint != null ? capturePoint : transform;
            // Capture() deliberately clears the rigidbody velocity, so remember
            // its magnitude before putting the bomb into the pipe.
            float entrySpeed = hit.Projectile.Velocity.magnitude;
            if (!hit.Projectile.Capture(anchor, hideBombWhileTravelling))
            {
                return BombHitResult.Ignore;
            }

            capturedBomb = hit.Projectile;
            capturedBomb.Completed += HandleCapturedBombCompleted;
            capturedEntrySpeed = entrySpeed;
            releaseFromExit = enteredAtA;
            activeTravelDuration =
                PipeLength / Mathf.Max(0.01f, internalTravelSpeed);
            state = BombPipeState.Occupied;
            onBombEntered?.Invoke();
            travelRoutine = StartCoroutine(TravelRoutine(activeTravelDuration));
            return BombHitResult.Capture;
        }

        private IEnumerator TravelRoutine(float duration)
        {
            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
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
            Transform destination = releaseFromExit ? exit : entrance;
            Vector3 releasePosition = destination.position;
            Vector3 direction = destination.forward.sqrMagnitude > 0.001f
                ? destination.forward.normalized
                : Vector3.forward;
            float releaseSpeed = preserveEntrySpeed && capturedEntrySpeed > 0.01f
                ? capturedEntrySpeed
                : exitSpeed;

            ClearOccupiedState();
            bomb.SetPipeRecaptureLock(
                releasePosition,
                releasedBombIgnoreDuration,
                releasedBombIgnoreDistance);
            bomb.Release(
                releasePosition,
                Quaternion.LookRotation(direction, Vector3.up),
                direction * releaseSpeed);
            onBombReleased?.Invoke();
        }

        private bool DetermineEnteredAtA(
            Collider hitCollider,
            Vector3 bombPosition)
        {
            if (BelongsToPortal(hitCollider, entrance))
            {
                return true;
            }
            if (BelongsToPortal(hitCollider, exit))
            {
                return false;
            }

            return (bombPosition - entrance.position).sqrMagnitude <=
                   (bombPosition - exit.position).sqrMagnitude;
        }

        private static bool BelongsToPortal(
            Collider hitCollider,
            Transform portal)
        {
            if (hitCollider == null || portal == null)
            {
                return false;
            }

            Transform hitTransform = hitCollider.transform;
            return hitTransform == portal || hitTransform.IsChildOf(portal);
        }

        public float CalculateRouteLength()
        {
            if (entrance == null || exit == null)
            {
                return 0f;
            }

            float length = 0f;
            Vector3 previous = entrance.position;
            foreach (Transform point in pathPoints)
            {
                if (point == null)
                {
                    continue;
                }
                length += Vector3.Distance(previous, point.position);
                previous = point.position;
            }
            length += Vector3.Distance(previous, exit.position);
            return length;
        }

        public void RefreshPathPointsFromHierarchy()
        {
            if (pathPointsRoot == null)
            {
                Transform existing = transform.Find("PathPoints");
                if (existing != null)
                {
                    pathPointsRoot = existing;
                }
            }

            if (pathPointsRoot == null)
            {
                pathPoints = System.Array.Empty<Transform>();
                return;
            }

            pathPoints = new Transform[pathPointsRoot.childCount];
            for (int i = 0; i < pathPointsRoot.childCount; i++)
            {
                pathPoints[i] = pathPointsRoot.GetChild(i);
            }
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
            activeTravelDuration = 0f;
            capturedEntrySpeed = 0f;
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

        private void OnValidate()
        {
            internalTravelSpeed = Mathf.Max(0.01f, internalTravelSpeed);
            exitSpeed = Mathf.Max(0f, exitSpeed);
            RefreshPathPointsFromHierarchy();
        }

        public void CaptureCheckpointState()
        {
            checkpointWasOccupied = state == BombPipeState.Occupied;
        }

        public void RestoreCheckpointState()
        {
            if (capturedBomb != null)
            {
                capturedBomb.Completed -= HandleCapturedBombCompleted;
                capturedBomb.Disarm();
            }
            ClearOccupiedState();

            if (checkpointWasOccupied)
            {
                Debug.LogWarning(
                    $"Checkpoint captured '{name}' while occupied. " +
                    "The transient bomb is restored as an empty pipe.",
                    this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            RefreshPathPointsFromHierarchy();
            if (entrance == null || exit == null)
            {
                return;
            }

            Gizmos.color = state == BombPipeState.Occupied
                ? Color.yellow
                : Color.cyan;
            Vector3 previous = entrance.position;
            Gizmos.DrawWireSphere(previous, 0.15f);
            foreach (Transform point in pathPoints)
            {
                if (point == null)
                {
                    continue;
                }
                Gizmos.DrawLine(previous, point.position);
                Gizmos.DrawWireSphere(point.position, 0.1f);
                previous = point.position;
            }
            Gizmos.DrawLine(previous, exit.position);
            Gizmos.DrawWireSphere(exit.position, 0.15f);
            Gizmos.DrawRay(entrance.position, entrance.forward * 1.5f);
            Gizmos.DrawRay(exit.position, exit.forward * 1.5f);
        }
    }
}
