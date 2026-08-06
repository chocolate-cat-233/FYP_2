using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum BombMovableIronState
    {
        Idle,
        Moving
    }

    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class BombMovableIron : MonoBehaviour,
        IBombDirectHitReceiver,
        ICheckpointStateParticipant
    {
        private const int CastBufferSize = 32;
        private static readonly HashSet<BombMovableIron> ActiveIrons =
            new HashSet<BombMovableIron>();

        [Header("Movement")]
        [Min(0f)]
        [SerializeField] private float moveDistance = 1f;
        [Min(0.01f)]
        [SerializeField] private float moveDuration = 0.3f;
        [SerializeField] private bool constrainToWorldAxes = true;
        [SerializeField] private AnimationCurve movementCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Obstruction")]
        [Tooltip("Scene geometry that stops horizontal iron movement. Other " +
                 "BombMovableIron objects always block, regardless of layer.")]
        [SerializeField] private LayerMask blockingLayers;
        [Min(0f)]
        [SerializeField] private float collisionSkin = 0.02f;
        [Min(0f)]
        [Tooltip("Hard minimum center-to-center distance between iron blocks " +
                 "in the movement direction. Use 1 for unit-sized blocks.")]
        [SerializeField] private float minimumIronCenterDistance = 1f;

        [Header("Events")]
        [SerializeField] private UnityEvent onMoveStarted;
        [SerializeField] private UnityEvent onMoveFinished;

        [SerializeField, HideInInspector]
        private BombMovableIronState state;

        private readonly RaycastHit[] castHits =
            new RaycastHit[CastBufferSize];
        private Rigidbody ironBody;
        private Collider ironCollider;
        private Collider[] ironColliders;
        private bool initialIsKinematic;
        private CollisionDetectionMode initialCollisionDetection;
        private RigidbodyInterpolation initialInterpolation;
        private RigidbodyConstraints idleConstraints;
        private Vector3 moveStart;
        private Vector3 moveDestination;
        private Vector3 moveDirection;
        private float moveElapsed;
        private bool finishOnNextFixedUpdate;
        private Vector3 savedCheckpointPosition;
        private Quaternion savedCheckpointRotation;

        public BombMovableIronState State => state;

        private void Awake()
        {
            ironBody = GetComponent<Rigidbody>();
            ironCollider = GetComponent<Collider>();
            if (ironCollider == null)
            {
                ironCollider = GetComponentInChildren<Collider>();
            }
            ironColliders = GetComponentsInChildren<Collider>(true);

            initialIsKinematic = ironBody.isKinematic;
            initialCollisionDetection = ironBody.collisionDetectionMode;
            initialInterpolation = ironBody.interpolation;
            idleConstraints =
                ironBody.constraints |
                RigidbodyConstraints.FreezePositionX |
                RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotation;
            ironBody.constraints = idleConstraints;
        }

        private void OnEnable()
        {
            foreach (BombMovableIron other in ActiveIrons)
            {
                SetIronCollisionIgnored(other, true);
            }

            ActiveIrons.Add(this);
        }

        private void SetIronCollisionIgnored(
            BombMovableIron other,
            bool ignored)
        {
            if (other == null || ironColliders == null ||
                other.ironColliders == null)
            {
                return;
            }

            foreach (Collider own in ironColliders)
            {
                if (own == null)
                {
                    continue;
                }

                foreach (Collider target in other.ironColliders)
                {
                    if (target != null)
                    {
                        Physics.IgnoreCollision(own, target, ignored);
                    }
                }
            }
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (state == BombMovableIronState.Idle)
            {
                Vector3 direction = -hit.Normal;
                direction.y = 0f;
                if (constrainToWorldAxes)
                {
                    direction = GetDominantWorldAxis(direction);
                }

                if (direction.sqrMagnitude > 0.001f)
                {
                    BeginMove(direction.normalized);
                }
            }

            // Bombs still explode on a moving iron, but cannot stack another
            // movement while the current movement is active.
            return BombHitResult.Explode;
        }

        private void BeginMove(Vector3 direction)
        {
            // Re-apply at the moment of use as well, so Enter Play Mode options
            // or a script hot reload cannot leave an iron pair registered but
            // physically colliding.
            BombMovableIron[] irons = FindObjectsByType<BombMovableIron>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (BombMovableIron other in irons)
            {
                if (other != null && other != this)
                {
                    SetIronCollisionIgnored(other, true);
                }
            }

            state = BombMovableIronState.Moving;
            moveStart = ironBody.position;
            moveDirection = direction;
            moveElapsed = 0f;
            finishOnNextFixedUpdate = false;

            // Snap the resting spot to the world-aligned integer grid on the
            // xz plane so the iron block settles on aligned coordinates.
            moveDestination = moveStart + moveDirection * moveDistance;
            moveDestination.x = Mathf.Round(moveDestination.x);
            moveDestination.z = Mathf.Round(moveDestination.z);

            if (ironBody != null && !ironBody.isKinematic)
            {
                ironBody.linearVelocity = Vector3.zero;
                ironBody.angularVelocity = Vector3.zero;
            }

            ironBody.constraints =
                idleConstraints &
                ~RigidbodyConstraints.FreezePositionX &
                ~RigidbodyConstraints.FreezePositionZ;
            ironBody.isKinematic = true;
            ironBody.interpolation = RigidbodyInterpolation.None;
            ironBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            onMoveStarted?.Invoke();
        }

        private void FixedUpdate()
        {
            if (state != BombMovableIronState.Moving)
            {
                return;
            }

            // Let the previous MovePosition be applied by one physics step
            // before restoring the body to its normal gravity-driven state.
            if (finishOnNextFixedUpdate)
            {
                EnforceMinimumIronDistance();
                FinishMove();
                return;
            }

            moveElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(moveElapsed / moveDuration);
            float curvedT = movementCurve != null
                ? Mathf.Clamp01(movementCurve.Evaluate(t))
                : t;
            Vector3 desiredPosition = Vector3.LerpUnclamped(
                moveStart,
                moveDestination,
                curvedT);
            Vector3 currentPosition = ironBody.position;
            Vector3 displacement = desiredPosition - currentPosition;
            float requestedDistance = displacement.magnitude;

            if (requestedDistance > 0.0001f)
            {
                Vector3 stepDirection =
                    displacement / requestedDistance;
                float allowedDistance = GetAllowedStepDistance(
                    stepDirection,
                    requestedDistance,
                    out bool blocked);
                Vector3 nextPosition =
                    currentPosition + stepDirection * allowedDistance;
                nextPosition = ClampToMinimumIronDistance(
                    nextPosition,
                    out bool minimumDistanceBlocked);
                ironBody.MovePosition(nextPosition);

                if (blocked || minimumDistanceBlocked)
                {
                    finishOnNextFixedUpdate = true;
                    return;
                }
            }

            if (t >= 1f)
            {
                finishOnNextFixedUpdate = true;
            }
        }

        private Vector3 ClampToMinimumIronDistance(
            Vector3 candidatePosition,
            out bool blocked)
        {
            blocked = false;
            if (minimumIronCenterDistance <= 0f)
            {
                return candidatePosition;
            }

            BombMovableIron[] irons = FindObjectsByType<BombMovableIron>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Vector3 sideAxis = new Vector3(
                -moveDirection.z,
                0f,
                moveDirection.x).normalized;

            foreach (BombMovableIron other in irons)
            {
                if (!IsIronInMovementLane(other, sideAxis))
                {
                    continue;
                }

                Vector3 otherPosition = other.ironBody != null
                    ? other.ironBody.position
                    : other.transform.position;
                // Only an iron located in front of A's movement origin can
                // block this move.
                if (Vector3.Dot(
                        otherPosition - moveStart,
                        moveDirection) <= 0f)
                {
                    continue;
                }

                float candidateDistance = Vector3.Dot(
                    otherPosition - candidatePosition,
                    moveDirection);
                if (candidateDistance >= minimumIronCenterDistance)
                {
                    continue;
                }

                candidatePosition -= moveDirection *
                    (minimumIronCenterDistance - candidateDistance);
                blocked = true;
            }

            return candidatePosition;
        }

        private bool IsIronInMovementLane(
            BombMovableIron other,
            Vector3 sideAxis)
        {
            if (other == null || other == this ||
                other.ironCollider == null ||
                !other.ironCollider.enabled)
            {
                return false;
            }

            Bounds ownBounds = GetPhysicsBounds();
            Bounds otherBounds = other.GetPhysicsBounds();
            Vector3 delta = otherBounds.center - ownBounds.center;
            float sideDistance =
                Mathf.Abs(Vector3.Dot(delta, sideAxis));
            float sideRadius =
                ProjectRadius(ownBounds.extents, sideAxis) +
                ProjectRadius(otherBounds.extents, sideAxis);
            float verticalDistance = Mathf.Abs(delta.y);
            float verticalRadius =
                ownBounds.extents.y + otherBounds.extents.y;
            return sideDistance < sideRadius &&
                   verticalDistance < verticalRadius;
        }

        private void EnforceMinimumIronDistance()
        {
            Vector3 correctedPosition = ClampToMinimumIronDistance(
                ironBody.position,
                out bool corrected);
            if (!corrected)
            {
                return;
            }

            // This is the final safety rule: only A is corrected. B is never
            // moved, assigned velocity, or switched to another body state.
            ironBody.position = correctedPosition;
            Physics.SyncTransforms();
        }

        private float GetAllowedStepDistance(
            Vector3 direction,
            float requestedDistance,
            out bool blocked)
        {
            blocked = false;
            if (ironCollider == null)
            {
                return requestedDistance;
            }

            float allowedDistance = GetIronSurfaceDistance(
                direction,
                requestedDistance,
                out bool ironBlocked);
            blocked = ironBlocked;

            GetCastShape(
                out Vector3 center,
                out Vector3 halfExtents,
                out Quaternion orientation);
            int hitCount = Physics.BoxCastNonAlloc(
                center,
                halfExtents,
                direction,
                castHits,
                orientation,
                requestedDistance + collisionSkin,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = castHits[i];
                if (!IsBlockingCollider(hit.collider))
                {
                    continue;
                }

                BombMovableIron otherIron =
                    hit.collider.GetComponentInParent<BombMovableIron>();
                if (otherIron == null)
                {
                    // Ignore the floor already touching the bottom of the
                    // block. A wall must overlap the block vertically and face
                    // against its horizontal movement.
                    Bounds ownBounds = ironCollider.bounds;
                    Bounds obstacleBounds = hit.collider.bounds;
                    bool overlapsVertically =
                        obstacleBounds.max.y >
                            ownBounds.min.y + collisionSkin &&
                        obstacleBounds.min.y <
                            ownBounds.max.y - collisionSkin;
                    bool facesMovement =
                        Vector3.Dot(hit.normal, direction) < -0.05f;
                    if (!overlapsVertically || !facesMovement)
                    {
                        continue;
                    }
                }

                float safeDistance = Mathf.Max(
                    0f,
                    hit.distance - collisionSkin);
                if (safeDistance < allowedDistance)
                {
                    allowedDistance = safeDistance;
                    blocked = true;
                }
            }

            return allowedDistance;
        }

        private float GetIronSurfaceDistance(
            Vector3 direction,
            float requestedDistance,
            out bool blocked)
        {
            blocked = false;
            Bounds ownBounds = GetPhysicsBounds();
            Vector3 sideAxis =
                new Vector3(-direction.z, 0f, direction.x).normalized;
            float allowed = requestedDistance;

            BombMovableIron[] irons = FindObjectsByType<BombMovableIron>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (BombMovableIron other in irons)
            {
                if (other == null || other == this ||
                    other.ironCollider == null ||
                    !other.ironCollider.enabled)
                {
                    continue;
                }

                Bounds otherBounds = other.GetPhysicsBounds();
                Vector3 delta = otherBounds.center - ownBounds.center;
                float forwardDistance = Vector3.Dot(delta, direction);
                if (forwardDistance <= 0f)
                {
                    continue;
                }

                float sideDistance =
                    Mathf.Abs(Vector3.Dot(delta, sideAxis));
                float sideRadius =
                    ProjectRadius(ownBounds.extents, sideAxis) +
                    ProjectRadius(otherBounds.extents, sideAxis);
                float verticalDistance = Mathf.Abs(delta.y);
                float verticalRadius =
                    ownBounds.extents.y + otherBounds.extents.y;
                if (sideDistance >= sideRadius ||
                    verticalDistance >= verticalRadius)
                {
                    continue;
                }

                float surfaceDistance = forwardDistance -
                    ProjectRadius(ownBounds.extents, direction) -
                    ProjectRadius(otherBounds.extents, direction) -
                    collisionSkin;
                float safeDistance = Mathf.Max(0f, surfaceDistance);
                if (safeDistance < allowed)
                {
                    allowed = safeDistance;
                    blocked = true;
                }
            }

            return allowed;
        }

        private Bounds GetPhysicsBounds()
        {
            Bounds bounds = ironCollider.bounds;
            if (ironBody != null)
            {
                // With Rigidbody interpolation, Transform and Collider.bounds
                // can visually lag behind the current physics pose. Move the
                // bounds center onto Rigidbody.position so distance checks and
                // MovePosition use the same fixed-step coordinate space.
                bounds.center += ironBody.position - transform.position;
            }

            return bounds;
        }

        private static float ProjectRadius(
            Vector3 extents,
            Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                   Mathf.Abs(axis.y) * extents.y +
                   Mathf.Abs(axis.z) * extents.z;
        }

        private void GetCastShape(
            out Vector3 center,
            out Vector3 halfExtents,
            out Quaternion orientation)
        {
            Bounds bounds = ironCollider.bounds;
            center = bounds.center;
            halfExtents = bounds.extents;
            orientation = Quaternion.identity;

            if (ironCollider is not BoxCollider box)
            {
                if (ironBody != null)
                {
                    center += ironBody.position - transform.position;
                }
                return;
            }

            Vector3 scale = box.transform.lossyScale;
            center = box.transform.TransformPoint(box.center);
            if (ironBody != null)
            {
                center += ironBody.position - transform.position;
            }
            halfExtents = Vector3.Scale(
                box.size * 0.5f,
                new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)));
            orientation = box.transform.rotation;
        }

        private bool IsBlockingCollider(Collider other)
        {
            if (other == null || other.isTrigger ||
                other == ironCollider ||
                other.transform == transform ||
                other.transform.IsChildOf(transform))
            {
                return false;
            }

            BombMovableIron otherIron =
                other.GetComponentInParent<BombMovableIron>();
            if (otherIron != null)
            {
                return otherIron != this;
            }

            return (blockingLayers.value &
                    (1 << other.gameObject.layer)) != 0;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (state == BombMovableIronState.Moving &&
                IsBlockingCollider(collision.collider))
            {
                finishOnNextFixedUpdate = true;
            }
        }

        private void FinishMove()
        {
            if (state != BombMovableIronState.Moving)
            {
                return;
            }

            ironBody.constraints = idleConstraints;
            ironBody.isKinematic = initialIsKinematic;
            ironBody.collisionDetectionMode = initialCollisionDetection;
            ironBody.interpolation = initialInterpolation;
            if (!ironBody.isKinematic)
            {
                ironBody.linearVelocity = Vector3.zero;
                ironBody.angularVelocity = Vector3.zero;
            }
            state = BombMovableIronState.Idle;
            finishOnNextFixedUpdate = false;
            onMoveFinished?.Invoke();
        }

        public void CaptureCheckpointState()
        {
            savedCheckpointPosition = ironBody.position;
            savedCheckpointRotation = ironBody.rotation;
        }

        public void RestoreCheckpointState()
        {
            ironBody.constraints = idleConstraints;
            ironBody.isKinematic = initialIsKinematic;
            ironBody.collisionDetectionMode = initialCollisionDetection;
            ironBody.interpolation = initialInterpolation;
            ironBody.position = savedCheckpointPosition;
            ironBody.rotation = savedCheckpointRotation;
            if (!ironBody.isKinematic)
            {
                ironBody.linearVelocity = Vector3.zero;
                ironBody.angularVelocity = Vector3.zero;
            }
            state = BombMovableIronState.Idle;
            finishOnNextFixedUpdate = false;
            Physics.SyncTransforms();
        }

        private void OnDisable()
        {
            ActiveIrons.Remove(this);
            foreach (BombMovableIron other in ActiveIrons)
            {
                SetIronCollisionIgnored(other, false);
            }

            if (ironBody != null &&
                state == BombMovableIronState.Moving)
            {
                ironBody.constraints = idleConstraints;
                ironBody.isKinematic = initialIsKinematic;
                ironBody.collisionDetectionMode =
                    initialCollisionDetection;
                ironBody.interpolation = initialInterpolation;
            }

            if (ironBody != null)
            {
                ironBody.constraints = idleConstraints;
            }

            state = BombMovableIronState.Idle;
            finishOnNextFixedUpdate = false;
        }

        private static Vector3 GetDominantWorldAxis(Vector3 direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z))
            {
                return new Vector3(Mathf.Sign(direction.x), 0f, 0f);
            }

            return new Vector3(0f, 0f, Mathf.Sign(direction.z));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.15f);
        }
    }
}
