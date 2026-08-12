using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.WallHide
{
    /// <summary>
    /// Hides walls and obstacles that occlude the target: every frame it
    /// sphere-casts from the camera towards the target and, for every hit
    /// renderer, writes the target's screen-space hide circle (_WallHidePoint)
    /// via MaterialPropertyBlock. Only the renderers actually detected as
    /// occluders receive the circle, so other objects sharing the same
    /// material (Custom/WallHideLit) keep rendering normally; renderers with
    /// any other material are swapped to fallbackRevealMaterial while hidden
    /// and restored afterwards, so the system also works with URP/Lit.
    ///
    /// Runs in LateUpdate (after the camera rig) with DefaultExecutionOrder 100.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class WallHideManager : MonoBehaviour
    {

        [Header("References (wire in the Inspector)")]
        [SerializeField] private Transform target;
        [SerializeField] private Camera targetCamera;

        [Header("Detection")]
        [Tooltip("Layers whose renderers may be hidden when they block the target. Default ~0 hides everything between camera and target; narrow the mask or use WallHideIgnore to exclude.")]
        [SerializeField] private LayerMask occlusionMask = ~0;
        [Tooltip("Sphere-cast radius. About the player capsule radius so the silhouette is covered instead of a single thin line.")]
        [Min(0f)][SerializeField] private float castRadius = 0.45f;
        [Tooltip("Extra pixels added to the target's screen-space circle radius.")]
        [Min(0f)][SerializeField] private float paddingPixels = 24f;
        [Tooltip("How fast the hole fades in/out, in strength units per second.")]
        [Min(0f)][SerializeField] private float fadeSpeed = 8f;
        [Tooltip("Relative width of the halftone gradient band around the hole.")]
        [Range(0.001f, 1f)][SerializeField] private float hideSoftness = 0.03f;
        [Tooltip("Halftone cell size in pixels; the hole edge dissolves into dots of this size.")]
        [Min(1f)][SerializeField] private float halftoneCellSize = 4f;

        [Header("Fallback")]
        [Tooltip("Material swapped in for renderers whose shader does not read _WallHidePoint (e.g. URP/Lit). Usually a Custom/WallHideLit material.")]
        [SerializeField] private Material fallbackRevealMaterial;

        [Header("Debug")]
        [SerializeField] private bool debugDraw;

        // ── Per-occluder property block ─────────────────────────────────
        // Note: not readonly, and re-seeded in EnsureFieldInitializers —
        // Unity's script reload restores fields from a serialized snapshot
        // where empty collections come back as null.
        private static readonly int _hidePointId = Shader.PropertyToID("_WallHidePoint");
        // Created in EnsureFieldInitializers, not here: native-implemented
        // classes must not be instantiated in a MonoBehaviour constructor /
        // field initializer (throws UnityException).
        private MaterialPropertyBlock _propertyBlock;
        private Vector3 _lastCircle;   // cached hide circle (xy = centre px, z = full radius px)

        // Per-renderer fade strength (0..1); each occluder fades in/out on
        // its own schedule instead of sharing one global strength.
        private Dictionary<Renderer, float> _occluderFades = new Dictionary<Renderer, float>();
        private List<Renderer> _finishedFades = new List<Renderer>(8);
        private List<KeyValuePair<Renderer, float>> _fadeUpdates = new List<KeyValuePair<Renderer, float>>(8);

        // ── Per-frame detection ──────────────────────────────────────────
        private RaycastHit[] _hitBuffer = new RaycastHit[64];
        private List<Renderer> _hiddenRenderers = new List<Renderer>(16);

        // Cached target components (resolved once — the player hierarchy does
        // not change at runtime).
        private Collider[] _cachedColliders;
        private Renderer[] _cachedRenderers;

        // ── Fallback material-swap path ──────────────────────────────────
        private Dictionary<Renderer, Material[]> _swappedOriginals = new Dictionary<Renderer, Material[]>();
        private List<Renderer> _restoreList = new List<Renderer>(8);
        private bool _warnedNoFallbackMaterial;

        // ── Debug state ──────────────────────────────────────────────────
        private Vector3 _debugEndPoint;

        // ── Unity messages ───────────────────────────────────────────────
        private void Awake()
        {
            EnsureFieldInitializers();

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
            PushGlobalSettings();
        }

        private void LateUpdate()
        {
            if (target == null || targetCamera == null)
                return;

            EnsureFieldInitializers();
            UpdateHiding();
        }

        private void OnDisable()
        {
            EnsureFieldInitializers();
            ReleaseAllOccluders();
            RestoreAllSwappedMaterials();
        }

        private void OnDestroy()
        {
            EnsureFieldInitializers();
            ReleaseAllOccluders();
            RestoreAllSwappedMaterials();
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

            if (_hiddenRenderers.Count > 0)
            {
                // Enclosing circle: centre of the projected bounds, radius
                // covering the largest screen extent plus padding (pixels).
                float halfW = (rectMax.x - rectMin.x) * 0.5f;
                float halfH = (rectMax.y - rectMin.y) * 0.5f;
                _lastCircle = new Vector3(
                    rectMin.x + halfW,   // centre x (pixels, bottom-left origin)
                    rectMin.y + halfH,   // centre y (pixels)
                    Mathf.Max(halfW, halfH) + paddingPixels);   // radius (pixels)
            }

            UpdateOccluderFades();

            // Fallback path (materials without the hiding property) needs a
            // per-renderer material swap instead of the property block.
            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                HideRenderer(_hiddenRenderers[i]);
            }

            RestoreNoLongerHiddenRenderers();
        }

        /// <summary>
        /// Fades every tracked occluder towards its own target strength (1
        /// while detected this frame, 0 otherwise). Objects that just started
        /// occluding fade in from 0; objects that left the line of sight fade
        /// out and release their property block, while the others stay fully
        /// hidden — each object fades independently.
        /// </summary>
        private void UpdateOccluderFades()
        {
            // New occluders enter the fade map at 0 and grow in.
            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                if (!_occluderFades.ContainsKey(_hiddenRenderers[i]))
                    _occluderFades[_hiddenRenderers[i]] = 0f;
            }

            float step = fadeSpeed * Time.deltaTime;

            // Advance every tracked fade. The dictionary must not be touched
            // while enumerating (even updating an existing key's value throws
            // InvalidOperationException), so both the finished keys and the
            // new values are collected into reusable lists first.
            _finishedFades.Clear();
            _fadeUpdates.Clear();
            foreach (KeyValuePair<Renderer, float> kvp in _occluderFades)
            {
                float fade = Mathf.MoveTowards(kvp.Value, _hiddenRenderers.Contains(kvp.Key) ? 1f : 0f, step);
                if (fade <= 0.001f)
                    _finishedFades.Add(kvp.Key);
                else
                    _fadeUpdates.Add(new KeyValuePair<Renderer, float>(kvp.Key, fade));
            }

            for (int i = 0; i < _fadeUpdates.Count; i++)
            {
                _occluderFades[_fadeUpdates[i].Key] = _fadeUpdates[i].Value;
            }

            for (int i = 0; i < _finishedFades.Count; i++)
            {
                Renderer renderer = _finishedFades[i];
                _occluderFades.Remove(renderer);
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }

            // Apply the shared circle scaled by each object's own fade, so the
            // hole grows/shrinks as that object fades in/out.
            foreach (KeyValuePair<Renderer, float> kvp in _occluderFades)
            {
                Renderer renderer = kvp.Key;
                if (renderer == null)
                    continue;   // destroyed mid-fade; cleaned up on later frames

                _propertyBlock.SetVector(_hidePointId, new Vector4(
                    _lastCircle.x, _lastCircle.y, _lastCircle.z * kvp.Value, 0f));
                renderer.SetPropertyBlock(_propertyBlock);
            }
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
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

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
                else { bounds = _cachedColliders[i].bounds; hasBounds = true; }
            }

            if (!hasBounds)
            {
                for (int i = 0; i < _cachedRenderers.Length; i++)
                {
                    if (!_cachedRenderers[i].enabled)
                        continue;
                    if (hasBounds) bounds.Encapsulate(_cachedRenderers[i].bounds);
                    else { bounds = _cachedRenderers[i].bounds; hasBounds = true; }
                }
            }

            return hasBounds ? bounds : new Bounds(target.position, Vector3.one * 0.5f);
        }

        // ── Settings ─────────────────────────────────────────────────────
        /// <summary>
        /// Look settings shared by every WallHideLit material; pushed once.
        /// </summary>
        private void PushGlobalSettings()
        {
            Shader.SetGlobalFloat("_HideSoftness", hideSoftness);
            Shader.SetGlobalFloat("_HideHalftoneCellSize", halftoneCellSize);
        }

        /// <summary>
        /// Removes the property block from every tracked occluder, so each
        /// renderer falls back to the material default radius 0 (no hiding).
        /// Note this clears the whole block, so a renderer whose block is
        /// also used by another system must not be handled by this manager.
        /// </summary>
        private void ReleaseAllOccluders()
        {
            foreach (KeyValuePair<Renderer, float> kvp in _occluderFades)
            {
                if (kvp.Key != null)
                    kvp.Key.SetPropertyBlock(null);
            }
            _occluderFades.Clear();
        }

        // ── Fallback material swap ───────────────────────────────────────
        private void HideRenderer(Renderer renderer)
        {
            if (_swappedOriginals.ContainsKey(renderer))
                return;   // already swapped

            // Native path: the shader reads _WallHidePoint from its own
            // material property block.
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_WallHidePoint"))
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
        /// <summary>
        /// Re-seeds the reusable containers if they were lost. When scripts
        /// recompile while in Play Mode, Unity restores MonoBehaviour fields
        /// from a serialized snapshot in which empty collections come back as
        /// null — even for fields with initializers. Idempotent and cheap;
        /// called at every lifecycle entry point because Awake does not
        /// necessarily re-run after a reload.
        /// </summary>
        private void EnsureFieldInitializers()
        {
            _hitBuffer ??= new RaycastHit[64];
            _hiddenRenderers ??= new List<Renderer>(16);
            _occluderFades ??= new Dictionary<Renderer, float>();
            _finishedFades ??= new List<Renderer>(8);
            _fadeUpdates ??= new List<KeyValuePair<Renderer, float>>(8);
            _restoreList ??= new List<Renderer>(8);
            _swappedOriginals ??= new Dictionary<Renderer, Material[]>();
            _propertyBlock ??= new MaterialPropertyBlock();
        }

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
