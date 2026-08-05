using UnityEngine;

namespace SuperFinalFuse.Grass
{
    /// <summary>
    /// Singleton that collects all active GrassPusher components and pushes
    /// them into global shader arrays every frame, so grass blades
    /// (Custom/GrassGeometry) bend away from colliders in the geometry
    /// shader.
    ///
    /// Auto-creates itself on first access — no manual scene setup required.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GrassPushManager : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────
        private const int MaxPushers = 16;

        // ── Singleton ────────────────────────────────────────────────────
        private static GrassPushManager _instance;

        public static GrassPushManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<GrassPushManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[GrassPushManager]");
                        DontDestroyOnLoad(go);
                        _instance = go.AddComponent<GrassPushManager>();
                    }
                }

                return _instance;
            }
        }

        // ── State ────────────────────────────────────────────────────────
        private readonly GrassPusher[] pushers = new GrassPusher[MaxPushers];
        private readonly Vector4[] points = new Vector4[MaxPushers];
        private readonly float[] strengths = new float[MaxPushers];
        private int activeCount;

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

        private void LateUpdate()
        {
            // Pushers move every frame, so just rewrite the arrays.
            for (int i = 0; i < activeCount; i++)
            {
                if (pushers[i] == null)
                {
                    strengths[i] = 0f;
                    continue;
                }

                Vector3 center = pushers[i].PushCenter;
                points[i] = new Vector4(
                    center.x,
                    center.y,
                    center.z,
                    pushers[i].Radius);
                strengths[i] = pushers[i].Strength;
            }

            PushToShader();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                activeCount = 0;
                System.Array.Clear(points, 0, MaxPushers);
                System.Array.Clear(strengths, 0, MaxPushers);
                PushToShader();
            }
        }

        // ── Public API ───────────────────────────────────────────────────
        public void Register(GrassPusher pusher)
        {
            if (pusher == null)
            {
                return;
            }

            if (activeCount >= MaxPushers)
            {
                // Evict the oldest entry to make room.
                RemoveAt(0);
            }

            pushers[activeCount] = pusher;
            activeCount++;
        }

        public void Unregister(GrassPusher pusher)
        {
            int index = System.Array.IndexOf(pushers, pusher);
            if (index < 0)
            {
                return;
            }

            RemoveAt(index);
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void RemoveAt(int index)
        {
            activeCount--;
            for (int i = index; i < activeCount; i++)
            {
                pushers[i] = pushers[i + 1];
            }

            pushers[activeCount] = null;
        }

        private void PushToShader()
        {
            Shader.SetGlobalVectorArray("_GrassPushers", points);
            Shader.SetGlobalFloatArray("_GrassPushStrengths", strengths);
        }
    }
}
