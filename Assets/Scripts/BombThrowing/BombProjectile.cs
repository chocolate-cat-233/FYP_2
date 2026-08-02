using System;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Buffs;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class BombProjectile : MonoBehaviour
    {
        [Header("Explosion")]
        [Min(0.1f)]
        [SerializeField] private float blastRadius = 4f;
        [Min(0.01f)]
        [Tooltip("Shockwave expansion speed in world units per second.")]
        [SerializeField] private float blastSpeed = 12f;
        [SerializeField] private LayerMask blastLayers = ~0;
        [Tooltip("When enabled, the bomb explodes when it first lands or hits anything.")]
        [SerializeField] private bool explodeOnAnyCollision = true;
        [Min(0f)]
        [Tooltip("Safety fuse. Zero disables it.")]
        [SerializeField] private float maximumLifetime = 8f;

        [Header("Visual")]
        [SerializeField] private GameObject explosionEffectPrefab;
        [Min(0f)]
        [SerializeField] private float effectLifetime = 3f;
        [SerializeField] private bool showDebugBlast = true;
        [SerializeField] private Color debugBlastColor =
            new Color(1f, 0.25f, 0f, 0.35f);

        [Header("Events")]
        [SerializeField] private UnityEvent onExploded;

        private Rigidbody bombBody;
        private Collider bombCollider;
        private GameObject owner;
        private bool hasExploded;
        private bool hasSentCompletion;

        public event Action<BombProjectile> Exploded;
        public float BlastRadius => blastRadius;
        public bool HasExploded => hasExploded;
        public GameObject Owner => owner;

        private void Awake()
        {
            bombBody = GetComponent<Rigidbody>();
            bombCollider = GetComponent<Collider>();
        }

        private void Start()
        {
            if (maximumLifetime > 0f)
            {
                Invoke(nameof(Explode), maximumLifetime);
            }
        }

        public void Launch(Vector3 velocity, GameObject throwingOwner)
        {
            owner = throwingOwner;
            bombBody.linearVelocity = velocity;

            Collider[] ownerColliders = owner != null
                ? owner.GetComponentsInChildren<Collider>()
                : Array.Empty<Collider>();
            foreach (Collider ownerCollider in ownerColliders)
            {
                Physics.IgnoreCollision(bombCollider, ownerCollider, true);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            HandleContact(collision.collider, true);
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleContact(other, false);
        }

        private void HandleContact(
            Collider contactedCollider,
            bool isSolidCollision)
        {
            if (hasExploded || contactedCollider == null ||
                owner != null &&
                (contactedCollider.gameObject == owner ||
                 contactedCollider.transform.IsChildOf(owner.transform)))
            {
                return;
            }

            BombContactBreakable breakable =
                contactedCollider.GetComponentInParent<BombContactBreakable>();

            BombTeleportTarget teleportTarget =
                contactedCollider.GetComponentInParent<BombTeleportTarget>();
            if (teleportTarget != null && teleportTarget.TryTeleport(owner))
            {
                Explode();
                return;
            }

            if (breakable != null)
            {
                breakable.Break(transform.position);
                Explode();
                return;
            }

            if (explodeOnAnyCollision && isSolidCollision)
            {
                Explode();
            }
        }

        public void Explode()
        {
            if (hasExploded)
            {
                return;
            }

            hasExploded = true;
            CancelInvoke();
            Vector3 center = transform.position;

            if (explosionEffectPrefab != null)
            {
                GameObject effect = Instantiate(
                    explosionEffectPrefab,
                    center,
                    Quaternion.identity);
                if (effectLifetime > 0f)
                {
                    Destroy(effect, effectLifetime);
                }
            }

            BombBlastWave.Create(
                center,
                blastRadius,
                blastSpeed,
                blastLayers,
                showDebugBlast,
                debugBlastColor);

            onExploded?.Invoke();
            SendCompletionOnce();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Also unlock the thrower if this projectile is removed by an
            // external system, hierarchy cleanup, or an unexpected lifetime.
            SendCompletionOnce();
        }

        private void SendCompletionOnce()
        {
            if (hasSentCompletion)
            {
                return;
            }

            hasSentCompletion = true;
            Exploded?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = debugBlastColor;
            Gizmos.DrawWireSphere(transform.position, blastRadius);
        }
    }
}
