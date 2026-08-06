using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.Enemies
{
    public enum ChargingEnemyState
    {
        Waiting,
        ApproachStepOne,
        ApproachStepTwo,
        Pause,
        Charge,
        ChargeRecovery,
        ShieldKnockback,
        Dead
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class ChargingEnemyController : MonoBehaviour,
        IBombDirectHitReceiver
    {
        private const float DirectionEpsilon = 0.0001f;
        private const float TargetSearchInterval = 0.5f;

        [Header("References")]
        [SerializeField] private Rigidbody enemyBody;
        [SerializeField] private Transform target;
        [SerializeField] private Collider attackArea;
        [SerializeField] private EnemyChargeHitbox chargeHitbox;
        [SerializeField] private Collider[] shieldColliders =
            Array.Empty<Collider>();

        [Header("Approach")]
        [Min(0f)]
        [SerializeField] private float approachSpeed = 1.5f;
        [SerializeField] private Vector2 approachDurationRange =
            new Vector2(0.6f, 1.2f);
        [Min(1f)]
        [SerializeField] private float approachDurationMultiplier = 1.25f;
        [SerializeField] private Vector2 pauseDurationRange =
            new Vector2(0.4f, 0.8f);
        [Min(0f)]
        [SerializeField] private float turnSpeed = 360f;

        [Header("Charge")]
        [Min(0.01f)]
        [SerializeField] private float chargeSpeed = 8f;
        [Min(0.01f)]
        [SerializeField] private float chargeDistance = 6f;
        [Min(0)]
        [SerializeField] private int chargeDamage = 1;
        [Min(0.01f)]
        [SerializeField] private float playerKnockbackSpeed = 6f;
        [Min(0.01f)]
        [SerializeField] private float playerKnockbackDuration = 0.35f;
        [Min(0.01f)]
        [SerializeField] private float damageWaitDuration = 1f;
        [Min(0.01f)]
        [SerializeField] private float chargeRecoveryDuration = 0.5f;

        [Header("Shield")]
        [Range(0f, 90f)]
        [SerializeField] private float shieldFrontAngle = 70f;
        [Min(0.01f)]
        [SerializeField] private float shieldKnockbackDistance = 2f;
        [Min(0.01f)]
        [SerializeField] private float shieldKnockbackDuration = 0.3f;

        [Header("Death")]
        [SerializeField] private float deathWorldY = -10f;
        [SerializeField] private UnityEvent onDied = new UnityEvent();

        private readonly HashSet<PlayerHealth> playersHitThisCharge =
            new HashSet<PlayerHealth>();
        private readonly Vector3[] containmentSamples = new Vector3[32];
        private Collider enemyCollider;
        private Collider[] targetColliders = Array.Empty<Collider>();
        private PlayerHealth targetHealth;
        private float stateTimeRemaining;
        private float movementDistance;
        private float movementSafetyTime;
        private float nextTargetSearchTime;
        private float attackResumeTime;
        private Vector3 lastMovementPosition;
        private Vector3 shieldKnockbackDirection;
        private bool isForcingShieldKnockback;
        private bool shieldBodyWasKinematic;
        private bool shieldBodyUsedGravity;
        private bool shieldBodyDetectedCollisions;
        private bool isConfigured;

        public ChargingEnemyState State { get; private set; } =
            ChargingEnemyState.Waiting;
        public bool IsCharging => State == ChargingEnemyState.Charge;
        public bool IsPlayerInsideAttackArea { get; private set; }
        public bool IsEnemyInsideAttackArea { get; private set; }
        public Vector3 ChargeDirection { get; private set; }
        public int ChargeSequenceId { get; private set; }

        public event Action<ChargingEnemyState> StateChanged;
        public event Action Died;

        private void Awake()
        {
            if (enemyBody == null)
            {
                enemyBody = GetComponent<Rigidbody>();
            }

            enemyCollider = GetComponent<Collider>();

            if (chargeHitbox == null)
            {
                chargeHitbox = GetComponentInChildren<EnemyChargeHitbox>(true);
            }

            if (chargeHitbox != null)
            {
                chargeHitbox.SetController(this);
            }

            isConfigured = ValidateConfiguration();
            SetTarget(target);
        }

        private void OnEnable()
        {
            if (!isConfigured)
            {
                enabled = false;
                return;
            }

            State = ChargingEnemyState.Waiting;
            IsPlayerInsideAttackArea = false;
            IsEnemyInsideAttackArea = false;
            ChargeDirection = Vector3.zero;
            attackResumeTime = 0f;
            playersHitThisCharge.Clear();
            StopHorizontalMovement();
        }

        private void OnDisable()
        {
            RestoreBodyAfterShieldKnockback();
            if (enemyBody != null && !enemyBody.isKinematic)
            {
                StopHorizontalMovement();
            }
        }

        private void Update()
        {
            CheckDeathHeight();
        }

        private void FixedUpdate()
        {
            if (State == ChargingEnemyState.Dead || CheckDeathHeight())
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;
            if (State == ChargingEnemyState.ShieldKnockback)
            {
                RefreshAttackAreaState();
                UpdateShieldKnockback(deltaTime);
                return;
            }

            IsEnemyInsideAttackArea =
                IsEnemyColliderFullyInsideAttackArea(Vector3.zero);
            if (!IsEnemyInsideAttackArea)
            {
                IsPlayerInsideAttackArea = false;
                EnterWaiting();
                return;
            }

            if (!HasUsableTarget())
            {
                IsPlayerInsideAttackArea = false;
                EnterWaiting();
                TryAcquirePlayerTarget();
                return;
            }

            IsPlayerInsideAttackArea = IsTargetOverlappingAttackArea();
            if (!IsPlayerInsideAttackArea)
            {
                EnterWaiting();
                return;
            }

            if (Time.time < attackResumeTime)
            {
                EnterWaiting();
                return;
            }

            if (State == ChargingEnemyState.Waiting)
            {
                BeginApproachStep(ChargingEnemyState.ApproachStepOne);
            }

            switch (State)
            {
                case ChargingEnemyState.ApproachStepOne:
                case ChargingEnemyState.ApproachStepTwo:
                    UpdateApproach(deltaTime);
                    break;
                case ChargingEnemyState.Pause:
                    UpdatePause(deltaTime);
                    break;
                case ChargingEnemyState.Charge:
                    UpdateCharge(deltaTime);
                    break;
                case ChargingEnemyState.ChargeRecovery:
                    UpdateChargeRecovery(deltaTime);
                    break;
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            targetHealth = target != null
                ? target.GetComponentInParent<PlayerHealth>()
                : null;

            if (targetHealth != null)
            {
                target = targetHealth.transform;
            }

            targetColliders = target != null
                ? target.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
        }

        private void RefreshAttackAreaState()
        {
            IsEnemyInsideAttackArea =
                IsEnemyColliderFullyInsideAttackArea(Vector3.zero);
            IsPlayerInsideAttackArea =
                IsEnemyInsideAttackArea &&
                HasUsableTarget() &&
                IsTargetOverlappingAttackArea();
        }

        private bool IsTargetOverlappingAttackArea()
        {
            if (attackArea == null ||
                !attackArea.enabled ||
                target == null)
            {
                return false;
            }

            if (targetColliders == null || targetColliders.Length == 0)
            {
                targetColliders =
                    target.GetComponentsInChildren<Collider>(true);
            }

            Bounds areaBounds = attackArea.bounds;
            for (int i = 0; i < targetColliders.Length; i++)
            {
                Collider targetCollider = targetColliders[i];
                if (targetCollider == null ||
                    targetCollider.isTrigger ||
                    !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy ||
                    !areaBounds.Intersects(targetCollider.bounds))
                {
                    continue;
                }

                if (Physics.ComputePenetration(
                        attackArea,
                        attackArea.transform.position,
                        attackArea.transform.rotation,
                        targetCollider,
                        targetCollider.transform.position,
                        targetCollider.transform.rotation,
                        out _,
                        out _))
                {
                    return true;
                }

                Vector3 targetPoint = targetCollider.ClosestPoint(
                    areaBounds.center);
                if (IsPointInsideAttackArea(targetPoint))
                {
                    return true;
                }
            }

            return IsPointInsideAttackArea(target.position);
        }

        private bool IsEnemyColliderFullyInsideAttackArea(
            Vector3 positionOffset)
        {
            if (attackArea == null ||
                !attackArea.enabled ||
                enemyCollider == null ||
                !enemyCollider.enabled)
            {
                return false;
            }

            int sampleCount = GetEnemyContainmentSamples(positionOffset);
            for (int i = 0; i < sampleCount; i++)
            {
                if (!IsPointInsideAttackArea(containmentSamples[i]))
                {
                    return false;
                }
            }

            return sampleCount > 0;
        }

        private int GetEnemyContainmentSamples(Vector3 positionOffset)
        {
            if (enemyCollider is CapsuleCollider capsule)
            {
                return GetCapsuleContainmentSamples(
                    capsule,
                    positionOffset);
            }

            if (enemyCollider is BoxCollider box)
            {
                return GetBoxContainmentSamples(box, positionOffset);
            }

            Bounds bounds = enemyCollider.bounds;
            Vector3 center = bounds.center + positionOffset;
            Vector3 extents = bounds.extents;
            int count = 0;
            containmentSamples[count++] = center;
            containmentSamples[count++] = center + Vector3.right * extents.x;
            containmentSamples[count++] = center - Vector3.right * extents.x;
            containmentSamples[count++] = center + Vector3.up * extents.y;
            containmentSamples[count++] = center - Vector3.up * extents.y;
            containmentSamples[count++] = center + Vector3.forward * extents.z;
            containmentSamples[count++] = center - Vector3.forward * extents.z;
            return count;
        }

        private int GetCapsuleContainmentSamples(
            CapsuleCollider capsule,
            Vector3 positionOffset)
        {
            Transform colliderTransform = capsule.transform;
            Vector3 scale = colliderTransform.lossyScale;
            Vector3 localAxis = capsule.direction switch
            {
                0 => Vector3.right,
                2 => Vector3.forward,
                _ => Vector3.up
            };
            float axisScale = capsule.direction switch
            {
                0 => Mathf.Abs(scale.x),
                2 => Mathf.Abs(scale.z),
                _ => Mathf.Abs(scale.y)
            };
            float perpendicularScale = capsule.direction switch
            {
                0 => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
                2 => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
                _ => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
            };
            Vector3 axis = colliderTransform.TransformDirection(
                localAxis).normalized;
            float radius = capsule.radius * perpendicularScale;
            float height = Mathf.Max(
                capsule.height * axisScale,
                radius * 2f);
            float halfSegment = Mathf.Max(height * 0.5f - radius, 0f);
            Vector3 center =
                colliderTransform.TransformPoint(capsule.center) +
                positionOffset;
            Vector3 top = center + axis * halfSegment;
            Vector3 bottom = center - axis * halfSegment;
            Vector3 referenceAxis =
                Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f
                    ? Vector3.right
                    : Vector3.up;
            Vector3 radialA = Vector3.Cross(axis, referenceAxis).normalized;
            Vector3 radialB = Vector3.Cross(axis, radialA).normalized;

            int count = 0;
            containmentSamples[count++] = center;
            containmentSamples[count++] = top + axis * radius;
            containmentSamples[count++] = bottom - axis * radius;
            const int RadialSampleCount = 8;
            for (int i = 0; i < RadialSampleCount; i++)
            {
                float angle = Mathf.PI * 2f * i / RadialSampleCount;
                Vector3 radialDirection =
                    radialA * Mathf.Cos(angle) +
                    radialB * Mathf.Sin(angle);
                Vector3 radialOffset = radialDirection * radius;
                containmentSamples[count++] = top + radialOffset;
                containmentSamples[count++] = bottom + radialOffset;
            }

            return count;
        }

        private int GetBoxContainmentSamples(
            BoxCollider box,
            Vector3 positionOffset)
        {
            Transform colliderTransform = box.transform;
            Vector3 halfSize = box.size * 0.5f;
            int count = 0;
            containmentSamples[count++] =
                colliderTransform.TransformPoint(box.center) +
                positionOffset;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localPoint = box.center + new Vector3(
                            halfSize.x * x,
                            halfSize.y * y,
                            halfSize.z * z);
                        containmentSamples[count++] =
                            colliderTransform.TransformPoint(localPoint) +
                            positionOffset;
                    }
                }
            }

            return count;
        }

        private bool IsPointInsideAttackArea(Vector3 worldPoint)
        {
            const float ContainmentTolerance = 0.002f;
            Vector3 closestPoint = attackArea.ClosestPoint(worldPoint);
            return (closestPoint - worldPoint).sqrMagnitude <=
                   ContainmentTolerance * ContainmentTolerance;
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (State == ChargingEnemyState.Dead ||
                State == ChargingEnemyState.ShieldKnockback ||
                hit.Projectile == null ||
                hit.OwnerType != BombOwnerType.Player ||
                !hit.IsSolidCollision ||
                !IsShieldCollider(hit.Collider))
            {
                return BombHitResult.Ignore;
            }

            Vector3 incomingVelocity = Vector3.ProjectOnPlane(
                hit.Projectile.Velocity,
                Vector3.up);
            if (incomingVelocity.sqrMagnitude <= DirectionEpsilon)
            {
                incomingVelocity = Vector3.ProjectOnPlane(
                    hit.RelativeVelocity,
                    Vector3.up);
            }

            Vector3 forward = GetHorizontalForward();
            float minimumDot = Mathf.Cos(shieldFrontAngle * Mathf.Deg2Rad);
            if (incomingVelocity.sqrMagnitude <= DirectionEpsilon ||
                Vector3.Dot(
                    incomingVelocity.normalized,
                    -forward) < minimumDot)
            {
                return BombHitResult.Ignore;
            }

            BeginShieldKnockback();
            return BombHitResult.Explode;
        }

        internal bool TryHitPlayer(Collider other)
        {
            RefreshAttackAreaState();
            if (State == ChargingEnemyState.Waiting ||
                State == ChargingEnemyState.Dead ||
                State == ChargingEnemyState.ShieldKnockback ||
                !IsPlayerInsideAttackArea ||
                !IsEnemyInsideAttackArea ||
                other == null)
            {
                return false;
            }

            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null ||
                (targetHealth != null && health != targetHealth) ||
                health.IsDead)
            {
                return false;
            }

            if (IsCharging && playersHitThisCharge.Contains(health))
            {
                return true;
            }

            bool damaged = health.TakeDamage(new PlayerDamage(
                chargeDamage,
                PlayerDamageType.EnemyCharge,
                gameObject));
            if (!damaged)
            {
                return false;
            }

            if (IsCharging)
            {
                playersHitThisCharge.Add(health);
            }

            if (!health.IsDead)
            {
                PlayerKnockbackReceiver knockbackReceiver =
                    health.GetComponentInParent<PlayerKnockbackReceiver>();
                if (knockbackReceiver == null)
                {
                    Debug.LogError(
                        $"{nameof(ChargingEnemyController)} on '{name}' " +
                        $"damaged '{health.name}', but the player has no " +
                        $"{nameof(PlayerKnockbackReceiver)} component.",
                        this);
                }
                else
                {
                    Vector3 initialVelocity =
                        GetHorizontalForward() * playerKnockbackSpeed;
                    knockbackReceiver.ApplyKnockback(
                        initialVelocity,
                        playerKnockbackDuration,
                        transform);
                }
            }

            BeginDamageWait();

            return true;
        }

        private void UpdateApproach(float deltaTime)
        {
            Vector3 direction = GetDirectionToTarget();
            RotateTowards(direction, deltaTime);
            SetHorizontalVelocity(direction * approachSpeed);

            stateTimeRemaining -= deltaTime;
            if (stateTimeRemaining > 0f)
            {
                return;
            }

            if (State == ChargingEnemyState.ApproachStepOne)
            {
                BeginApproachStep(ChargingEnemyState.ApproachStepTwo);
            }
            else
            {
                BeginPause();
            }
        }

        private void UpdatePause(float deltaTime)
        {
            StopHorizontalMovement();
            RotateTowards(GetDirectionToTarget(), deltaTime);
            stateTimeRemaining -= deltaTime;
            if (stateTimeRemaining <= 0f)
            {
                BeginCharge();
            }
        }

        private void UpdateCharge(float deltaTime)
        {
            AccumulateMovementDistance(ChargeDirection);
            movementSafetyTime -= deltaTime;
            if (movementDistance >= chargeDistance || movementSafetyTime <= 0f)
            {
                BeginChargeRecovery();
                return;
            }

            TrySetChargeVelocity();
        }

        private bool TrySetChargeVelocity()
        {
            Vector3 chargeVelocity = ChargeDirection * chargeSpeed;
            Vector3 nextStepOffset =
                chargeVelocity * Time.fixedDeltaTime;
            if (!IsEnemyColliderFullyInsideAttackArea(nextStepOffset))
            {
                BeginChargeRecovery();
                return false;
            }

            SetHorizontalVelocity(chargeVelocity);
            return true;
        }

        private void UpdateChargeRecovery(float deltaTime)
        {
            StopHorizontalMovement();
            stateTimeRemaining -= deltaTime;
            if (stateTimeRemaining <= 0f)
            {
                BeginApproachStep(ChargingEnemyState.ApproachStepOne);
            }
        }

        private void UpdateShieldKnockback(float deltaTime)
        {
            float retreatSpeed =
                shieldKnockbackDistance / shieldKnockbackDuration;
            float remainingDistance = Mathf.Max(
                0f,
                shieldKnockbackDistance - movementDistance);
            float stepDistance = Mathf.Min(
                retreatSpeed * deltaTime,
                remainingDistance);

            enemyBody.position +=
                shieldKnockbackDirection * stepDistance;
            movementDistance += stepDistance;
            stateTimeRemaining -= deltaTime;
            if (movementDistance >= shieldKnockbackDistance ||
                stateTimeRemaining <= 0f)
            {
                CompleteShieldKnockback();
            }
        }

        private void BeginApproachStep(ChargingEnemyState approachState)
        {
            stateTimeRemaining = UnityEngine.Random.Range(
                approachDurationRange.x,
                approachDurationRange.y) * approachDurationMultiplier;
            movementDistance = 0f;
            SetState(approachState);
        }

        private void BeginPause()
        {
            stateTimeRemaining = UnityEngine.Random.Range(
                pauseDurationRange.x,
                pauseDurationRange.y);
            StopHorizontalMovement();
            SetState(ChargingEnemyState.Pause);
        }

        private void BeginCharge()
        {
            ChargeDirection = GetDirectionToTarget();
            if (ChargeDirection.sqrMagnitude <= DirectionEpsilon)
            {
                ChargeDirection = GetHorizontalForward();
            }

            RotateImmediately(ChargeDirection);
            movementDistance = 0f;
            lastMovementPosition = enemyBody.position;
            movementSafetyTime = chargeDistance / chargeSpeed + 1f;
            ChargeSequenceId++;
            playersHitThisCharge.Clear();
            SetState(ChargingEnemyState.Charge);
            TrySetChargeVelocity();
        }

        private void BeginChargeRecovery()
        {
            stateTimeRemaining = chargeRecoveryDuration;
            StopHorizontalMovement();
            SetState(ChargingEnemyState.ChargeRecovery);
        }

        private void BeginShieldKnockback()
        {
            shieldKnockbackDirection = -GetHorizontalForward();
            movementDistance = 0f;
            stateTimeRemaining = shieldKnockbackDuration;
            ChargeDirection = Vector3.zero;
            playersHitThisCharge.Clear();
            StopHorizontalMovement();

            shieldBodyWasKinematic = enemyBody.isKinematic;
            shieldBodyUsedGravity = enemyBody.useGravity;
            shieldBodyDetectedCollisions = enemyBody.detectCollisions;
            enemyBody.useGravity = false;
            enemyBody.isKinematic = true;
            enemyBody.detectCollisions = false;
            isForcingShieldKnockback = true;

            SetState(ChargingEnemyState.ShieldKnockback);
        }

        private void CompleteShieldKnockback()
        {
            RestoreBodyAfterShieldKnockback();
            RefreshAttackAreaState();

            if (!IsEnemyInsideAttackArea ||
                !IsPlayerInsideAttackArea ||
                Time.time < attackResumeTime)
            {
                EnterWaiting();
                return;
            }

            BeginApproachStep(ChargingEnemyState.ApproachStepOne);
        }

        private void RestoreBodyAfterShieldKnockback()
        {
            if (!isForcingShieldKnockback || enemyBody == null)
            {
                return;
            }

            enemyBody.isKinematic = shieldBodyWasKinematic;
            enemyBody.useGravity = shieldBodyUsedGravity;
            enemyBody.detectCollisions = shieldBodyDetectedCollisions;
            isForcingShieldKnockback = false;
        }

        private void EnterWaiting()
        {
            if (State == ChargingEnemyState.Dead)
            {
                return;
            }

            RestoreBodyAfterShieldKnockback();
            StopHorizontalMovement();
            ChargeDirection = Vector3.zero;
            stateTimeRemaining = 0f;
            movementDistance = 0f;
            movementSafetyTime = 0f;
            lastMovementPosition = enemyBody != null
                ? enemyBody.position
                : transform.position;
            shieldKnockbackDirection = Vector3.zero;
            playersHitThisCharge.Clear();
            SetState(ChargingEnemyState.Waiting);
        }

        private void BeginDamageWait()
        {
            attackResumeTime = Time.time + damageWaitDuration;
            EnterWaiting();
        }

        private void TryAcquirePlayerTarget()
        {
            if (target != null || Time.time < nextTargetSearchTime)
            {
                return;
            }

            nextTargetSearchTime = Time.time + TargetSearchInterval;
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                SetTarget(player.transform);
            }
        }

        private bool HasUsableTarget()
        {
            return target != null &&
                   target.gameObject.activeInHierarchy &&
                   (targetHealth == null || !targetHealth.IsDead);
        }

        private Vector3 GetDirectionToTarget()
        {
            if (target == null)
            {
                return Vector3.zero;
            }

            Vector3 direction = Vector3.ProjectOnPlane(
                target.position - transform.position,
                Vector3.up);
            return direction.sqrMagnitude > DirectionEpsilon
                ? direction.normalized
                : Vector3.zero;
        }

        private Vector3 GetHorizontalForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                transform.forward,
                Vector3.up);
            return forward.normalized;
        }

        private void RotateTowards(Vector3 direction, float deltaTime)
        {
            if (direction.sqrMagnitude <= DirectionEpsilon || turnSpeed <= 0f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(
                direction,
                Vector3.up);
            enemyBody.MoveRotation(Quaternion.RotateTowards(
                enemyBody.rotation,
                targetRotation,
                turnSpeed * deltaTime));
        }

        private void RotateImmediately(Vector3 direction)
        {
            if (direction.sqrMagnitude > DirectionEpsilon)
            {
                enemyBody.MoveRotation(Quaternion.LookRotation(
                    direction,
                    Vector3.up));
            }
        }

        private void AccumulateMovementDistance(Vector3 direction)
        {
            Vector3 displacement = Vector3.ProjectOnPlane(
                enemyBody.position - lastMovementPosition,
                Vector3.up);
            movementDistance += Mathf.Max(
                0f,
                Vector3.Dot(displacement, direction));
            lastMovementPosition = enemyBody.position;
        }

        private void SetHorizontalVelocity(Vector3 horizontalVelocity)
        {
            Vector3 velocity = enemyBody.linearVelocity;
            velocity.x = horizontalVelocity.x;
            velocity.z = horizontalVelocity.z;
            enemyBody.linearVelocity = velocity;
            enemyBody.angularVelocity = Vector3.zero;
        }

        private void StopHorizontalMovement()
        {
            if (enemyBody == null)
            {
                return;
            }

            SetHorizontalVelocity(Vector3.zero);
        }

        private bool IsShieldCollider(Collider hitCollider)
        {
            if (hitCollider == null || shieldColliders == null)
            {
                return false;
            }

            for (int i = 0; i < shieldColliders.Length; i++)
            {
                Collider shieldCollider = shieldColliders[i];
                if (shieldCollider != null &&
                    (hitCollider == shieldCollider ||
                     hitCollider.transform.IsChildOf(
                         shieldCollider.transform)))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            StopMovementWhenBlocked(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            StopMovementWhenBlocked(collision);
        }

        private void StopMovementWhenBlocked(Collision collision)
        {
            if (State != ChargingEnemyState.Charge)
            {
                return;
            }

            if (collision.collider.GetComponentInParent<PlayerHealth>() != null ||
                collision.collider.GetComponentInParent<BombProjectile>() != null)
            {
                return;
            }

            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (Vector3.Dot(normal, ChargeDirection) > -0.2f)
                {
                    continue;
                }

                BeginChargeRecovery();
                return;
            }
        }

        private bool CheckDeathHeight()
        {
            if (State == ChargingEnemyState.Dead ||
                transform.position.y > deathWorldY)
            {
                return State == ChargingEnemyState.Dead;
            }

            Die();
            return true;
        }

        private void Die()
        {
            if (State == ChargingEnemyState.Dead)
            {
                return;
            }

            RestoreBodyAfterShieldKnockback();
            StopHorizontalMovement();
            SetState(ChargingEnemyState.Dead);
            Died?.Invoke();
            onDied?.Invoke();
            Destroy(gameObject);
        }

        private void SetState(ChargingEnemyState newState)
        {
            if (State == newState)
            {
                return;
            }

            State = newState;
            StateChanged?.Invoke(State);
        }

        private bool ValidateConfiguration()
        {
            if (enemyBody == null)
            {
                return LogConfigurationError("requires a Rigidbody reference.");
            }

            if (enemyCollider == null)
            {
                return LogConfigurationError("requires a root Collider.");
            }

            if (attackArea == null)
            {
                return LogConfigurationError(
                    "requires an Attack Area Collider reference.");
            }

            if (!attackArea.enabled || !attackArea.isTrigger)
            {
                return LogConfigurationError(
                    "requires the Attack Area Collider to be enabled and configured as a Trigger.");
            }

            if (attackArea.transform == transform ||
                attackArea.transform.IsChildOf(transform))
            {
                return LogConfigurationError(
                    "requires an Attack Area outside the enemy hierarchy so the boundary does not move with the enemy.");
            }

            if (chargeHitbox == null)
            {
                return LogConfigurationError(
                    "requires an EnemyChargeHitbox reference.");
            }

            if (playerKnockbackSpeed <= 0f ||
                playerKnockbackDuration <= 0f ||
                damageWaitDuration <= 0f)
            {
                return LogConfigurationError(
                    "requires positive Player Knockback Speed, Duration, and Damage Wait Duration values.");
            }

            return true;
        }

        private bool LogConfigurationError(string message)
        {
            Debug.LogError($"{nameof(ChargingEnemyController)} on " +
                $"'{name}' {message}", this);
            return false;
        }

        private void OnValidate()
        {
            approachSpeed = Mathf.Max(0f, approachSpeed);
            approachDurationRange = SortPositiveRange(
                approachDurationRange,
                0.01f);
            approachDurationMultiplier = Mathf.Max(
                1f,
                approachDurationMultiplier);
            pauseDurationRange = SortPositiveRange(
                pauseDurationRange,
                0.01f);
            turnSpeed = Mathf.Max(0f, turnSpeed);
            chargeSpeed = Mathf.Max(0.01f, chargeSpeed);
            chargeDistance = Mathf.Max(0.01f, chargeDistance);
            chargeDamage = Mathf.Max(0, chargeDamage);
            playerKnockbackSpeed = Mathf.Max(
                0.01f,
                playerKnockbackSpeed);
            playerKnockbackDuration = Mathf.Max(
                0.01f,
                playerKnockbackDuration);
            damageWaitDuration = Mathf.Max(
                0.01f,
                damageWaitDuration);
            chargeRecoveryDuration = Mathf.Max(
                0.01f,
                chargeRecoveryDuration);
            shieldKnockbackDistance = Mathf.Max(
                0.01f,
                shieldKnockbackDistance);
            shieldKnockbackDuration = Mathf.Max(
                0.01f,
                shieldKnockbackDuration);
        }

        private static Vector2 SortPositiveRange(
            Vector2 range,
            float minimum)
        {
            float first = Mathf.Max(minimum, range.x);
            float second = Mathf.Max(minimum, range.y);
            return new Vector2(
                Mathf.Min(first, second),
                Mathf.Max(first, second));
        }
    }
}
