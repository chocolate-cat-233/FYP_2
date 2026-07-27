using UnityEngine;

namespace SuperFinalFuse.RevealDisc
{
    /// <summary>
    /// Drives the reveal-disc effect. Attach to the player GameObject.
    /// Broadcasts world-space disc parameters every frame via global
    /// shader properties, which are consumed by RevealDiscRenderFeature
    /// and the decal / stencil shaders.
    ///
    /// Call Activate() / Deactivate() to smoothly expand or shrink the disc.
    /// </summary>
    public sealed class RevealDiscController : MonoBehaviour
    {
        [Header("Disc Shape")]
        [Tooltip("Maximum world-space radius of the reveal disc (units).")]
        [SerializeField, Min(0.1f)]
        private float discRadius = 3f;

        [Tooltip("How many seconds to fully expand or shrink.")]
        [SerializeField, Min(0.01f)]
        private float transitionDuration = 0.4f;

        [Tooltip("Vertical offset above the player's pivot (e.g. hip height).")]
        [SerializeField]
        private float heightOffset = 0.2f;

        [Header("Cosmetic")]
        [Tooltip("Fill color inside the disc.")]
        [SerializeField]
        private Color discFillColor = new Color(1f, 0.9f, 0.5f, 0.15f);

        [Tooltip("Rim / edge glow color.")]
        [SerializeField]
        private Color discRimColor = new Color(1f, 0.85f, 0.2f, 0.85f);

        [Tooltip("Width of the rim relative to the disc radius (0–1).")]
        [SerializeField, Range(0.01f, 0.5f)]
        private float rimWidth = 0.08f;

        [Header("Debug")]
        [Tooltip("Activate the disc immediately on Start (for testing).")]
        [SerializeField]
        private bool activateOnStart;
        [SerializeField]
        private bool showDebugGizmos;

        // ── Shader property IDs (static to avoid per-frame string hashing) ──
        private static readonly int PropActive = Shader.PropertyToID("_RevealDiscActive");
        private static readonly int PropCenter = Shader.PropertyToID("_RevealDiscCenter");
        private static readonly int PropRadius = Shader.PropertyToID("_RevealDiscRadius");
        private static readonly int PropDepth  = Shader.PropertyToID("_RevealDiscDepth");
        private static readonly int PropFill   = Shader.PropertyToID("_RevealDiscFillColor");
        private static readonly int PropRim    = Shader.PropertyToID("_RevealDiscRimColor");
        private static readonly int PropRimW   = Shader.PropertyToID("_RevealDiscRimWidth");

        private static readonly int PropScreenPos   = Shader.PropertyToID("_RevealDiscScreenPos");
        private static readonly int PropScreenRadius = Shader.PropertyToID("_RevealDiscScreenRadius");

        // ── State ─────────────────────────────────────────────────────────
        public bool  IsActive      { get; private set; }
        public float CurrentRadius { get; private set; }
        public float TargetRadius  { get; private set; }
        public float DiscRadius    => discRadius;

        private Camera   mainCamera;
        private Transform mainCameraTransform;

        // ── Unity messages ────────────────────────────────────────────────
        private void Awake()
        {
            mainCamera = Camera.main;
            if (mainCamera != null)
                mainCameraTransform = mainCamera.transform;

            CurrentRadius = 0f;
            TargetRadius  = 0f;
            IsActive      = false;
        }

        private void Start()
        {
            if (activateOnStart)
                Activate();
        }

        private void OnEnable()
        {
            // Start from zero — if already active was requested,
            // Activate() must be called again.
            CurrentRadius = 0f;
            TargetRadius  = 0f;
        }

        private void OnDisable()
        {
            // Clear globals so other objects don't see stale values.
            Shader.SetGlobalFloat(PropActive, 0f);
            Shader.SetGlobalFloat(PropRadius, 0f);
        }

        private void Update()
        {
            // Smooth transition
            float speed = discRadius / Mathf.Max(transitionDuration, 0.001f);
            CurrentRadius = Mathf.MoveTowards(CurrentRadius, TargetRadius,
                                              speed * Time.deltaTime);

            if (CurrentRadius > 0.001f)
            {
                Vector3 center = transform.position + Vector3.up * heightOffset;
                float playerDepth = GetPlayerViewDepth();

                Shader.SetGlobalFloat(PropActive, 1f);
                Shader.SetGlobalVector(PropCenter, center);
                Shader.SetGlobalFloat(PropRadius, CurrentRadius);
                Shader.SetGlobalFloat(PropDepth,  playerDepth);
                Shader.SetGlobalColor(PropFill,  discFillColor);
                Shader.SetGlobalColor(PropRim,   discRimColor);
                Shader.SetGlobalFloat(PropRimW,  rimWidth);

                // ── Screen-space disc params (for the fullscreen stencil pass) ──
                if (mainCamera != null)
                {
                    Vector3 viewportCenter = mainCamera.WorldToViewportPoint(center);
                    float maxScreenDist = ComputeScreenRadius(center, CurrentRadius, mainCamera);
                    Shader.SetGlobalVector(PropScreenPos,
                        new Vector2(viewportCenter.x, viewportCenter.y));
                    Shader.SetGlobalFloat(PropScreenRadius, maxScreenDist);
                }
            }
            else
            {
                Shader.SetGlobalFloat(PropActive, 0f);
                Shader.SetGlobalFloat(PropRadius, 0f);
            }
        }

        // ── Public API ────────────────────────────────────────────────────
        /// <summary>Smoothly expand the reveal disc.</summary>
        public void Activate()
        {
            TargetRadius = discRadius;
        }

        /// <summary>Smoothly shrink the reveal disc.</summary>
        public void Deactivate()
        {
            TargetRadius = 0f;
        }

        /// <summary>Instantly set state without transition.</summary>
        public void SetActiveInstant(bool active)
        {
            TargetRadius  = active ? discRadius : 0f;
            CurrentRadius = TargetRadius;
        }

        /// <summary>Immediately set the disc to a custom radius.</summary>
        public void SetRadius(float radius)
        {
            TargetRadius = Mathf.Clamp(radius, 0f, discRadius);
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private float GetPlayerViewDepth()
        {
            if (mainCamera == null) return 10f;

            Vector3 center = transform.position + Vector3.up * heightOffset;
            Vector3 viewPos = mainCamera.worldToCameraMatrix.MultiplyPoint(center);
            // In Unity camera space, forward is negative Z. Depth is positive Z distance.
            return Mathf.Max(-viewPos.z, 0.01f);
        }

        /// <summary>
        /// Computes the screen-space radius of the reveal disc by projecting
        /// edge points from world space into viewport space and taking the
        /// maximum distance from the center.
        /// </summary>
        private static float ComputeScreenRadius(Vector3 worldCenter, float worldRadius,
                                                 Camera cam)
        {
            Vector3 viewportCenter = cam.WorldToViewportPoint(worldCenter);

            // Sample four cardinal directions on the disc edge
            Vector3[] edgeDirs = { Vector3.right, Vector3.left,
                                   Vector3.forward, Vector3.back };
            float maxDist = 0f;

            foreach (Vector3 dir in edgeDirs)
            {
                Vector3 edgeWorld = worldCenter + dir * worldRadius;
                Vector3 edgeVP = cam.WorldToViewportPoint(edgeWorld);

                // If the edge point is behind the camera, skip it
                if (edgeVP.z <= 0f) continue;

                float dx = edgeVP.x - viewportCenter.x;
                float dy = edgeVP.y - viewportCenter.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                maxDist = Mathf.Max(maxDist, d);
            }

            // Add a small margin to account for the player's height
            return maxDist + 0.05f;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos && !Application.isPlaying) return;

            Vector3 center = transform.position + Vector3.up * heightOffset;
            float r = Application.isPlaying ? CurrentRadius : discRadius;

            if (r <= 0.001f) return;

            // Rim
            Gizmos.color = discRimColor;
            DrawWireDisc(center, r);

            // Inner fill
            Gizmos.color = new Color(discFillColor.r, discFillColor.g,
                                     discFillColor.b, discFillColor.a * 0.5f);
            DrawWireDisc(center, r * (1f - rimWidth));
        }

        private static void DrawWireDisc(Vector3 center, float radius,
                                         int segments = 64)
        {
            float step = 360f / segments * Mathf.Deg2Rad;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = step * i;
                Vector3 next = center + new Vector3(
                    Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
