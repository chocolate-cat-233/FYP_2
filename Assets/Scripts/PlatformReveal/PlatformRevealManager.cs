using UnityEngine;

namespace SuperFinalFuse.PlatformReveal
{
    /// <summary>
    /// Singleton that tracks all active platform-reveal zones and pushes them
    /// to global shader arrays every frame so that TransparantPlatform materials
    /// can compute per-fragment alpha via distance fields.
    ///
    /// Auto-creates itself on first access – no manual scene setup required.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlatformRevealManager : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────
        private const int MaxRevealPoints = 16;

        // ── Singleton ────────────────────────────────────────────────────
        private static PlatformRevealManager _instance;

        public static PlatformRevealManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<PlatformRevealManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[PlatformRevealManager]");
                        DontDestroyOnLoad(go);
                        _instance = go.AddComponent<PlatformRevealManager>();
                    }
                }

                return _instance;
            }
        }

        // ── State ────────────────────────────────────────────────────────
        private readonly Vector4[] _points    = new Vector4[MaxRevealPoints];
        private readonly float[]   _strengths = new float[MaxRevealPoints];
        private readonly float[]   _startTime = new float[MaxRevealPoints];
        private readonly float[]   _duration  = new float[MaxRevealPoints];
        private readonly float[]   _fadeDuration = new float[MaxRevealPoints];
        private int _activeCount;

        // ── Unity messages ───────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Zero out shader globals on startup.
            PushToShader();
        }

        private void Update()
        {
            bool dirty = false;

            for (int i = _activeCount - 1; i >= 0; i--)
            {
                float elapsed    = Time.time - _startTime[i];
                float totalLife  = _duration[i] + _fadeDuration[i];

                if (elapsed >= totalLife)
                {
                    RemoveAt(i);
                    dirty = true;
                    continue;
                }

                float targetStrength;
                if (elapsed <= _duration[i])
                {
                    targetStrength = 1f;
                }
                else
                {
                    float fadeT = (elapsed - _duration[i]) / _fadeDuration[i];
                    targetStrength = 1f - Mathf.Clamp01(fadeT);
                }

                if (!Mathf.Approximately(_strengths[i], targetStrength))
                {
                    _strengths[i] = targetStrength;
                    dirty = true;
                }
            }

            if (dirty)
            {
                PushToShader();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _activeCount = 0;
                System.Array.Clear(_strengths, 0, MaxRevealPoints);
                PushToShader();
            }
        }

        // ── Public API ───────────────────────────────────────────────────
        /// <summary>
        /// Register a new reveal zone. If the pool is full the oldest entry
        /// is evicted.
        /// </summary>
        /// <param name="worldPosition">Centre of the reveal zone.</param>
        /// <param name="radius">Radius inside which the platform becomes visible.</param>
        /// <param name="duration">Seconds the zone stays at full strength.</param>
        /// <param name="fadeDurationSeconds">Seconds to fade from full to invisible.</param>
        public void RegisterReveal(
            Vector3 worldPosition,
            float radius,
            float duration,
            float fadeDurationSeconds)
        {
            if (_activeCount >= MaxRevealPoints)
            {
                // Evict the oldest entry to make room.
                RemoveAt(0);
            }

            int i = _activeCount;
            _points[i]        = new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, radius);
            _strengths[i]     = 1f;
            _startTime[i]     = Time.time;
            _duration[i]      = duration;
            _fadeDuration[i]  = Mathf.Max(0.01f, fadeDurationSeconds);
            _activeCount++;

            PushToShader();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void RemoveAt(int index)
        {
            _activeCount--;
            for (int i = index; i < _activeCount; i++)
            {
                _points[i]        = _points[i + 1];
                _strengths[i]     = _strengths[i + 1];
                _startTime[i]     = _startTime[i + 1];
                _duration[i]      = _duration[i + 1];
                _fadeDuration[i]  = _fadeDuration[i + 1];
            }

            _points[_activeCount] = Vector4.zero;
            _strengths[_activeCount] = 0f;
            _startTime[_activeCount] = 0f;
            _duration[_activeCount] = 0f;
            _fadeDuration[_activeCount] = 0f;
        }

        private void PushToShader()
        {
            Shader.SetGlobalVectorArray("_RevealPoints",    _points);
            Shader.SetGlobalFloatArray ("_RevealStrengths", _strengths);
        }
    }
}
