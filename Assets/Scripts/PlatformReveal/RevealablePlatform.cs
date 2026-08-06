using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using SuperFinalFuse.BombThrowing;
using SuperFinalFuse.Explosion;

namespace SuperFinalFuse.PlatformReveal
{
    public enum InvisibleRoadRevealState
    {
        Transparent,
        Visible,
        Fading
    }

    /// <summary>
    /// A solid road whose visuals are normally transparent. Player-thrown bomb
    /// blasts reveal only the area around the blast origin through global shader
    /// reveal data. The collider remains enabled in every visual state.
    /// </summary>
    [RequireComponent(typeof(Collider), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class RevealablePlatform : MonoBehaviour,
        IBombBlastReactive,
        IShockwaveReactive
    {
        [Header("Reveal")]
        [Min(0.1f)]
        [SerializeField] private float revealRadius = 5f;
        [Min(0f)]
        [FormerlySerializedAs("holdDuration")]
        [SerializeField] private float visibleDuration = 2f;
        [Min(0.01f)]
        [SerializeField] private float fadeDuration = 1.5f;

        [Header("Rendering")]
        [Tooltip("Invisible roads normally should not cast a visible shadow.")]
        [SerializeField] private bool disableShadowCasting = true;

        [Header("Events / TA hooks")]
        [SerializeField] private UnityEvent onBecameVisible;
        [SerializeField] private UnityEvent onFadeStarted;
        [SerializeField] private UnityEvent onBecameTransparent;

        [Header("Debug")]
        [SerializeField] private bool debugLog;

        private Collider roadCollider;
        private Renderer roadRenderer;
        private float revealStartTime = float.NegativeInfinity;
        private InvisibleRoadRevealState state =
            InvisibleRoadRevealState.Transparent;

        public float RevealRadius => revealRadius;
        public float VisibleDuration => visibleDuration;
        public float FadeDuration => fadeDuration;
        public InvisibleRoadRevealState State => state;
        public bool IsVisible => state != InvisibleRoadRevealState.Transparent;

        private void Awake()
        {
            roadCollider = GetComponent<Collider>();
            roadRenderer = GetComponent<Renderer>();
            EnsurePhysicalRoad();
        }

        private void OnEnable()
        {
            EnsurePhysicalRoad();
        }

        private void Update()
        {
            if (state == InvisibleRoadRevealState.Transparent)
            {
                return;
            }

            float elapsed = Time.time - revealStartTime;
            if (state == InvisibleRoadRevealState.Visible &&
                elapsed >= visibleDuration)
            {
                SetState(InvisibleRoadRevealState.Fading);
            }

            if (elapsed >= visibleDuration + fadeDuration)
            {
                SetState(InvisibleRoadRevealState.Transparent);
            }
        }

        public void OnBombBlast(BombBlastHit hit)
        {
            RevealAt(hit.Origin);
        }

        // Compatibility with the original level-start radial explosion system.
        public void OnShockwaveHit(ShockwaveHit hit)
        {
            RevealAt(hit.Origin);
        }

        /// <summary>
        /// Public gameplay/TA entry point. Visual implementations can listen to
        /// the state events while the supplied shader uses the global reveal zone.
        /// </summary>
        public void RevealAt(Vector3 worldPosition)
        {
            revealStartTime = Time.time;
            PlatformRevealManager.Instance.RegisterReveal(
                worldPosition,
                revealRadius,
                visibleDuration,
                fadeDuration);
            SetState(InvisibleRoadRevealState.Visible, true);

            if (debugLog)
            {
                Debug.Log(
                    $"Invisible road '{name}' revealed at {worldPosition}, " +
                    $"radius {revealRadius}.",
                    this);
            }
        }

        [ContextMenu("Test Reveal At Road Centre")]
        public void TestReveal()
        {
            RevealAt(roadCollider != null
                ? roadCollider.bounds.center
                : transform.position);
        }

        private void EnsurePhysicalRoad()
        {
            if (roadCollider == null)
            {
                roadCollider = GetComponent<Collider>();
            }
            if (roadRenderer == null)
            {
                roadRenderer = GetComponent<Renderer>();
            }

            // Transparency is visual only. Never disable either component.
            if (roadCollider != null)
            {
                roadCollider.enabled = true;
                roadCollider.isTrigger = false;
            }
            if (roadRenderer != null)
            {
                roadRenderer.enabled = true;
                if (disableShadowCasting)
                {
                    roadRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private void SetState(
            InvisibleRoadRevealState newState,
            bool retriggerVisibleEvent = false)
        {
            if (state == newState && !retriggerVisibleEvent)
            {
                return;
            }

            state = newState;
            switch (state)
            {
                case InvisibleRoadRevealState.Visible:
                    onBecameVisible?.Invoke();
                    break;
                case InvisibleRoadRevealState.Fading:
                    onFadeStarted?.Invoke();
                    break;
                case InvisibleRoadRevealState.Transparent:
                    onBecameTransparent?.Invoke();
                    break;
            }
        }

        private void OnValidate()
        {
            revealRadius = Mathf.Max(0.1f, revealRadius);
            visibleDuration = Mathf.Max(0f, visibleDuration);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
            EnsurePhysicalRoad();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, revealRadius);
        }
    }
}
