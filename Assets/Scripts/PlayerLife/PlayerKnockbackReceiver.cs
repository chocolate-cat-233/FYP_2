using UnityEngine;

namespace SuperFinalFuse.PlayerLife
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(pl_Move),
        typeof(pl_stateManager),
        typeof(CapsuleCollider))]
    public sealed class PlayerKnockbackReceiver : MonoBehaviour
    {
        private const float MovementEpsilon = 0.0001f;

        [Header("References")]
        [SerializeField] private pl_Move playerMovement;
        [SerializeField] private Pl_Interact playerInteraction;
        [SerializeField] private pl_stateManager stateManager;
        [SerializeField] private PlayerState knockedBackPlayerState;
        [SerializeField] private CapsuleCollider playerCollider;
        [SerializeField] private PlayerHealth playerHealth;

        [Header("Collision")]
        [SerializeField] private LayerMask wallMask;
        [SerializeField] private LayerMask movableMask;
        [Min(0f)]
        [SerializeField] private float collisionSkin = 0.02f;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
        private Vector3 initialVelocity;
        private Vector3 currentVelocity;
        private float duration;
        private float remainingTime;
        private PlayerState stateBeforeKnockback;
        private Transform ignoredSourceTransform;

        public bool IsKnockedBack { get; private set; }
        public Vector3 CurrentVelocity => currentVelocity;

        private void Awake()
        {
            if (playerMovement == null)
            {
                playerMovement = GetComponent<pl_Move>();
            }

            if (playerInteraction == null)
            {
                playerInteraction = GetComponent<Pl_Interact>();
            }

            if (stateManager == null)
            {
                stateManager = GetComponent<pl_stateManager>();
            }

            if (playerCollider == null)
            {
                playerCollider = GetComponent<CapsuleCollider>();
            }

            if (playerHealth == null)
            {
                playerHealth = GetComponent<PlayerHealth>();
            }
        }

        private void OnEnable()
        {
            if (!ValidateConfiguration())
            {
                enabled = false;
                return;
            }

            stateManager.StateChanged += HandlePlayerStateChanged;
            playerHealth.Died += HandlePlayerDied;
        }

        private void OnDisable()
        {
            if (stateManager != null)
            {
                stateManager.StateChanged -= HandlePlayerStateChanged;
            }

            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDied;
            }

            StopKnockback(true);
        }

        private void LateUpdate()
        {
            if (!IsKnockedBack)
            {
                return;
            }

            if (playerHealth.IsDead)
            {
                StopKnockback(true);
                return;
            }

            float normalizedRemaining = duration > 0f
                ? Mathf.Clamp01(remainingTime / duration)
                : 0f;
            currentVelocity = initialVelocity * normalizedRemaining;
            bool hitWall = MoveWithCollision(
                currentVelocity * Time.deltaTime);

            remainingTime -= Time.deltaTime;
            if (hitWall || remainingTime <= 0f)
            {
                StopKnockback(true);
            }
        }

        public void ApplyKnockback(
            Vector3 initialHorizontalVelocity,
            float knockbackDuration,
            Transform ignoredSource)
        {
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(
                initialHorizontalVelocity,
                Vector3.up);
            if (!isActiveAndEnabled ||
                playerHealth.IsDead ||
                horizontalVelocity.sqrMagnitude <=
                    MovementEpsilon * MovementEpsilon ||
                knockbackDuration <= 0f)
            {
                Debug.LogError(
                    $"{nameof(PlayerKnockbackReceiver)} on '{name}' " +
                    "rejected invalid knockback input or is not active.",
                    this);
                return;
            }

            playerInteraction.SetInputBlocked(true);
            playerMovement.CancelMoveToTarget();

            if (!IsKnockedBack)
            {
                stateBeforeKnockback = stateManager.CurrentState;
                if (stateBeforeKnockback == null ||
                    stateBeforeKnockback == knockedBackPlayerState)
                {
                    stateBeforeKnockback = null;
                    Debug.LogError(
                        $"{nameof(PlayerKnockbackReceiver)} on '{name}' " +
                        "cannot record a valid state to restore after knockback.",
                        this);
                    playerInteraction.SetInputBlocked(false);
                    return;
                }

                IsKnockedBack = true;
                if (!stateManager.ChangeState(knockedBackPlayerState))
                {
                    IsKnockedBack = false;
                    stateBeforeKnockback = null;
                    Debug.LogError(
                        $"{nameof(PlayerKnockbackReceiver)} on '{name}' " +
                        $"could not enter '{knockedBackPlayerState.name}'.",
                        this);
                    playerInteraction.SetInputBlocked(false);
                    return;
                }
            }

            initialVelocity = horizontalVelocity;
            currentVelocity = horizontalVelocity;
            duration = knockbackDuration;
            remainingTime = knockbackDuration;
            Rigidbody ignoredSourceBody = ignoredSource != null
                ? ignoredSource.GetComponentInParent<Rigidbody>()
                : null;
            ignoredSourceTransform = ignoredSourceBody != null
                ? ignoredSourceBody.transform
                : ignoredSource;
        }

        private bool MoveWithCollision(Vector3 displacement)
        {
            float distance = displacement.magnitude;
            if (distance <= MovementEpsilon)
            {
                return false;
            }

            Physics.SyncTransforms();
            GetWorldCapsule(
                out Vector3 capsuleTop,
                out Vector3 capsuleBottom,
                out float capsuleRadius);

            Vector3 direction = displacement / distance;
            int hitCount = Physics.CapsuleCastNonAlloc(
                capsuleTop,
                capsuleBottom,
                Mathf.Max(
                    capsuleRadius - collisionSkin,
                    MovementEpsilon),
                direction,
                hitBuffer,
                distance + collisionSkin,
                wallMask.value | movableMask.value,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null ||
                    IsIgnoredCollider(hit.collider) ||
                    hit.distance >= nearestDistance)
                {
                    continue;
                }

                Vector3 horizontalNormal = Vector3.ProjectOnPlane(
                    hit.normal,
                    Vector3.up);
                if (horizontalNormal.sqrMagnitude <=
                        MovementEpsilon * MovementEpsilon ||
                    Vector3.Dot(
                        direction,
                        horizontalNormal.normalized) >= -MovementEpsilon)
                {
                    continue;
                }

                nearestDistance = hit.distance;
            }

            if (float.IsPositiveInfinity(nearestDistance))
            {
                transform.position += displacement;
                return false;
            }

            float allowedDistance = Mathf.Clamp(
                nearestDistance - collisionSkin,
                0f,
                distance);
            transform.position += direction * allowedDistance;
            return true;
        }

        private void GetWorldCapsule(
            out Vector3 top,
            out Vector3 bottom,
            out float radius)
        {
            Transform colliderTransform = playerCollider.transform;
            Vector3 scale = colliderTransform.lossyScale;
            float horizontalScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.z));
            radius = playerCollider.radius * horizontalScale;
            float height = Mathf.Max(
                playerCollider.height * Mathf.Abs(scale.y),
                radius * 2f);
            Vector3 center = colliderTransform.TransformPoint(
                playerCollider.center);
            float halfSegment = Mathf.Max(height * 0.5f - radius, 0f);
            Vector3 offset = colliderTransform.up * halfSegment;
            top = center + offset;
            bottom = center - offset;
        }

        private bool IsIgnoredCollider(Collider hitCollider)
        {
            Transform hitTransform = hitCollider.transform;
            if (hitTransform == transform ||
                hitTransform.IsChildOf(transform))
            {
                return true;
            }

            return ignoredSourceTransform != null &&
                   (hitTransform == ignoredSourceTransform ||
                    hitTransform.IsChildOf(ignoredSourceTransform));
        }

        private void StopKnockback(bool restorePlayerState)
        {
            if (!IsKnockedBack)
            {
                return;
            }

            PlayerState previousState = stateBeforeKnockback;
            IsKnockedBack = false;
            initialVelocity = Vector3.zero;
            currentVelocity = Vector3.zero;
            duration = 0f;
            remainingTime = 0f;
            stateBeforeKnockback = null;
            ignoredSourceTransform = null;
            playerInteraction?.SetInputBlocked(false);

            if (restorePlayerState &&
                stateManager != null &&
                stateManager.CurrentState == knockedBackPlayerState &&
                previousState != null)
            {
                stateManager.ChangeState(previousState);
            }
        }

        private void HandlePlayerStateChanged(PlayerState newState)
        {
            if (IsKnockedBack && newState != knockedBackPlayerState)
            {
                StopKnockback(false);
            }
        }

        private void HandlePlayerDied()
        {
            StopKnockback(true);
        }

        private bool ValidateConfiguration()
        {
            if (playerMovement == null ||
                playerInteraction == null ||
                stateManager == null ||
                knockedBackPlayerState == null ||
                playerCollider == null ||
                playerHealth == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerKnockbackReceiver)} on '{name}' has " +
                    "missing references.",
                    this);
                return false;
            }

            if (playerCollider.direction != 1)
            {
                Debug.LogError(
                    $"{nameof(PlayerKnockbackReceiver)} on '{name}' requires " +
                    "a Y-axis CapsuleCollider.",
                    this);
                return false;
            }

            if ((wallMask.value | movableMask.value) == 0)
            {
                Debug.LogError(
                    $"{nameof(PlayerKnockbackReceiver)} on '{name}' requires " +
                    "at least one blocking layer.",
                    this);
                return false;
            }

            return true;
        }

        private void OnValidate()
        {
            collisionSkin = Mathf.Max(0f, collisionSkin);
        }
    }
}
