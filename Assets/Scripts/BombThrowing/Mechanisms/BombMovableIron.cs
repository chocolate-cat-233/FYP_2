using System.Collections;
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

        public BombMovableIronState State => state;

        private void Awake()
        {
            ironCollider = GetComponent<Collider>();
            if (ironCollider == null)
            {
                ironCollider = GetComponentInChildren<Collider>();
            }
            ironBody = GetComponent<Rigidbody>();
            if (ironBody != null)
            {
                initialIsKinematic = ironBody.isKinematic;
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
            float allowedDistance = GetAllowedDistance(direction);
            Vector3 destination = start + direction * allowedDistance;
            // Snap to the world-aligned integer grid on the xz plane so the
            // iron block always settles on aligned coordinates.
            destination.x = Mathf.Round(destination.x);
            destination.z = Mathf.Round(destination.z);

            if (ironBody != null)
            {
                ironBody.linearVelocity = Vector3.zero;
                ironBody.angularVelocity = Vector3.zero;
                ironBody.isKinematic = true;
            }

            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);
                float curvedT = movementCurve != null
                    ? movementCurve.Evaluate(t)
                    : t;
                transform.position = Vector3.LerpUnclamped(
                    start,
                    destination,
                    curvedT);
                yield return null;
            }

            transform.position = destination;
            Physics.SyncTransforms();
            if (ironBody != null)
            {
                ironBody.isKinematic = initialIsKinematic;
            }

            state = BombMovableIronState.Idle;
            onMoveFinished?.Invoke();
        }

        private float GetAllowedDistance(Vector3 direction)
        {
            if (ironCollider == null || blockingLayers.value == 0)
            {
                return moveDistance;
            }

            Bounds bounds = ironCollider.bounds;
            RaycastHit[] hits = Physics.BoxCastAll(
                bounds.center,
                bounds.extents * 0.95f,
                direction,
                transform.rotation,
                moveDistance + collisionSkin,
                blockingLayers,
                QueryTriggerInteraction.Ignore);

            float allowed = moveDistance;
            foreach (RaycastHit obstacle in hits)
            {
                if (obstacle.collider == null ||
                    obstacle.collider.transform == transform ||
                    obstacle.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                allowed = Mathf.Min(
                    allowed,
                    Mathf.Max(0f, obstacle.distance - collisionSkin));
            }

            return allowed;
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
