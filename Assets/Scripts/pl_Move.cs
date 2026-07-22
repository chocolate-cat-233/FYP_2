using UnityEngine;

public sealed class pl_Move : MonoBehaviour
{
    private const string GroundTag = "Ground";
    private const float InputDeadZone = 0.0001f;
    private const float RayOriginOffset = 0.001f;

    [Header("References")]
    [SerializeField] private InputManager inputManager;
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

    private float currentMoveSpeed;
    private float verticalVelocity;
    private Vector3 lastMoveDirection = Vector3.forward;
    private Vector3 groundNormal = Vector3.up;
    private bool isMovementReady;
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[32];

    public bool IsGrounded { get; private set; }

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
        isMovementReady = true;
    }

    private void OnDisable()
    {
        currentMoveSpeed = 0f;
        verticalVelocity = 0f;
        groundNormal = Vector3.up;
        IsGrounded = false;
        isMovementReady = false;
    }

    private void Update()
    {
        if (!isMovementReady)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        Vector2 moveInput = Vector2.ClampMagnitude(inputManager.MoveInput, 1f);

        UpdateHorizontalMovement(moveInput, deltaTime);
        UpdateVerticalVelocity(deltaTime);
        ApplyMovement(deltaTime);
        SnapOnGround();
    }

    private void UpdateHorizontalMovement(Vector2 moveInput, float deltaTime)
    {
        float inputMagnitude = moveInput.magnitude;
        bool hasMoveInput = inputMagnitude > InputDeadZone;

        if (hasMoveInput)
        {
            Vector3 inputDirection = GetCameraRelativeDirection(moveInput).normalized;
            lastMoveDirection = IsGrounded
                ? ProjectOnPlane(inputDirection, groundNormal)
                : inputDirection;
            RotateTowards(inputDirection, deltaTime);
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

        float movementDistance = velocity.magnitude * deltaTime;
        float wallDetectionDistance = movementDistance + GetPlayerWorldRadius();

        if (movementDistance > InputDeadZone &&
            Physics.Raycast(
                transform.position,
                velocity,
                out RaycastHit wallHit,
                wallDetectionDistance,
                wallMask,
                QueryTriggerInteraction.Ignore))
        {
            velocity = ProjectOnPlane(velocity, wallHit.normal);
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

    private bool ValidateConfiguration()
    {
        if (inputManager == null)
        {
            return LogConfigurationError("requires an InputManager reference.");
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
