using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.WallHide
{
    /// <summary>
    /// Hides walls and obstacles that occlude the target: every frame it
    /// sphere-casts from the camera towards the target and pushes the target's
    /// screen-space circle into global shader arrays (_WallHidePoints /
    /// _WallHideStrengths). Renderers whose shader reads those globals
    /// (Custom/WallHideLit) cut a hole where the target appears on screen;
    /// renderers with any other material are swapped to fallbackRevealMaterial
    /// while hidden and restored afterwards, so the system also works with
    /// URP/Lit materials.
    ///
    /// Runs in LateUpdate (after the camera rig) with DefaultExecutionOrder 100.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class WallHideManager : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────
        private const int MaxHidePoints = 16;

        [Header("References (wire in the Inspector)")]
        [SerializeField] private Transform target;
        [SerializeField] private Camera targetCamera;

        [Header("Detection")]
        [Tooltip("Layers whose renderers may be hidden when they block the target. Default ~0 hides everything between camera and target; narrow the mask or use WallHideIgnore to exclude.")]
        [SerializeField] private LayerMask occlusionMask = ~0;
        [Tooltip("Sphere-cast radius. About the player capsule radius so the silhouette is covered instead of a single thin line.")]
        [Min(0f)] [SerializeField] private float castRadius = 0.45f;
        [Tooltip("Extra pixels added to the target's screen-space circle radius.")]
        [Min(0f)] [SerializeField] private float paddingPixels = 24f;
        [Tooltip("How fast the hole fades in/out, in strength units per second.")]
        [Min(0f)] [SerializeField] private float fadeSpeed = 8f;
        [Tooltip("Relative width of the halftone gradient band around the hole.")]
        [Range(0.001f, 0.5f)] [SerializeField] private float hideSoftness = 0.03f;
        [Tooltip("Halftone cell size in pixels; the hole edge dissolves into dots of this size.")]
        [Min(1f)] [SerializeField] private float halftoneCellSize = 4f;

        [Header("Fallback")]
        [Tooltip("Material swapped in for renderers whose shader does not read _WallHidePoints (e.g. URP/Lit). Usually a Custom/WallHideLit material.")]
        [SerializeField] private Material fallbackRevealMaterial;

        [Header("Debug")]
        [SerializeField] private bool debugDraw;

        // ── Shader globals ───────────────────────────────────────────────
        private readonly Vector4[] _points    = new Vector4[MaxHidePoints];
        private readonly float[]   _strengths = new float[MaxHidePoints];
        private float _strength;   // current hole strength 0..1, faded in/out

        // ── Per-frame detection ──────────────────────────────────────────
        private RaycastHit[] _hitBuffer = new RaycastHit[64];
        private readonly List<Renderer> _hiddenRenderers = new List<Renderer>(16);
        private bool _hadHiddenThisFrame;

        // Cached target components (resolved once — the player hierarchy does
        // not change at runtime).
        private Collider[] _cachedColliders;
        private Renderer[] _cachedRenderers;

        // ── Fallback material-swap path ──────────────────────────────────
        private readonly Dictionary<Renderer, Material[]> _swappedOriginals = new Dictionary<Renderer, Material[]>();
        private readonly List<Renderer> _restoreList = new List<Renderer>(8);
        private bool _warnedNoFallbackMaterial;

        // ── Debug state ──────────────────────────────────────────────────
        private Vector3 _debugEndPoint;

        // ── Unity messages ───────────────────────────────────────────────
        private void Awake()
        {
            if (target == null)
            {
                target = FindTargetWithTag("Player");
                if (target == null)
                {
                    Debug.LogError($"{nameof(WallHideManager)} on '{name}' requires a target Transform. Wire it in the Inspector.", this);
                }
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main ?? FindFirstObjectByType<Camera>();
                if (targetCamera == null)
                {
                    Debug.LogError($"{nameof(WallHideManager)} on '{name}' requires a Camera. Wire it in the Inspector.", this);
                }
            }

            CacheTargetComponents();
            PushToShader();
        }

        private void LateUpdate()
        {
            if (target == null || targetCamera == null)
                return;

            UpdateHiding();
            UpdateStrength();
            PushToShader();
        }

        private void OnDisable()
        {
            RestoreAllSwappedMaterials();
        }

        private void OnDestroy()
        {
            RestoreAllSwappedMaterials();
            _strength = 0f;
            System.Array.Clear(_points, 0, MaxHidePoints);
            System.Array.Clear(_strengths, 0, MaxHidePoints);
            PushToShader();
        }

        // ── Detection ────────────────────────────────────────────────────
        private void UpdateHiding()
        {
            _hiddenRenderers.Clear();

            bool visible = TryGetTargetScreenRect(out Vector2 rectMin, out Vector2 rectMax);
            if (visible)
            {
                Bounds bounds = GetTargetBounds();
                Vector3 camPos = targetCamera.transform.position;
                Vector3 endPoint = bounds.ClosestPoint(camPos);
                _debugEndPoint = endPoint;

                Vector3 direction = endPoint - camPos;
                float distance = direction.magnitude;
                if (distance > 0.0001f)
                {
                    direction /= distance;
                    CollectHiddenRenderers(camPos, direction, distance);
                }
            }

            _hadHiddenThisFrame = _hiddenRenderers.Count > 0;
            if (_hadHiddenThisFrame)
            {
                // Enclosing circle: centre of the projected bounds, radius
                // covering the largest screen extent plus padding (pixels).
                float halfW = (rectMax.x - rectMin.x) * 0.5f;
                float halfH = (rectMax.y - rectMin.y) * 0.5f;
                float radiusPx = Mathf.Max(halfW, halfH) + paddingPixels;

                _points[0] = new Vector4(
                    rectMin.x + halfW,   // centre x (pixels, bottom-left origin)
                    rectMin.y + halfH,   // centre y (pixels)
                    radiusPx,            // radius (pixels)
                    0f);
            }

            // Native path (shader reads the globals) needs nothing per
            // renderer; unsupported materials swap to the fallback instead.
            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                HideRenderer(_hiddenRenderers[i]);
            }

            RestoreNoLongerHiddenRenderers();
        }

        private void CollectHiddenRenderers(Vector3 camPos, Vector3 direction, float distance)
        {
            int hitCount = SphereCastAllNonAlloc(camPos, direction, distance);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _hitBuffer[i];

                // Colliders at/behind the target are not obstacles.
                if (hit.distance > distance - castRadius)
                    continue;

                Collider c = hit.collider;

                // The target's own colliders.
                if (c.transform == target || c.transform.IsChildOf(target))
                    continue;

                // Explicitly excluded from hiding.
                if (c.GetComponentInParent<WallHideIgnore>(true) != null)
                    continue;

                Renderer renderer = c.GetComponent<Renderer>();
                if (renderer == null)
                {
                    renderer = c.GetComponentInParent<Renderer>();
                }
                if (renderer == null || !renderer.enabled)
                    continue;

                _hiddenRenderers.Add(renderer);
            }
        }

        private int SphereCastAllNonAlloc(Vector3 origin, Vector3 direction, float maxDistance)
        {
            int count = Physics.SphereCastNonAlloc(origin, castRadius, direction, _hitBuffer,
                maxDistance, occlusionMask, QueryTriggerInteraction.Ignore);

            if (count == _hitBuffer.Length)
            {
                // Buffer full: grow once and retry to stay correct.
                _hitBuffer = new RaycastHit[_hitBuffer.Length * 2];
                count = Physics.SphereCastNonAlloc(origin, castRadius, direction, _hitBuffer,
                    maxDistance, occlusionMask, QueryTriggerInteraction.Ignore);
            }

            return count;
        }

        // ── Target screen rect ───────────────────────────────────────────
        /// <summary>
        /// Projects the target's bounds to a screen-pixel rectangle (bottom-left
        /// origin, matching shader UV). Returns false when the target is fully
        /// behind the camera or completely off-screen — in that case nothing is
        /// hidden.
        /// </summary>
        private bool TryGetTargetScreenRect(out Vector2 rectMin, out Vector2 rectMax)
        {
            rectMin = new Vector2(float.MaxValue, float.MaxValue);
            rectMax = new Vector2(float.MinValue, float.MinValue);
            bool anyVisible = false;

            Bounds bounds = GetTargetBounds();
            Vector3 center   = bounds.center;
            Vector3 extents  = bounds.extents;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z);

                Vector3 screen = targetCamera.WorldToScreenPoint(corner);
                if (screen.z <= 0f)
                    continue;   // corner behind the camera

                anyVisible = true;
                rectMin = Vector2.Min(rectMin, screen);
                rectMax = Vector2.Max(rectMax, screen);
            }

            return anyVisible;
        }

        private Bounds GetTargetBounds()
        {
            Bounds bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < _cachedColliders.Length; i++)
            {
                if (_cachedColliders[i].isTrigger)
                    continue;
                if (hasBounds) bounds.Encapsulate(_cachedColliders[i].bounds);
                else           { bounds = _cachedColliders[i].bounds; hasBounds = true; }
            }

            if (!hasBounds)
            {
                for (int i = 0; i < _cachedRenderers.Length; i++)
                {
                    if (!_cachedRenderers[i].enabled)
                        continue;
                    if (hasBounds) bounds.Encapsulate(_cachedRenderers[i].bounds);
                    else           { bounds = _cachedRenderers[i].bounds; hasBounds = true; }
                }
            }

            return hasBounds ? bounds : new Bounds(target.position, Vector3.one * 0.5f);
        }

        // ── Fade + push ──────────────────────────────────────────────────
        private void UpdateStrength()
        {
            float targetStrength = _hadHiddenThisFrame ? 1f : 0f;
            _strength = Mathf.MoveTowards(_strength, targetStrength, fadeSpeed * Time.deltaTime);
            _strengths[0] = _strength;
            // _points[0] keeps the last rect while fading out — the shader
            // scales the rect by the strength, so the hole shrinks to nothing.
        }

        private void PushToShader()
        {
            Shader.SetGlobalVectorArray("_WallHidePoints",    _points);
            Shader.SetGlobalFloatArray ("_WallHideStrengths", _strengths);
            Shader.SetGlobalFloat      ("_HideSoftness", hideSoftness);
            Shader.SetGlobalFloat      ("_HideHalftoneCellSize", halftoneCellSize);
        }

        // ── Fallback material swap ───────────────────────────────────────
        private void HideRenderer(Renderer renderer)
        {
            if (_swappedOriginals.ContainsKey(renderer))
                return;   // already swapped

            // Native path: the shader reads _WallHidePoints itself.
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_WallHidePoints"))
                return;

            if (fallbackRevealMaterial == null)
            {
                if (!_warnedNoFallbackMaterial)
                {
                    _warnedNoFallbackMaterial = true;
                    Debug.LogWarning(
                        $"{nameof(WallHideManager)} on '{name}': renderer '{renderer.name}' does not use a wall-hiding shader " +
                        $"and no fallbackRevealMaterial is assigned, so it cannot be hidden.", this);
                }
                return;
            }

            Material[] originals = renderer.sharedMaterials;
            _swappedOriginals.Add(renderer, originals);

            Material[] replacement = new Material[originals.Length];
            for (int i = 0; i < replacement.Length; i++)
            {
                replacement[i] = fallbackRevealMaterial;
            }
            renderer.sharedMaterials = replacement;
        }

        private void RestoreNoLongerHiddenRenderers()
        {
            if (_swappedOriginals.Count == 0)
                return;

            _restoreList.Clear();
            foreach (KeyValuePair<Renderer, Material[]> kvp in _swappedOriginals)
            {
                if (!_hiddenRenderers.Contains(kvp.Key))
                    _restoreList.Add(kvp.Key);
            }

            for (int i = 0; i < _restoreList.Count; i++)
            {
                RestoreRenderer(_restoreList[i]);
            }
        }

        private void RestoreRenderer(Renderer renderer)
        {
            if (_swappedOriginals.TryGetValue(renderer, out Material[] originals))
            {
                renderer.sharedMaterials = originals;
                _swappedOriginals.Remove(renderer);
            }
        }

        private void RestoreAllSwappedMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> kvp in _swappedOriginals)
            {
                if (kvp.Key != null)
                    kvp.Key.sharedMaterials = kvp.Value;
            }
            _swappedOriginals.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void CacheTargetComponents()
        {
            if (target == null)
            {
                _cachedColliders = System.Array.Empty<Collider>();
                _cachedRenderers = System.Array.Empty<Renderer>();
                return;
            }

            _cachedColliders = target.GetComponentsInChildren<Collider>(true);
            _cachedRenderers = target.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// The "Player" tag is not registered in this project's TagManager,
        /// so FindGameObjectWithTag may throw — hence the guard.
        /// </summary>
        private static Transform FindTargetWithTag(string tag)
        {
            try
            {
                GameObject go = GameObject.FindGameObjectWithTag(tag);
                return go != null ? go.transform : null;
            }
            catch (UnityException)
            {
                return null;
            }
        }

        // ── Gizmos ───────────────────────────────────────────────────────
        private void OnDrawGizmos()
        {
            if (!debugDraw || target == null || targetCamera == null)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_debugEndPoint, castRadius);
            Gizmos.DrawLine(targetCamera.transform.position, _debugEndPoint);

            Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
            Gizmos.DrawWireCube(GetTargetBounds().center, GetTargetBounds().size);
        }
    }
}
