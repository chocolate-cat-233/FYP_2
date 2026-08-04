using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum ExplosiveBarrelState
    {
        Normal,
        Moving,
        Exploded
    }

    public enum BarrelExplosionCause
    {
        DirectBombHit,
        ChainReaction,
        HighFall,
        External
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class ExplosiveBarrel : MonoBehaviour,
        IBombDirectHitReceiver,
        IBombBlastReactive
    {
        [Header("Direct bomb hit")]
        [SerializeField] private BombOwnerMask directHitOwners =
            BombOwnerMask.Player;

        [Header("Bomb shockwave push")]
        [Min(0f)]
        [SerializeField] private float shockwavePushImpulse = 5f;
        [SerializeField] private bool horizontalPushOnly = true;
        [Min(0f)]
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        [Header("High fall explosion")]
        [Min(0f)]
        [Tooltip("Equivalent vertical fall height required to explode on landing. Zero disables it.")]
        [SerializeField] private float minimumFallHeight = 3f;
        [Range(0f, 1f)]
        [SerializeField] private float minimumGroundNormalY = 0.5f;

        [Header("Barrel explosion")]
        [Min(0.1f)]
        [SerializeField] private float blastRadius = 4f;
        [Min(0.01f)]
        [SerializeField] private float blastSpeed = 10f;
        [SerializeField] private LayerMask blastLayers = ~0;
        [SerializeField] private bool showDebugBlast = true;
        [SerializeField] private Color debugBlastColor =
            new Color(1f, 0.45f, 0f, 0.45f);

        [Header("Chain reaction")]
        [Min(0f)]
        [SerializeField] private float chainReactionRadius = 5f;
        [SerializeField] private LayerMask chainReactionLayers = ~0;

        [Header("Presentation")]
        [SerializeField] private Renderer[] renderersToHide;
        [SerializeField] private Collider[] collidersToDisable;
        [SerializeField] private GameObject explosionEffectPrefab;
        [Min(0f)]
        [SerializeField] private float effectLifetime = 3f;
        [SerializeField] private UnityEvent onExploded;

        [SerializeField, HideInInspector]
        private ExplosiveBarrelState state = ExplosiveBarrelState.Normal;

        private Rigidbody barrelBody;
        private float previousVerticalVelocity;

        public ExplosiveBarrelState State => state;
        public BarrelExplosionCause LastExplosionCause { get; private set; }

        private void Awake()
        {
            barrelBody = GetComponent<Rigidbody>();

            if (renderersToHide == null || renderersToHide.Length == 0)
            {
                renderersToHide = GetComponentsInChildren<Renderer>();
            }

            if (collidersToDisable == null || collidersToDisable.Length == 0)
            {
                collidersToDisable = GetComponentsInChildren<Collider>();
            }
        }

        private void FixedUpdate()
        {
            if (state == ExplosiveBarrelState.Exploded)
            {
                return;
            }

            previousVerticalVelocity = barrelBody.linearVelocity.y;
            Vector3 horizontalVelocity = barrelBody.linearVelocity;
            horizontalVelocity.y = 0f;
            state = horizontalVelocity.sqrMagnitude >
                    movingSpeedThreshold * movingSpeedThreshold
                ? ExplosiveBarrelState.Moving
                : ExplosiveBarrelState.Normal;
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (state == ExplosiveBarrelState.Exploded ||
                !directHitOwners.Contains(hit.OwnerType))
            {
                return BombHitResult.Ignore;
            }

            Explode(BarrelExplosionCause.DirectBombHit);
            return BombHitResult.Explode;
        }

        public void OnBombBlast(BombBlastHit hit)
        {
            if (state == ExplosiveBarrelState.Exploded || barrelBody == null)
            {
                return;
            }

            Vector3 direction = transform.position - hit.Origin;
            if (horizontalPushOnly)
            {
                direction.y = 0f;
            }

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector3.forward;
            }

            float distanceFactor = hit.Radius > 0.001f
                ? 1f - Mathf.Clamp01(hit.Distance / hit.Radius)
                : 1f;
            barrelBody.AddForce(
                direction.normalized * shockwavePushImpulse * distanceFactor,
                ForceMode.Impulse);
            state = ExplosiveBarrelState.Moving;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (state == ExplosiveBarrelState.Exploded ||
                minimumFallHeight <= 0f ||
                previousVerticalVelocity >= 0f)
            {
                return;
            }

            bool hitGroundLikeSurface = false;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y >= minimumGroundNormalY)
                {
                    hitGroundLikeSurface = true;
                    break;
                }
            }

            if (!hitGroundLikeSurface)
            {
                return;
            }

            float gravity = Mathf.Abs(Physics.gravity.y);
            float requiredImpactSpeed = Mathf.Sqrt(
                2f * gravity * minimumFallHeight);
            if (-previousVerticalVelocity >= requiredImpactSpeed)
            {
                Explode(BarrelExplosionCause.HighFall);
            }
        }

        [ContextMenu("Explode Barrel")]
        public void Explode()
        {
            Explode(BarrelExplosionCause.External);
        }

        public void Explode(BarrelExplosionCause cause)
        {
            if (state == ExplosiveBarrelState.Exploded)
            {
                return;
            }

            state = ExplosiveBarrelState.Exploded;
            LastExplosionCause = cause;
            Vector3 center = transform.position;

            foreach (Renderer targetRenderer in renderersToHide)
            {
                if (targetRenderer != null)
                {
                    targetRenderer.enabled = false;
                }
            }

            foreach (Collider targetCollider in collidersToDisable)
            {
                if (targetCollider != null)
                {
                    targetCollider.enabled = false;
                }
            }

            barrelBody.linearVelocity = Vector3.zero;
            barrelBody.angularVelocity = Vector3.zero;
            barrelBody.isKinematic = true;

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

            TriggerNearbyBarrels(center);
            onExploded?.Invoke();
        }

        private void TriggerNearbyBarrels(Vector3 center)
        {
            if (chainReactionRadius <= 0f)
            {
                return;
            }

            Collider[] nearby = Physics.OverlapSphere(
                center,
                chainReactionRadius,
                chainReactionLayers,
                QueryTriggerInteraction.Collide);
            HashSet<ExplosiveBarrel> uniqueBarrels =
                new HashSet<ExplosiveBarrel>();

            foreach (Collider nearbyCollider in nearby)
            {
                ExplosiveBarrel barrel =
                    nearbyCollider.GetComponentInParent<ExplosiveBarrel>();
                if (barrel != null && barrel != this)
                {
                    uniqueBarrels.Add(barrel);
                }
            }

            foreach (ExplosiveBarrel barrel in uniqueBarrels)
            {
                barrel.Explode(BarrelExplosionCause.ChainReaction);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = debugBlastColor;
            Gizmos.DrawWireSphere(transform.position, blastRadius);
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, chainReactionRadius);
        }
    }
}
