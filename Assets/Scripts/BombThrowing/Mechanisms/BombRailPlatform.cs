using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum RailPlatformRouteMode
    {
        PingPong,
        Loop,
        Once
    }

    public enum RailPlatformState
    {
        Stopped,
        Moving,
        Waiting,
        Completed
    }

    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class BombRailPlatform : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [Header("Route")]
        [Tooltip("World-space route points. Do not parent these points to the " +
                 "moving platform, otherwise the route moves with it.")]
        [SerializeField] private Transform[] waypoints;
        [SerializeField] private RailPlatformRouteMode routeMode =
            RailPlatformRouteMode.PingPong;
        [Min(0.01f)]
        [SerializeField] private float moveSpeed = 2f;
        [Min(0f)]
        [SerializeField] private float waitAtWaypoint = 0f;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private bool snapToFirstWaypoint = true;

        [Header("Bomb interaction")]
        [Tooltip("Owner types whose bombs can rest on and be transported by " +
                 "this platform without exploding from the collision.")]
        [SerializeField] private BombOwnerMask transportedBombOwners =
            BombOwnerMask.All;

        [Header("Events")]
        [SerializeField] private UnityEvent onRouteStarted;
        [SerializeField] private UnityEvent onWaypointReached;
        [SerializeField] private UnityEvent onRouteCompleted;

        [SerializeField, HideInInspector]
        private RailPlatformState state = RailPlatformState.Stopped;

        private Rigidbody platformBody;
        private int targetWaypointIndex;
        private int travelDirection = 1;
        private float remainingWaitTime;

        public RailPlatformState State => state;
        public int TargetWaypointIndex => targetWaypointIndex;

        private void Reset()
        {
            ConfigureBody();
        }

        private void Awake()
        {
            ConfigureBody();
            InitialiseRoute();
        }

        private void Start()
        {
            if (playOnAwake)
            {
                Play();
            }
        }

        private void ConfigureBody()
        {
            if (platformBody == null)
            {
                platformBody = GetComponent<Rigidbody>();
            }

            if (platformBody == null)
            {
                return;
            }

            platformBody.useGravity = false;
            platformBody.isKinematic = true;
            platformBody.interpolation = RigidbodyInterpolation.Interpolate;
            platformBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
        }

        private void InitialiseRoute()
        {
            targetWaypointIndex = 0;
            travelDirection = 1;
            remainingWaitTime = 0f;
            state = RailPlatformState.Stopped;

            if (snapToFirstWaypoint && HasUsableRoute())
            {
                platformBody.position = waypoints[0].position;
                targetWaypointIndex = waypoints.Length > 1 ? 1 : 0;
            }
        }

        private void FixedUpdate()
        {
            if (state == RailPlatformState.Waiting)
            {
                remainingWaitTime -= Time.fixedDeltaTime;
                if (remainingWaitTime <= 0f)
                {
                    state = RailPlatformState.Moving;
                }
                return;
            }

            if (state != RailPlatformState.Moving || !HasUsableRoute())
            {
                return;
            }

            Transform target = waypoints[targetWaypointIndex];
            if (target == null)
            {
                AdvanceTarget();
                return;
            }

            Vector3 nextPosition = Vector3.MoveTowards(
                platformBody.position,
                target.position,
                moveSpeed * Time.fixedDeltaTime);
            platformBody.MovePosition(nextPosition);

            if ((nextPosition - target.position).sqrMagnitude <= 0.000001f)
            {
                ReachWaypoint();
            }
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (hit.Projectile == null ||
                !transportedBombOwners.Contains(hit.OwnerType))
            {
                return BombHitResult.Ignore;
            }

            // Keep the normal solid collision so the Rigidbody bomb can rest
            // on and inherit motion from the kinematic platform, but suppress
            // BombProjectile's collision-triggered explosion.
            return BombHitResult.ContinueFlight;
        }

        [ContextMenu("Play Route")]
        public void Play()
        {
            if (!HasUsableRoute())
            {
                Debug.LogWarning(
                    $"{nameof(BombRailPlatform)} on '{name}' requires at " +
                    "least two valid waypoints.",
                    this);
                return;
            }

            if (state == RailPlatformState.Completed)
            {
                InitialiseRoute();
            }

            state = RailPlatformState.Moving;
            onRouteStarted?.Invoke();
        }

        [ContextMenu("Stop Route")]
        public void Stop()
        {
            state = RailPlatformState.Stopped;
        }

        public void Toggle()
        {
            if (state == RailPlatformState.Moving ||
                state == RailPlatformState.Waiting)
            {
                Stop();
            }
            else
            {
                Play();
            }
        }

        private void ReachWaypoint()
        {
            onWaypointReached?.Invoke();
            if (!AdvanceTarget())
            {
                return;
            }

            if (waitAtWaypoint > 0f)
            {
                remainingWaitTime = waitAtWaypoint;
                state = RailPlatformState.Waiting;
            }
        }

        private bool AdvanceTarget()
        {
            int count = waypoints != null ? waypoints.Length : 0;
            if (count < 2)
            {
                CompleteRoute();
                return false;
            }

            switch (routeMode)
            {
                case RailPlatformRouteMode.Loop:
                    targetWaypointIndex =
                        (targetWaypointIndex + 1) % count;
                    return true;

                case RailPlatformRouteMode.Once:
                    if (targetWaypointIndex >= count - 1)
                    {
                        CompleteRoute();
                        return false;
                    }
                    targetWaypointIndex++;
                    return true;

                case RailPlatformRouteMode.PingPong:
                    if (targetWaypointIndex >= count - 1)
                    {
                        travelDirection = -1;
                    }
                    else if (targetWaypointIndex <= 0)
                    {
                        travelDirection = 1;
                    }
                    targetWaypointIndex += travelDirection;
                    return true;

                default:
                    return false;
            }
        }

        private void CompleteRoute()
        {
            state = RailPlatformState.Completed;
            onRouteCompleted?.Invoke();
        }

        private bool HasUsableRoute()
        {
            if (waypoints == null || waypoints.Length < 2)
            {
                return false;
            }

            int validCount = 0;
            foreach (Transform waypoint in waypoints)
            {
                if (waypoint != null)
                {
                    validCount++;
                }
            }
            return validCount >= 2;
        }

        private void OnDrawGizmosSelected()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform waypoint = waypoints[i];
                if (waypoint == null)
                {
                    continue;
                }

                Gizmos.DrawWireSphere(waypoint.position, 0.2f);
                if (i + 1 < waypoints.Length &&
                    waypoints[i + 1] != null)
                {
                    Gizmos.DrawLine(
                        waypoint.position,
                        waypoints[i + 1].position);
                }
            }

            if (routeMode == RailPlatformRouteMode.Loop &&
                waypoints.Length > 1 &&
                waypoints[0] != null &&
                waypoints[waypoints.Length - 1] != null)
            {
                Gizmos.DrawLine(
                    waypoints[waypoints.Length - 1].position,
                    waypoints[0].position);
            }
        }
    }
}
