using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.Explosion
{
    public enum DestructionEffectMode
    {
        ParticleEffect,
        PhysicalDebris
    }

    [DisallowMultipleComponent]
    public sealed class ShockwaveDestructible : MonoBehaviour, IShockwaveReactive
    {
        [Header("Destruction mode")]
        [SerializeField] private DestructionEffectMode effectMode =
            DestructionEffectMode.PhysicalDebris;

        [Header("Intact object")]
        [SerializeField] private Renderer[] intactRenderers;
        [SerializeField] private Collider[] intactColliders;

        [Header("Particle effect mode")]
        [Tooltip("Instantiated once at the object's position when destroyed.")]
        [SerializeField] private GameObject particleEffectPrefab;
        [Min(0f)]
        [SerializeField] private float particleLifetime = 3f;

        [Header("Physical debris mode")]
        [SerializeField] private GameObject debrisRoot;
        [Min(0f)]
        [SerializeField] private float debrisImpulseMultiplier = 1f;
        [Min(0f)]
        [SerializeField] private float debrisLifetime = 5f;

        [Header("Event")]
        [SerializeField] private UnityEvent onDestroyed;

        public bool IsDestroyed { get; private set; }

        private void Awake()
        {
            // Debris must not be included in automatic intact-part discovery.
            if (debrisRoot != null)
            {
                debrisRoot.SetActive(false);
            }

            if (intactRenderers == null || intactRenderers.Length == 0)
            {
                intactRenderers = GetComponentsInChildren<Renderer>();
            }

            if (intactColliders == null || intactColliders.Length == 0)
            {
                intactColliders = GetComponentsInChildren<Collider>();
            }
        }

        public void OnShockwaveHit(ShockwaveHit hit)
        {
            if (IsDestroyed)
            {
                return;
            }

            IsDestroyed = true;

            foreach (Renderer targetRenderer in intactRenderers)
            {
                if (targetRenderer != null)
                {
                    targetRenderer.enabled = false;
                }
            }

            foreach (Collider targetCollider in intactColliders)
            {
                if (targetCollider != null)
                {
                    targetCollider.enabled = false;
                }
            }

            if (effectMode == DestructionEffectMode.ParticleEffect)
            {
                PlayParticleEffect();
            }
            else
            {
                ReleasePhysicalDebris(hit);
            }

            onDestroyed?.Invoke();
        }

        private void PlayParticleEffect()
        {
            if (particleEffectPrefab == null)
            {
                Debug.LogWarning(
                    $"{nameof(ShockwaveDestructible)} on '{name}' is using " +
                    $"{nameof(DestructionEffectMode.ParticleEffect)} mode but " +
                    "has no Particle Effect Prefab assigned.",
                    this);
                return;
            }

            GameObject effect = Instantiate(
                particleEffectPrefab,
                transform.position,
                transform.rotation);

            if (particleLifetime > 0f)
            {
                Destroy(effect, particleLifetime);
            }
        }

        private void ReleasePhysicalDebris(ShockwaveHit hit)
        {
            if (debrisRoot == null)
            {
                Debug.LogWarning(
                    $"{nameof(ShockwaveDestructible)} on '{name}' is using " +
                    $"{nameof(DestructionEffectMode.PhysicalDebris)} mode but " +
                    "has no Debris Root assigned.",
                    this);
                return;
            }

            debrisRoot.transform.SetParent(null, true);
            debrisRoot.SetActive(true);

            foreach (Rigidbody body in
                     debrisRoot.GetComponentsInChildren<Rigidbody>())
            {
                Vector3 direction = body.worldCenterOfMass - hit.Origin;
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = hit.Direction;
                }

                body.AddForce(
                    direction.normalized *
                    hit.Strength *
                    debrisImpulseMultiplier,
                    ForceMode.Impulse);
            }

            if (debrisLifetime > 0f)
            {
                Destroy(debrisRoot, debrisLifetime);
            }
        }
    }
}
