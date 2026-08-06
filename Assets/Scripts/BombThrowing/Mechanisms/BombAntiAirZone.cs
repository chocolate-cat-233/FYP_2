using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    /// <summary>
    /// Trigger volume that shoots down player-thrown bombs. The bomb remains
    /// alive, loses its throw velocity, falls vertically, and explodes normally
    /// when it reaches a solid surface.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class BombAntiAirZone : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [Header("Interception")]
        [SerializeField] private BombOwnerMask interceptedOwners =
            BombOwnerMask.Player;
        [Min(0f)]
        [Tooltip("Immediate downward speed after interception. Gravity continues afterwards.")]
        [SerializeField] private float dropSpeed = 1f;

        [Header("Map / boundary visual")]
        [Tooltip("World-space boundary. A minimap camera can render this directly.")]
        [SerializeField] private bool showBoundaryWhenPlayerInside = true;
        [SerializeField] private Color boundaryColor =
            new Color(1f, 0.1f, 0.1f, 0.85f);
        [Min(0.01f)]
        [SerializeField] private float boundaryWidth = 0.08f;
        [Range(12, 128)]
        [SerializeField] private int circleSegments = 48;
        [SerializeField] private float boundaryHeightOffset = 0.05f;
        [Tooltip("Optional map UI/world marker. Enabled only while the player is inside.")]
        [SerializeField] private GameObject mapIndicator;

        [Header("Events / map hooks")]
        [SerializeField] private UnityEvent onPlayerEnteredZone;
        [SerializeField] private UnityEvent onPlayerExitedZone;
        [SerializeField] private UnityEvent onBombIntercepted;

        [Header("Debug")]
        [SerializeField] private bool debugLog;

        private readonly HashSet<BombProjectile> interceptedBombs =
            new HashSet<BombProjectile>();
        private Collider zoneCollider;
        private PlayerHealth player;
        private Collider playerCollider;
        private LineRenderer boundary;
        private Material boundaryMaterial;
        private bool playerInside;

        public bool IsPlayerInside => playerInside;
        public Bounds WorldBounds => zoneCollider != null
            ? zoneCollider.bounds
            : new Bounds(transform.position, Vector3.zero);

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            zoneCollider.isTrigger = true;
            FindPlayer();
            CreateBoundary();
            SetBoundaryVisible(false);
            SetMapIndicatorVisible(false);
        }

        private void Update()
        {
            if (player == null || playerCollider == null)
            {
                FindPlayer();
            }

            bool currentlyInside = IsPlayerOverlappingZone();
            if (currentlyInside != playerInside)
            {
                playerInside = currentlyInside;
                SetBoundaryVisible(
                    showBoundaryWhenPlayerInside && playerInside);
                SetMapIndicatorVisible(playerInside);
                if (playerInside)
                {
                    onPlayerEnteredZone?.Invoke();
                }
                else
                {
                    onPlayerExitedZone?.Invoke();
                }
            }

            RemoveDestroyedBombs();
        }

        private void LateUpdate()
        {
            UpdateBoundary();
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            BombProjectile bomb = hit.Projectile;
            if (bomb == null ||
                !interceptedOwners.Contains(hit.OwnerType) ||
                !interceptedBombs.Add(bomb))
            {
                return BombHitResult.ContinueFlight;
            }

            if (bomb.ForceDrop(dropSpeed))
            {
                onBombIntercepted?.Invoke();
                if (debugLog)
                {
                    Debug.Log(
                        $"Anti-air zone '{name}' shot down bomb " +
                        $"'{bomb.name}'.",
                        this);
                }
            }
            return BombHitResult.ContinueFlight;
        }

        private void FindPlayer()
        {
            player = FindFirstObjectByType<PlayerHealth>();
            if (player == null)
            {
                playerCollider = null;
                return;
            }

            playerCollider = player.GetComponent<CapsuleCollider>();
            if (playerCollider == null)
            {
                playerCollider = player.GetComponentInChildren<Collider>();
            }
        }

        private bool IsPlayerOverlappingZone()
        {
            if (zoneCollider == null || playerCollider == null ||
                player == null || player.IsDead)
            {
                return false;
            }

            return Physics.ComputePenetration(
                zoneCollider,
                zoneCollider.transform.position,
                zoneCollider.transform.rotation,
                playerCollider,
                playerCollider.transform.position,
                playerCollider.transform.rotation,
                out _,
                out _);
        }

        private void CreateBoundary()
        {
            boundary = GetComponent<LineRenderer>();
            if (boundary == null)
            {
                boundary = gameObject.AddComponent<LineRenderer>();
            }

            boundary.loop = true;
            boundary.useWorldSpace = true;
            boundary.widthMultiplier = boundaryWidth;
            boundary.startColor = boundaryColor;
            boundary.endColor = boundaryColor;
            boundary.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            boundary.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader != null && boundary.sharedMaterial == null)
            {
                boundaryMaterial = new Material(shader)
                {
                    name = "Runtime Anti-Air Boundary Material",
                    color = boundaryColor
                };
                boundary.material = boundaryMaterial;
            }
            UpdateBoundary();
        }

        private void UpdateBoundary()
        {
            if (boundary == null || zoneCollider == null)
            {
                return;
            }

            Bounds bounds = zoneCollider.bounds;
            float y = bounds.min.y + boundaryHeightOffset;
            if (zoneCollider is SphereCollider)
            {
                boundary.positionCount = circleSegments;
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                for (int i = 0; i < circleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / circleSegments;
                    boundary.SetPosition(i, new Vector3(
                        bounds.center.x + Mathf.Cos(angle) * radius,
                        y,
                        bounds.center.z + Mathf.Sin(angle) * radius));
                }
                return;
            }

            boundary.positionCount = 4;
            boundary.SetPosition(0, new Vector3(bounds.min.x, y, bounds.min.z));
            boundary.SetPosition(1, new Vector3(bounds.max.x, y, bounds.min.z));
            boundary.SetPosition(2, new Vector3(bounds.max.x, y, bounds.max.z));
            boundary.SetPosition(3, new Vector3(bounds.min.x, y, bounds.max.z));
        }

        private void SetBoundaryVisible(bool value)
        {
            if (boundary != null)
            {
                boundary.enabled = value;
            }
        }

        private void SetMapIndicatorVisible(bool value)
        {
            if (mapIndicator != null &&
                mapIndicator != gameObject &&
                mapIndicator.activeSelf != value)
            {
                mapIndicator.SetActive(value);
            }
        }

        private void RemoveDestroyedBombs()
        {
            interceptedBombs.RemoveWhere(bomb => bomb == null);
        }

        private void OnValidate()
        {
            dropSpeed = Mathf.Max(0f, dropSpeed);
            boundaryWidth = Mathf.Max(0.01f, boundaryWidth);
            circleSegments = Mathf.Clamp(circleSegments, 12, 128);
            Collider target = GetComponent<Collider>();
            if (target != null)
            {
                target.isTrigger = true;
            }
        }

        private void OnDestroy()
        {
            if (boundaryMaterial != null)
            {
                Destroy(boundaryMaterial);
            }
        }

        private void OnDrawGizmos()
        {
            Collider target = GetComponent<Collider>();
            if (target == null)
            {
                return;
            }

            Gizmos.color = boundaryColor;
            if (target is SphereCollider sphere)
            {
                float radius = sphere.radius * Mathf.Max(
                    Mathf.Abs(transform.lossyScale.x),
                    Mathf.Abs(transform.lossyScale.z));
                Gizmos.DrawWireSphere(target.bounds.center, radius);
            }
            else
            {
                Gizmos.DrawWireCube(target.bounds.center, target.bounds.size);
            }
        }
    }
}
