using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;

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
        [SerializeField]
        private Color debugBlastColor =
            new Color(1f, 0.25f, 0f, 0.35f);

        [Header("Events")]
        [SerializeField] private UnityEvent onExploded;

        private Rigidbody bombBody;
        private Collider[] bombColliders;
        private bool[] initialColliderEnabledStates;
        private GameObject owner;
        private bool hasSentCompletion;
        private float remainingLifetime;
        private bool initialUseGravity;
        private bool initialIsKinematic;
        private Transform captureAnchor;
        private Vector3 scaleBeforeCapture;

        [SerializeField, HideInInspector]
        private BombProjectileState state = BombProjectileState.Flying;
        [SerializeField, HideInInspector]
        private BombOwnerType ownerType = BombOwnerType.Environment;

        /// <summary>Raised only when this bomb actually produces an explosion.</summary>
        public event Action<BombProjectile> Exploded;
        /// <summary>
        /// Raised for every terminal outcome: explosion, disarm, or destruction.
        /// Throwers should use this event to release inventory locks.
        /// </summary>
        public event Action<BombProjectile> Completed;
        public float BlastRadius => blastRadius;
        public bool HasExploded => state == BombProjectileState.Exploding;
        public GameObject Owner => owner;
        public BombOwnerType OwnerType => ownerType;
        public BombProjectileState State => state;
        public Vector3 Velocity => bombBody != null
            ? bombBody.linearVelocity
            : Vector3.zero;
        public float RemainingLifetime => remainingLifetime;

        private void Awake()
        {
            bombBody = GetComponent<Rigidbody>();
            bombColliders = GetComponentsInChildren<Collider>();
            initialColliderEnabledStates = new bool[bombColliders.Length];
            for (int i = 0; i < bombColliders.Length; i++)
            {
                initialColliderEnabledStates[i] = bombColliders[i].enabled;
            }
            initialUseGravity = bombBody.useGravity;
            initialIsKinematic = bombBody.isKinematic;
            remainingLifetime = maximumLifetime;
            state = BombProjectileState.Flying;
        }

        private void Update()
        {
            if (state != BombProjectileState.Flying || maximumLifetime <= 0f)
            {
                return;
            }

            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f)
            {
                Explode();
            }
        }

        private void LateUpdate()
        {
            if (state != BombProjectileState.Captured ||
                captureAnchor == null)
            {
                return;
            }

            // Follow without parenting. Parenting below a non-uniformly scaled
            // and rotated pipe hierarchy can shear a sphere into an ellipsoid.
            transform.SetPositionAndRotation(
                captureAnchor.position,
                captureAnchor.rotation);
            transform.localScale = scaleBeforeCapture;
        }

        public void Launch(Vector3 velocity, GameObject throwingOwner)
        {
            BombOwnerType inferredOwnerType = throwingOwner != null
                ? BombOwnerType.Player
                : BombOwnerType.Environment;
            Launch(velocity, throwingOwner, inferredOwnerType);
        }

        public void Launch(
            Vector3 velocity,
            GameObject throwingOwner,
            BombOwnerType sourceType)
        {
            owner = throwingOwner;
            ownerType = sourceType;
            state = BombProjectileState.Flying;
            captureAnchor = null;
            remainingLifetime = maximumLifetime;
            transform.SetParent(null, true);
            RestoreBombColliders();
            bombBody.isKinematic = initialIsKinematic;
            bombBody.useGravity = initialUseGravity;
            bombBody.linearVelocity = velocity;

            Collider[] ownerColliders;
            if (owner == null)
            {
                ownerColliders = Array.Empty<Collider>();
            }
            else if (ownerType == BombOwnerType.Player)
            {
                // The player consists of several child colliders, all of which
                // must be ignored by the freshly thrown bomb.
                ownerColliders = owner.GetComponentsInChildren<Collider>();
            }
            else
            {
                // A generator may be placed on a larger mechanism hierarchy.
                // Never ignore every descendant here, or unrelated rocks and
                // iron blocks below that hierarchy would become non-colliding.
                ownerColliders = owner.GetComponents<Collider>();
            }

            IgnoreCollisionsWith(ownerColliders);
        }

        public void IgnoreCollisionsWith(IEnumerable<Collider> colliders)
        {
            if (bombColliders == null || colliders == null)
            {
                return;
            }

            foreach (Collider ownerCollider in colliders)
            {
                if (ownerCollider == null)
                {
                    continue;
                }

                foreach (Collider projectileCollider in bombColliders)
                {
                    if (projectileCollider != null &&
                        ownerCollider != projectileCollider)
                    {
                        Physics.IgnoreCollision(
                            projectileCollider,
                            ownerCollider,
                            true);
                    }
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0)
            {
                HandleContact(
                    collision.collider,
                    transform.position,
                    -Velocity.normalized,
                    collision.relativeVelocity,
                    true);
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            HandleContact(
                collision.collider,
                contact.point,
                contact.normal,
                collision.relativeVelocity,
                true);
        }

        private void OnTriggerEnter(Collider other)
        {
            Vector3 point = other.ClosestPoint(transform.position);
            Vector3 normal = Velocity.sqrMagnitude > 0.001f
                ? -Velocity.normalized
                : Vector3.up;
            HandleContact(other, point, normal, Velocity, false);
        }

        private void HandleContact(
            Collider contactedCollider,
            Vector3 point,
            Vector3 normal,
            Vector3 relativeVelocity,
            bool isSolidCollision)
        {
            if (state != BombProjectileState.Flying ||
                contactedCollider == null ||
                IsOwnerCollider(contactedCollider))
            {
                return;
            }

            BombDirectHit hit = new BombDirectHit(
                this,
                contactedCollider,
                point,
                normal,
                relativeVelocity,
                isSolidCollision);
            BombHitResult result = BombInteractionRouter.Route(hit);

            switch (result)
            {
                case BombHitResult.ContinueFlight:
                case BombHitResult.Capture:
                    return;
                case BombHitResult.Explode:
                    Explode();
                    return;
                case BombHitResult.Disarm:
                    Disarm();
                    return;
                case BombHitResult.Ignore:
                    if (explodeOnAnyCollision && isSolidCollision)
                    {
                        Explode();
                    }
                    return;
            }
        }

        private bool IsOwnerCollider(Collider contactedCollider)
        {
            if (owner == null || contactedCollider == null)
            {
                return false;
            }

            if (contactedCollider.gameObject == owner)
            {
                return true;
            }

            return ownerType == BombOwnerType.Player &&
                   contactedCollider.transform.IsChildOf(owner.transform);
        }

        public bool Capture(Transform anchor)
        {
            if (state != BombProjectileState.Flying)
            {
                return false;
            }

            state = BombProjectileState.Captured;
            captureAnchor = anchor;
            scaleBeforeCapture = transform.lossyScale;
            transform.SetParent(null, true);
            transform.localScale = scaleBeforeCapture;
            bombBody.linearVelocity = Vector3.zero;
            bombBody.angularVelocity = Vector3.zero;
            bombBody.useGravity = false;
            bombBody.isKinematic = true;
            SetBombCollidersEnabled(false);

            if (captureAnchor != null)
            {
                transform.SetPositionAndRotation(
                    captureAnchor.position,
                    captureAnchor.rotation);
            }

            return true;
        }

        public bool Release(
            Vector3 position,
            Quaternion rotation,
            Vector3 velocity)
        {
            if (state != BombProjectileState.Captured)
            {
                return false;
            }

            captureAnchor = null;
            transform.SetParent(null, true);
            transform.localScale = scaleBeforeCapture;
            transform.SetPositionAndRotation(position, rotation);
            bombBody.isKinematic = initialIsKinematic;
            bombBody.useGravity = initialUseGravity;
            RestoreBombColliders();
            bombBody.linearVelocity = velocity;
            state = BombProjectileState.Flying;
            Physics.SyncTransforms();
            return true;
        }

        private void SetBombCollidersEnabled(bool value)
        {
            foreach (Collider projectileCollider in bombColliders)
            {
                if (projectileCollider != null)
                {
                    projectileCollider.enabled = value;
                }
            }
        }

        private void RestoreBombColliders()
        {
            for (int i = 0; i < bombColliders.Length; i++)
            {
                if (bombColliders[i] != null)
                {
                    bombColliders[i].enabled =
                        initialColliderEnabledStates[i];
                }
            }
        }

        [ContextMenu("Disarm Bomb (No Explosion)")]
        public void Disarm()
        {
            if (IsTerminalState())
            {
                return;
            }

            state = BombProjectileState.Disarmed;
            SendCompletionOnce();
            Destroy(gameObject);
        }

        public void Explode()
        {
            if (IsTerminalState())
            {
                return;
            }

            state = BombProjectileState.Exploding;
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
            Exploded?.Invoke(this);
            SendCompletionOnce();
            Destroy(gameObject);
        }

        private bool IsTerminalState()
        {
            return state == BombProjectileState.Exploding ||
                   state == BombProjectileState.Disarmed ||
                   state == BombProjectileState.Destroyed;
        }

        private void OnDestroy()
        {
            // Also unlock the thrower if this projectile is removed by an
            // external system, hierarchy cleanup, or an unexpected lifetime.
            if (state != BombProjectileState.Exploding &&
                state != BombProjectileState.Disarmed)
            {
                state = BombProjectileState.Destroyed;
            }

            SendCompletionOnce();
        }

        private void SendCompletionOnce()
        {
            if (hasSentCompletion)
            {
                return;
            }

            hasSentCompletion = true;
            Completed?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = debugBlastColor;
            Gizmos.DrawWireSphere(transform.position, blastRadius);
        }
    }
}
