using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum BombMovableIronState
    {
        Idle,
        Moving
    }

    [DisallowMultipleComponent]
    public sealed class BombMovableIron : MonoBehaviour,
        IBombDirectHitReceiver
    {
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
        [SerializeField] private LayerMask blockingLayers;
        [Min(0f)]
        [SerializeField] private float collisionSkin = 0.02f;

        [Header("Events")]
        [SerializeField] private UnityEvent onMoveStarted;
        [SerializeField] private UnityEvent onMoveFinished;

        [SerializeField, HideInInspector]
        private BombMovableIronState state;
        private Collider ironCollider;
        private Rigidbody ironBody;
        private bool initialIsKinematic;
        private Collider[] ironColliders;
        private BombMovableIron[] movementObstacles;

        public BombMovableIronState State => state;

        private void Awake()
        {
            ironCollider = GetComponent<Collider>();
            if (ironCollider == null)
            {
                ironCollider = GetComponentInChildren<Collider>();
            }
            ironBody = GetComponent<Rigidbody>();
            ironColliders = GetComponentsInChildren<Collider>(true);
            if (ironBody != null)
            {
                initialIsKinematic = ironBody.isKinematic;
            }
        }

        private void OnEnable()
        {
            foreach (BombMovableIron other in ActiveIrons)
            {
                SetPairCollisionIgnored(other, true);
            }

            ActiveIrons.Add(this);
        }

        private void OnDisable()
        {
            ActiveIrons.Remove(this);
            foreach (BombMovableIron other in ActiveIrons)
            {
                SetPairCollisionIgnored(other, false);
            }
        }

        private void SetPairCollisionIgnored(
            BombMovableIron other,
            bool ignored)
        {
            if (other == null || ironColliders == null ||
                other.ironColliders == null)
            {
                return;
            }

            foreach (Collider ownCollider in ironColliders)
            {
                if (ownCollider == null)
                {
                    continue;
                }

                foreach (Collider otherCollider in other.ironColliders)
                {
                    if (otherCollider != null)
                    {
                        Physics.IgnoreCollision(
                            ownCollider,
                            otherCollider,
                            ignored);
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
                    StartCoroutine(MoveRoutine(direction.normalized));
                }
            }

            // A hit while moving still detonates the bomb, but does not stack
            // another displacement.
            return BombHitResult.Explode;
        }

        private IEnumerator MoveRoutine(Vector3 direction)
        {
            state = BombMovableIronState.Moving;
            onMoveStarted?.Invoke();

            Vector3 start = transform.position;
            movementObstacles = FindObjectsByType<BombMovableIron>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            float plannedDistance = GetIronSurfaceDistance(
                direction,
                moveDistance);
            Vector3 destination = start + direction * plannedDistance;

            if (ironBody != null)
            {
                ironBody.linearVelocity = Vector3.zero;
                ironBody.angularVelocity = Vector3.zero;
                ironBody.isKinematic = true;
            }

            float elapsed = 0f;
            bool stoppedByObstacle = false;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);
                float curvedT = movementCurve != null
                    ? movementCurve.Evaluate(t)
                    : t;
                Vector3 nextPosition = Vector3.LerpUnclamped(
                    start,
                    destination,
                    curvedT);

                Vector3 frameDisplacement = nextPosition - transform.position;
                float frameDistance = frameDisplacement.magnitude;
                if (frameDistance > 0.0001f)
                {
                    Vector3 frameDirection = frameDisplacement / frameDistance;
                    float safeFrameDistance = GetAllowedDistance(
                        frameDirection,
                        frameDistance);
                    safeFrameDistance = ClampBeforeIronPenetration(
                        frameDirection,
                        safeFrameDistance);
                    transform.position += frameDirection * safeFrameDistance;
                    Physics.SyncTransforms();

                    if (safeFrameDistance + 0.0001f < frameDistance)
                    {
                        stoppedByObstacle = true;
                        break;
                    }
                }
                yield return null;
            }

            if (!stoppedByObstacle)
            {
                transform.position = destination;
            }

            Physics.SyncTransforms();
            if (ironBody != null)
            {
                ironBody.isKinematic = initialIsKinematic;
            }

            state = BombMovableIronState.Idle;
            movementObstacles = null;
            onMoveFinished?.Invoke();
        }

        private float ClampBeforeIronPenetration(
            Vector3 direction,
            float requestedDistance)
        {
            if (requestedDistance <= 0f ||
                !WouldOverlapIron(transform.position +
                                  direction * requestedDistance))
            {
                return requestedDistance;
            }

            float low = 0f;
            float high = requestedDistance;
            for (int i = 0; i < 12; i++)
            {
                float middle = (low + high) * 0.5f;
                Vector3 candidate =
                    transform.position + direction * middle;
                if (WouldOverlapIron(candidate))
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            return Mathf.Max(0f, low - collisionSkin);
        }

        private bool WouldOverlapIron(Vector3 candidateRootPosition)
        {
            if (ironColliders == null)
            {
                return false;
            }

            Vector3 rootOffset = candidateRootPosition - transform.position;
            BombMovableIron[] irons = movementObstacles ??
                FindObjectsByType<BombMovableIron>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            foreach (BombMovableIron other in irons)
            {
                if (other == null || other == this ||
                    other.ironColliders == null)
                {
                    continue;
                }

                foreach (Collider ownCollider in ironColliders)
                {
                    if (ownCollider == null || !ownCollider.enabled ||
                        ownCollider.isTrigger)
                    {
                        continue;
                    }

                    Vector3 ownPosition =
                        ownCollider.transform.position + rootOffset;
                    Quaternion ownRotation = ownCollider.transform.rotation;
                    foreach (Collider otherCollider in other.ironColliders)
                    {
                        if (otherCollider == null ||
                            !otherCollider.enabled ||
                            otherCollider.isTrigger)
                        {
                            continue;
                        }

                        if (Physics.ComputePenetration(
                            ownCollider,
                            ownPosition,
                            ownRotation,
                            otherCollider,
                            otherCollider.transform.position,
                            otherCollider.transform.rotation,
                            out _,
                            out _))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private float GetAllowedDistance(Vector3 direction)
        {
            return GetAllowedDistance(direction, moveDistance);
        }

        private float GetAllowedDistance(
            Vector3 direction,
            float requestedDistance)
        {
            if (ironCollider == null)
            {
                return requestedDistance;
            }

            float allowed = GetIronSurfaceDistance(
                direction,
                requestedDistance);
            Bounds bounds = ironCollider.bounds;
            Vector3 center = bounds.center;
            Vector3 halfExtents = bounds.extents;
            Quaternion orientation = Quaternion.identity;

            if (ironCollider is BoxCollider box)
            {
                Vector3 scale = box.transform.lossyScale;
                center = box.transform.TransformPoint(box.center);
                halfExtents = Vector3.Scale(
                    box.size * 0.5f,
                    new Vector3(
                        Mathf.Abs(scale.x),
                        Mathf.Abs(scale.y),
                        Mathf.Abs(scale.z)));
                orientation = box.transform.rotation;
            }

            RaycastHit[] hits = Physics.BoxCastAll(
                center,
                halfExtents,
                direction,
                orientation,
                requestedDistance + collisionSkin,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit obstacle = hits[i];
                if (obstacle.collider == null ||
                    obstacle.collider.isTrigger ||
                    obstacle.collider.transform == transform ||
                    obstacle.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                BombMovableIron otherIron =
                    obstacle.collider.GetComponentInParent<BombMovableIron>();
                bool isAnotherIron =
                    otherIron != null && otherIron != this;
                bool isConfiguredObstacle =
                    (blockingLayers.value &
                     (1 << obstacle.collider.gameObject.layer)) != 0;

                // A horizontal cast can report the floor already touching the
                // block at distance zero. Only a surface facing against the
                // movement direction is a horizontal obstruction.
                bool facesMovement =
                    Vector3.Dot(obstacle.normal, direction) < -0.05f;

                // Iron blocks are always solid to one another, independently
                // from Blocking Layers. A bomb can move one block, but that
                // block can never push another block as part of the move.
                if ((!isAnotherIron && !isConfiguredObstacle) ||
                    !facesMovement)
                {
                    continue;
                }

                allowed = Mathf.Min(
                    allowed,
                    Mathf.Max(0f, obstacle.distance - collisionSkin));
            }

            return allowed;
        }

        private float GetIronSurfaceDistance(
            Vector3 direction,
            float requestedDistance)
        {
            Bounds ownBounds = ironCollider.bounds;
            Vector3 horizontalPerpendicular =
                new Vector3(-direction.z, 0f, direction.x).normalized;
            float allowed = requestedDistance;

            BombMovableIron[] irons = movementObstacles ??
                FindObjectsByType<BombMovableIron>(
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

                Bounds otherBounds = other.ironCollider.bounds;
                Vector3 centerDelta =
                    otherBounds.center - ownBounds.center;
                float forwardCenterDistance =
                    Vector3.Dot(centerDelta, direction);
                if (forwardCenterDistance <= 0f)
                {
                    continue;
                }

                float ownForwardRadius = ProjectedRadius(
                    ownBounds.extents,
                    direction);
                float otherForwardRadius = ProjectedRadius(
                    otherBounds.extents,
                    direction);
                float sideDistance = Mathf.Abs(Vector3.Dot(
                    centerDelta,
                    horizontalPerpendicular));
                float combinedSideRadius =
                    ProjectedRadius(
                        ownBounds.extents,
                        horizontalPerpendicular) +
                    ProjectedRadius(
                        otherBounds.extents,
                        horizontalPerpendicular);
                float verticalDistance = Mathf.Abs(centerDelta.y);
                float combinedVerticalRadius =
                    ownBounds.extents.y + otherBounds.extents.y;

                // Only blocks located in the same horizontal lane and at an
                // overlapping height can stop this movement.
                if (sideDistance >= combinedSideRadius ||
                    verticalDistance >= combinedVerticalRadius)
                {
                    continue;
                }

                float surfaceGap = forwardCenterDistance -
                    ownForwardRadius - otherForwardRadius;
                allowed = Mathf.Min(
                    allowed,
                    Mathf.Max(0f, surfaceGap - collisionSkin));
            }

            return allowed;
        }

        private static float ProjectedRadius(
            Vector3 extents,
            Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                   Mathf.Abs(axis.y) * extents.y +
                   Mathf.Abs(axis.z) * extents.z;
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
