using System;
using UnityEngine;

public sealed class pl_Move : MonoBehaviour
{
    private const string GroundTag = "Ground";
    private const float InputDeadZone = 0.0001f;
    private const float RayOriginOffset = 0.001f;

    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private pl_stateManager stateManager;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private CapsuleCollider playerCollider;

    [Header("Movement")]
    [Min(0f)]
    [SerializeField] private float maxMoveSpeed = 5f;
    [Min(0.01f)]
    [SerializeField] private float accelerationTime = 1f;
    [Min(0.01f)]
    [SerializeField] private float decelerationTime = 0.5f;
    [Min(0f)]
    [SerializeField] private float maxTurnSpeed = 360f;

    [Header("Ground Check")]
    [Min(0.001f)]
    [SerializeField] private float playerHeight = 2f;
    [Min(0f)]
    [SerializeField] private float groundSnapDistance = 0.2f;
    [Min(0f)]
    [SerializeField] private float skin = 0.02f;
    [Min(0f)]
    [SerializeField] private float stepOffset = 0.3f;

    [Header("Wall Check")]
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask movableMask;

    private float currentMoveSpeed;
    private float verticalVelocity;
    private Vector3 lastMoveDirection = Vector3.forward;
    private Vector3 groundNormal = Vector3.up;
    private bool isMovementReady;
    private bool isMovingToTarget;
    private bool canPlayerControlMovement = true;
    private bool canPlayerTurn = true;
    private Vector3 lockedMoveForward = Vector3.forward;
    private Vector3 moveTarget;
    private Quaternion moveTargetRotation = Quaternion.identity;
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[32];
    private Action<Vector3> horizontalMovementDriver;

    public bool IsGrounded { get; private set; }
    public bool IsMovingToTarget => isMovingToTarget;

    public event Action<Vector3> MovementApplied;

    private void OnEnable()
    {
        if (playerCollider == null)
        {
            playerCollider = GetComponent<CapsuleCollider>();
        }

        if (!ValidateConfiguration())
        {
            enabled = false;
            return;
        }

        currentMoveSpeed = 0f;
        verticalVelocity = 0f;
        groundNormal = Vector3.up;
        IsGrounded = false;
        isMovingToTarget = false;
        isMovementReady = true;

        stateManager.StateChanged += OnPlayerStateChanged;
        ApplyPlayerState(stateManager.CurrentState);
    }

    private void OnDisable()
    {
        if (stateManager != null)
        {
            stateManager.StateChanged -= OnPlayerStateChanged;
        }

        currentMoveSpeed = 0f;
        verticalVelocity = 0f;
        groundNormal = Vector3.up;
        IsGrounded = false;
        isMovingToTarget = false;
        isMovementReady = false;
        horizontalMovementDriver?.Invoke(Vector3.zero);
    }

    private void Update()
    {
        if (!isMovementReady)
        {
            return;
        }

        float deltaTime = Time.deltaTime;

        if (isMovingToTarget)
        {
            UpdateMoveToTarget(deltaTime);
            return;
        }

        Vector2 moveInput = canPlayerControlMovement
            ? Vector2.ClampMagnitude(inputManager.MoveInput, 1f)
            : Vector2.zero;

        Vector3 positionBeforeMovement = transform.position;

        UpdateHorizontalMovement(moveInput, deltaTime);
        UpdateVerticalVelocity(deltaTime);
        ApplyMovement(deltaTime);
        SnapOnGround();

        Vector3 appliedMovement = transform.position - positionBeforeMovement;
        appliedMovement.y = 0f;

        if (appliedMovement.sqrMagnitude > InputDeadZone * InputDeadZone)
        {
            MovementApplied?.Invoke(appliedMovement);
        }
    }

    public void MoveToTarget(
        Vector3 targetPosition,
        Vector3 facingTargetPosition)
    {
        moveTarget = targetPosition;
        moveTargetRotation = transform.rotation;

        Vector3 facingDirection = Vector3.ProjectOnPlane(
            facingTargetPosition - targetPosition,
            Vector3.up);

        if (facingDirection.sqrMagnitude > InputDeadZone * InputDeadZone)
        {
            moveTargetRotation = Quaternion.LookRotation(
                facingDirection.normalized,
                Vector3.up);
        }
        else
        {
            Debug.LogWarning(
                $"{nameof(MoveToTarget)} on '{name}' received a facing target " +
                "with no horizontal direction and will preserve the current rotation.",
                this);
        }

        isMovingToTarget = true;
        currentMoveSpeed = 0f;
        verticalVelocity = 0f;
    }

    public bool TrySetHorizontalMovementDriver(Action<Vector3> driver)
    {
        if (driver == null)
        {
            return false;
        }

        if (horizontalMovementDriver != null &&
            horizontalMovementDriver != driver)
        {
            return false;
        }

        horizontalMovementDriver = driver;
        return true;
    }

    public void ClearHorizontalMovementDriver(Action<Vector3> driver)
    {
        if (horizontalMovementDriver == driver)
        {
            horizontalMovementDriver = null;
        }
    }

    public void CancelMoveToTarget()
    {
        isMovingToTarget = false;
        currentMoveSpeed = 0f;
    }

    private void UpdateMoveToTarget(float deltaTime)
    {
        Vector3 horizontalTarget = new Vector3(
            moveTarget.x,
            transform.position.y,
            moveTarget.z);

        transform.position = Vector3.MoveTowards(
            transform.position,
            horizontalTarget,
            maxMoveSpeed * 0.5f * deltaTime);

        UpdateVerticalVelocity(deltaTime);
        transform.position += Vector3.up * verticalVelocity * deltaTime;
        SnapOnGround();

        Vector3 horizontalDistance = new Vector3(
            transform.position.x - moveTarget.x,
            0f,
            transform.position.z - moveTarget.z);

        if (horizontalDistance.sqrMagnitude <= InputDeadZone * InputDeadZone)
        {
            transform.rotation = moveTargetRotation;
            isMovingToTarget = false;
        }
    }

    private void UpdateHorizontalMovement(Vector2 moveInput, float deltaTime)
    {
        Vector3 inputMovement = GetCameraRelativeDirection(moveInput);
        float inputMagnitude = moveInput.magnitude;

        if (!canPlayerTurn)
        {
            inputMovement = Vector3.Project(
                inputMovement,
                lockedMoveForward);
            inputMagnitude = inputMovement.magnitude;
        }

        bool hasMoveInput = inputMagnitude > InputDeadZone;

        if (hasMoveInput)
        {
            Vector3 inputDirection = inputMovement.normalized;
            lastMoveDirection = IsGrounded
                ? ProjectOnPlane(inputDirection, groundNormal)
                : inputDirection;

            if (canPlayerTurn)
            {
                RotateTowards(inputDirection, deltaTime);
            }
        }

        float targetSpeed = hasMoveInput ? maxMoveSpeed * inputMagnitude : 0f;
        float speedChangeRate = targetSpeed > currentMoveSpeed
            ? maxMoveSpeed / accelerationTime
            : maxMoveSpeed / decelerationTime;

        currentMoveSpeed = Mathf.MoveTowards(
            currentMoveSpeed,
            targetSpeed,
            speedChangeRate * deltaTime);
    }

    private Vector3 GetCameraRelativeDirection(Vector2 moveInput)
    {
        Vector3 cameraForward = Vector3.ProjectOnPlane(
            cameraTransform.forward,
            Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(
            cameraTransform.right,
            Vector3.up).normalized;

        return cameraForward * moveInput.y + cameraRight * moveInput.x;
    }

    private void RotateTowards(Vector3 moveDirection, float deltaTime)
    {
        if (maxTurnSpeed <= 0f || moveDirection.sqrMagnitude <= InputDeadZone)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            maxTurnSpeed * deltaTime);
    }

    private void UpdateVerticalVelocity(float deltaTime)
    {
        if (IsGrounded)
        {
            verticalVelocity = 0f;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * deltaTime;
        }
    }

    private void ApplyMovement(float deltaTime)
    {
        Vector3 horizontalVelocity = lastMoveDirection * currentMoveSpeed;
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;

        float horizontalMovementDistance =
            horizontalVelocity.magnitude * deltaTime;
        float playerWorldRadius = GetPlayerWorldRadius();
        Vector3 colliderScale = playerCollider.transform.lossyScale;
        float playerWorldHeight = Mathf.Max(
            playerCollider.height * Mathf.Abs(colliderScale.y),
            playerWorldRadius * 2f);
        Vector3 capsuleCenter = playerCollider.transform.TransformPoint(
            playerCollider.center);
        float capsuleHalfSegment = Mathf.Max(
            playerWorldHeight * 0.5f - playerWorldRadius,
            0f);
        Vector3 capsuleOffset =
            playerCollider.transform.up * capsuleHalfSegment;
        float wallDetectionDistance = horizontalMovementDistance + skin;

        if (horizontalMovementDistance > InputDeadZone &&
            Physics.CapsuleCast(
                capsuleCenter + capsuleOffset,
                capsuleCenter - capsuleOffset,
                playerWorldRadius,
                horizontalVelocity.normalized,
                out RaycastHit wallHit,
                wallDetectionDistance,
                wallMask.value | movableMask.value,
                QueryTriggerInteraction.Ignore))
        {
            horizontalVelocity = ProjectOnPlane(
                horizontalVelocity,
                wallHit.normal);
            horizontalVelocity.y = 0f;
            velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        }

        if (horizontalMovementDriver != null)
        {
            horizontalMovementDriver(horizontalVelocity * deltaTime);
            transform.position +=
                Vector3.up * verticalVelocity * deltaTime;
            return;
        }

        transform.position += velocity * deltaTime;
    }

    private float GetPlayerWorldRadius()
    {
        Vector3 colliderScale = playerCollider.transform.lossyScale;
        float horizontalScale = Mathf.Max(
            Mathf.Abs(colliderScale.x),
            Mathf.Abs(colliderScale.z));

        return playerCollider.radius * horizontalScale;
    }

    private void SnapOnGround()
    {
        if (TryGetGroundHit(out RaycastHit raycastHit))
        {
            groundNormal = raycastHit.normal;
            float groundHeight = raycastHit.point.y + playerHeight / 2f;
            float bottomDistance = transform.position.y - groundHeight;
            bool reachedGround = bottomDistance <= skin;
            bool canSnapDownSlope = IsGrounded &&
                                    bottomDistance <= groundSnapDistance;

            if (reachedGround || canSnapDownSlope)
            {
                IsGrounded = true;
                transform.position = new Vector3(
                    transform.position.x,
                    groundHeight - skin,
                    transform.position.z);
            }
            else
            {
                IsGrounded = false;
            }
        }
        else
        {
            IsGrounded = false;
            groundNormal = Vector3.up;
        }
    }

    private bool TryGetGroundHit(out RaycastHit groundHit)
    {
        float currentGroundHeight = transform.position.y - playerHeight / 2f + skin;
        Vector3 rayOrigin = new Vector3(
            transform.position.x,
            currentGroundHeight + stepOffset + RayOriginOffset,
            transform.position.z);
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            groundHitBuffer,
            2000f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        groundHit = default;
        bool foundGround = false;
        float nearestHitDistance = float.PositiveInfinity;

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = groundHitBuffer[hitIndex];

            if (hit.collider == null ||
                IsPlayerCollider(hit.collider) ||
                hit.collider.tag != GroundTag ||
                hit.distance >= nearestHitDistance)
            {
                continue;
            }

            groundHit = hit;
            foundGround = true;
            nearestHitDistance = hit.distance;
        }

        return foundGround;
    }

    private bool IsPlayerCollider(Collider hitCollider)
    {
        Transform hitTransform = hitCollider.transform;
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private Vector3 ProjectOnPlane(Vector3 velocity, Vector3 normal)
    {
        float dot = Vector3.Dot(velocity, normal);
        return velocity - dot * normal;
    }

    private void OnPlayerStateChanged(PlayerState playerState)
    {
        ApplyPlayerState(playerState);
    }

    private void ApplyPlayerState(PlayerState playerState)
    {
        if (playerState == null)
        {
            return;
        }

        canPlayerControlMovement = playerState.CanPlayerControlMovement;
        canPlayerTurn = playerState.CanPlayerTurn;
        maxMoveSpeed = playerState.MaxMoveSpeed;
        stepOffset = playerState.MaxStepOffset;

        if (!canPlayerTurn)
        {
            currentMoveSpeed = 0f;
            lockedMoveForward = Vector3.ProjectOnPlane(
                transform.forward,
                Vector3.up).normalized;

            if (lockedMoveForward.sqrMagnitude <= InputDeadZone)
            {
                lockedMoveForward = Vector3.forward;
            }

            lastMoveDirection = lockedMoveForward;
        }

        if (!canPlayerControlMovement && !isMovingToTarget)
        {
            currentMoveSpeed = 0f;
        }
    }

    private bool ValidateConfiguration()
    {
        if (inputManager == null)
        {
            return LogConfigurationError("requires an InputManager reference.");
        }

        if (stateManager == null)
        {
            return LogConfigurationError(
                $"requires a {nameof(pl_stateManager)} reference.");
        }

        if (cameraTransform == null)
        {
            return LogConfigurationError("requires a camera Transform reference.");
        }

        if (playerCollider == null)
        {
            return LogConfigurationError("requires a CapsuleCollider reference.");
        }

        if (playerCollider.direction != 1)
        {
            return LogConfigurationError(
                "requires the CapsuleCollider Direction to be Y-Axis.");
        }

        Vector3 cameraForward = Vector3.ProjectOnPlane(
            cameraTransform.forward,
            Vector3.up);
        Vector3 cameraRight = Vector3.ProjectOnPlane(
            cameraTransform.right,
            Vector3.up);

        if (cameraForward.sqrMagnitude <= InputDeadZone ||
            cameraRight.sqrMagnitude <= InputDeadZone)
        {
            return LogConfigurationError(
                "requires a camera with valid horizontal forward and right directions.");
        }

        if (maxMoveSpeed <= 0f ||
            accelerationTime <= 0f ||
            decelerationTime <= 0f ||
            maxTurnSpeed < 0f ||
            playerHeight <= 0f ||
            groundSnapDistance < 0f ||
            skin < 0f ||
            skin >= playerHeight / 2f ||
            stepOffset < 0f)
        {
            return LogConfigurationError("has one or more invalid movement settings.");
        }

        return true;
    }

    private bool LogConfigurationError(string message)
    {
        Debug.LogError($"{nameof(pl_Move)} on '{name}' {message}", this);
        return false;
    }
}
