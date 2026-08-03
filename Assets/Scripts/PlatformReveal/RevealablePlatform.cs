using UnityEngine;
using SuperFinalFuse.Explosion;

namespace SuperFinalFuse.PlatformReveal
{
    /// <summary>
    /// Put this on any platform GameObject that should be normally invisible
    /// but temporarily revealed when hit by a RadialShockwaveExplosion.
    ///
    /// Requires a Collider set to a layer included in the explosion's
    /// Reactive Layers mask.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class RevealablePlatform : MonoBehaviour, IShockwaveReactive
    {
        [Header("Reveal zone")]
        [Tooltip("Radius around the explosion centre inside which this platform becomes visible.")]
        [Min(0.1f)]
        [SerializeField] private float revealRadius = 5f;

        [Header("Timing")]
        [Tooltip("How long the platform stays fully revealed before fading.")]
        [Min(0f)]
        [SerializeField] private float holdDuration = 2f;

        [Tooltip("How long the fade back to invisible takes.")]
        [Min(0.01f)]
        [SerializeField] private float fadeDuration = 1.5f;

        [Header("Reveal origin")]
        [Tooltip("When true the centre is the explosion origin (the bomb). "
               + "When false it is the hit point on this platform.")]
        [SerializeField] private bool useExplosionOrigin = true;

        [Header("Debug")]
        [Tooltip("Log every shockwave hit to the console.")]
        [SerializeField] private bool debugLog = true;

        // ── IShockwaveReactive ───────────────────────────────────────────
        public void OnShockwaveHit(ShockwaveHit hit)
        {
            Vector3 centre = useExplosionOrigin ? hit.Origin : hit.Point;

            if (debugLog)
            {
                Debug.Log(
                    $"[RevealablePlatform] Hit on <b>{name}</b> — "
                    + $"centre: {centre}, radius: {revealRadius}, "
                    + $"hold: {holdDuration}s, fade: {fadeDuration}s",
                    this);
            }

            PlatformRevealManager.Instance.RegisterReveal(
                centre, revealRadius, holdDuration, fadeDuration);
        }

        // ── Editor test (right-click the component header) ──────────────
        [ContextMenu("Test Reveal (at this object)")]
        private void TestReveal()
        {
            Vector3 centre = transform.position;
            Debug.Log(
                $"[RevealablePlatform] <b>Manual test</b> on \"{name}\" — "
                + $"centre: {centre}, radius: {revealRadius}, "
                + $"hold: {holdDuration}s, fade: {fadeDuration}s",
                this);

            PlatformRevealManager.Instance.RegisterReveal(
                centre, revealRadius, holdDuration, fadeDuration);
        }

        // ── Editor ───────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, revealRadius);
        }
    }
}
