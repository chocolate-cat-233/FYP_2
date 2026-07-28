using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.Explosion
{
    [DisallowMultipleComponent]
    public sealed class RadialShockwaveExplosion : MonoBehaviour
    {
        [Header("Shockwave")]
        [Min(0.01f)]
        [SerializeField] private float explosionRadius = 8f;
        [Min(0.01f)]
        [SerializeField] private float shockwaveSpeed = 12f;
        [Min(0f)]
        [SerializeField] private float baseStrength = 10f;
        [Tooltip("Use horizontal distance and horizontal knockback direction.")]
        [SerializeField] private bool horizontalOnly = true;
        [Min(0f)]
        [Tooltip("Zero disables the height restriction.")]
        [SerializeField] private float maxHeightDifference = 2f;
        [SerializeField] private LayerMask reactiveLayers = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Collide;

        [Header("Optional obstruction")]
        [SerializeField] private bool requireLineOfSight;
        [SerializeField] private LayerMask obstructionLayers;

        [Header("Optional visuals")]
        [Tooltip("A ring/disc whose X and Z diameter will follow the shockwave.")]
        [SerializeField] private Transform expandingVisual;
        [SerializeField] private bool hideVisualWhenFinished = true;

        [Header("Debug ring visual")]
        [Tooltip("Creates a visible expanding LineRenderer ring at runtime.")]
        [SerializeField] private bool showDebugRing = true;
        [Min(0.01f)]
        [SerializeField] private float debugRingWidth = 0.15f;
        [Range(16, 256)]
        [SerializeField] private int debugRingSegments = 96;
        [SerializeField] private float debugRingHeightOffset = 0.05f;
        [SerializeField] private Color debugRingColor =
            new Color(1f, 0.3f, 0.05f, 0.9f);

        [Header("Testing")]
        [Tooltip("Convenient for scene testing until the real trigger is implemented.")]
        [SerializeField] private bool triggerOnStart;

        [Header("Events")]
        [SerializeField] private UnityEvent onExplosionStarted;
        [SerializeField] private UnityEvent onExplosionFinished;

        private readonly HashSet<IShockwaveReactive> hitReactives =
            new HashSet<IShockwaveReactive>();
        private Collider[] overlapBuffer = new Collider[64];
        private Coroutine explosionRoutine;
        private LineRenderer debugRing;
        private Material debugRingMaterial;

        public bool IsExploding => explosionRoutine != null;
        public float CurrentRadius { get; private set; }
        public float ExplosionRadius => explosionRadius;

        private void Start()
        {
            CreateDebugRing();
            SetVisualActive(false);
            if (triggerOnStart)
            {
                TriggerExplosion();
            }
        }

        [ContextMenu("Trigger Explosion")]
        public void TriggerExplosion()
        {
            if (!isActiveAndEnabled || IsExploding)
            {
                return;
            }

            explosionRoutine = StartCoroutine(ExplosionRoutine());
        }

        private IEnumerator ExplosionRoutine()
        {
            hitReactives.Clear();
            CurrentRadius = 0f;
            SetVisualActive(true);
            SetDebugRingActive(true);
            UpdateVisual();
            UpdateDebugRing();
            onExplosionStarted?.Invoke();

            while (CurrentRadius < explosionRadius)
            {
                float previousRadius = CurrentRadius;
                CurrentRadius = Mathf.Min(
                    explosionRadius,
                    CurrentRadius + shockwaveSpeed * Time.deltaTime);

                DetectNewHits(previousRadius, CurrentRadius);
                UpdateVisual();
                UpdateDebugRing();
                yield return null;
            }

            onExplosionFinished?.Invoke();
            if (hideVisualWhenFinished)
            {
                SetVisualActive(false);
                SetDebugRingActive(false);
            }

            explosionRoutine = null;
        }

        private void DetectNewHits(float previousRadius, float currentRadius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                currentRadius,
                overlapBuffer,
                reactiveLayers,
                triggerInteraction);

            if (count == overlapBuffer.Length)
            {
                overlapBuffer = new Collider[overlapBuffer.Length * 2];
                count = Physics.OverlapSphereNonAlloc(
                    transform.position,
                    currentRadius,
                    overlapBuffer,
                    reactiveLayers,
                    triggerInteraction);
            }

            for (int i = 0; i < count; i++)
            {
                Collider targetCollider = overlapBuffer[i];
                if (targetCollider == null)
                {
                    continue;
                }

                Vector3 closestPoint = GetClosestPoint(
                    targetCollider,
                    transform.position);
                Vector3 offset = closestPoint - transform.position;

                if (maxHeightDifference > 0f &&
                    Mathf.Abs(offset.y) > maxHeightDifference)
                {
                    continue;
                }

                if (horizontalOnly)
                {
                    offset.y = 0f;
                }

                float distance = offset.magnitude;
                if (distance <= previousRadius || distance > currentRadius)
                {
                    continue;
                }

                IShockwaveReactive reactive = FindReactive(targetCollider);
                if (reactive == null || !hitReactives.Add(reactive))
                {
                    continue;
                }

                if (requireLineOfSight &&
                    Physics.Linecast(
                        transform.position,
                        closestPoint,
                        obstructionLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Vector3 direction = distance > 0.001f
                    ? offset / distance
                    : transform.forward;
                float normalizedDistance = Mathf.Clamp01(distance / explosionRadius);
                float strength = baseStrength * (1f - normalizedDistance);

                reactive.OnShockwaveHit(new ShockwaveHit(
                    transform.position,
                    closestPoint,
                    direction,
                    distance,
                    normalizedDistance,
                    strength));
            }
        }

        private static IShockwaveReactive FindReactive(Collider targetCollider)
        {
            MonoBehaviour[] behaviours =
                targetCollider.GetComponentsInParent<MonoBehaviour>(true);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IShockwaveReactive reactive)
                {
                    return reactive;
                }
            }

            return null;
        }

        private static Vector3 GetClosestPoint(
            Collider targetCollider,
            Vector3 origin)
        {
            if (targetCollider is BoxCollider ||
                targetCollider is SphereCollider ||
                targetCollider is CapsuleCollider ||
                targetCollider is MeshCollider meshCollider &&
                meshCollider.convex)
            {
                return targetCollider.ClosestPoint(origin);
            }

            // Unity's Collider.ClosestPoint does not support non-convex mesh,
            // terrain, or arbitrary custom colliders.
            return targetCollider.bounds.ClosestPoint(origin);
        }

        private void UpdateVisual()
        {
            if (expandingVisual == null)
            {
                return;
            }

            float diameter = CurrentRadius * 2f;
            Vector3 scale = expandingVisual.localScale;
            expandingVisual.localScale = new Vector3(diameter, scale.y, diameter);
        }

        private void SetVisualActive(bool value)
        {
            if (expandingVisual != null)
            {
                expandingVisual.gameObject.SetActive(value);
            }
        }

        private void CreateDebugRing()
        {
            if (!showDebugRing || debugRing != null)
            {
                return;
            }

            GameObject ringObject = new GameObject("Shockwave Debug Ring");
            ringObject.transform.SetParent(transform, false);

            debugRing = ringObject.AddComponent<LineRenderer>();
            debugRing.useWorldSpace = false;
            debugRing.loop = true;
            debugRing.positionCount = debugRingSegments;
            debugRing.widthMultiplier = debugRingWidth;
            debugRing.numCornerVertices = 2;
            debugRing.numCapVertices = 2;
            debugRing.startColor = debugRingColor;
            debugRing.endColor = debugRingColor;
            debugRing.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            debugRing.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                debugRingMaterial = new Material(shader)
                {
                    name = "Runtime Shockwave Debug Material",
                    color = debugRingColor
                };
                debugRing.material = debugRingMaterial;
            }

            SetDebugRingActive(false);
        }

        private void UpdateDebugRing()
        {
            if (debugRing == null)
            {
                return;
            }

            if (debugRing.positionCount != debugRingSegments)
            {
                debugRing.positionCount = debugRingSegments;
            }

            debugRing.widthMultiplier = debugRingWidth;
            debugRing.startColor = debugRingColor;
            debugRing.endColor = debugRingColor;

            for (int i = 0; i < debugRingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / debugRingSegments;
                debugRing.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * CurrentRadius,
                    debugRingHeightOffset,
                    Mathf.Sin(angle) * CurrentRadius));
            }
        }

        private void SetDebugRingActive(bool value)
        {
            if (debugRing != null)
            {
                debugRing.gameObject.SetActive(value && showDebugRing);
            }
        }

        private void OnDestroy()
        {
            if (debugRingMaterial != null)
            {
                Destroy(debugRingMaterial);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
