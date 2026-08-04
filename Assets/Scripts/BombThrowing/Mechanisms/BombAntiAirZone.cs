using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class BombAntiAirZone : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [Header("Interception")]
        [SerializeField] private BombOwnerMask interceptedOwners =
            BombOwnerMask.All;

        [Header("Boundary visual")]
        [SerializeField] private bool showRuntimeBoundary = true;
        [SerializeField] private Color boundaryColor =
            new Color(1f, 0.1f, 0.1f, 0.8f);
        [Min(0.01f)]
        [SerializeField] private float boundaryWidth = 0.08f;
        [Range(12, 128)]
        [SerializeField] private int circleSegments = 48;

        [Header("Events")]
        [SerializeField] private UnityEvent onBombIntercepted;

        private Collider zoneCollider;
        private LineRenderer boundary;
        private Material boundaryMaterial;

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            zoneCollider.isTrigger = true;
            if (showRuntimeBoundary)
            {
                CreateBoundary();
            }
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (!interceptedOwners.Contains(hit.OwnerType))
            {
                return BombHitResult.Ignore;
            }

            onBombIntercepted?.Invoke();
            return BombHitResult.Disarm;
        }

        private void CreateBoundary()
        {
            boundary = gameObject.AddComponent<LineRenderer>();
            boundary.loop = true;
            boundary.useWorldSpace = true;
            boundary.widthMultiplier = boundaryWidth;
            boundary.startColor = boundaryColor;
            boundary.endColor = boundaryColor;
            boundary.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            boundary.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
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
            float y = bounds.min.y + 0.05f;
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

        private void LateUpdate()
        {
            if (showRuntimeBoundary)
            {
                UpdateBoundary();
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
            Collider targetCollider = GetComponent<Collider>();
            if (targetCollider == null)
            {
                return;
            }

            Gizmos.color = boundaryColor;
            Gizmos.DrawWireCube(
                targetCollider.bounds.center,
                targetCollider.bounds.size);
        }
    }
}
