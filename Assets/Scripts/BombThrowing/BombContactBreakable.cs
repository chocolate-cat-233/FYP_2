using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    public sealed class BombContactBreakable : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [SerializeField] private Renderer[] renderersToHide;
        [SerializeField] private Collider[] collidersToDisable;
        [SerializeField] private GameObject breakEffectPrefab;
        [Min(0f)]
        [SerializeField] private float effectLifetime = 3f;
        [SerializeField] private UnityEvent onBroken;

        public bool IsBroken { get; private set; }

        private void Awake()
        {
            if (renderersToHide == null || renderersToHide.Length == 0)
            {
                renderersToHide = GetComponentsInChildren<Renderer>();
            }

            if (collidersToDisable == null || collidersToDisable.Length == 0)
            {
                collidersToDisable = GetComponentsInChildren<Collider>();
            }
        }

        public void Break(Vector3 contactPoint)
        {
            if (IsBroken)
            {
                return;
            }

            IsBroken = true;
            foreach (Renderer targetRenderer in renderersToHide)
            {
                if (targetRenderer != null) targetRenderer.enabled = false;
            }

            foreach (Collider targetCollider in collidersToDisable)
            {
                if (targetCollider != null) targetCollider.enabled = false;
            }

            if (breakEffectPrefab != null)
            {
                GameObject effect = Instantiate(
                    breakEffectPrefab,
                    contactPoint,
                    Quaternion.identity);
                if (effectLifetime > 0f) Destroy(effect, effectLifetime);
            }

            onBroken?.Invoke();
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (IsBroken)
            {
                return BombHitResult.Ignore;
            }

            Break(hit.Point);
            return BombHitResult.Explode;
        }
    }
}
