using System;
using UnityEngine;
using UnityEngine.Serialization;
using SuperFinalFuse.BombThrowing.Mechanisms;

public sealed class pl_Move : MonoBehaviour
{
    private const string GroundTag = "Ground";
    private const float InputDeadZone = 0.0001f;
    private const float RayOriginOffset = 0.001f;
    private const float GroundProbeRadiusScale = 0.75f;
    private const float SameWallNormalDotThreshold = 0.99f;
    private static readonly Vector3[] GroundProbeDirections =
    {
        Vector3.forward,
        new Vector3(0.8660254f, 0f, -0.5f),
        new Vector3(-0.8660254f, 0f, -0.5f)
    };

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
    [FormerlySerializedAs("groundSnapDistance")]
    [SerializeField] private float centerGroundSnapDistance = 0.2f;
    [Min(0f)]
    [SerializeField] private float auxiliaryGroundSnapDistance = 0.2f;
    [Range(0f, 360f)]
    [Tooltip("World-space Y rotation of the three auxiliary ground probes.")]
    [SerializeField] private float auxiliaryGroundProbeRotation;
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
    private readonly RaycastHit[] wallHitBuffer = new RaycastHit[16];
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[32];
    private Action<Vector3> horizontalMovementDriver;
    private Behaviour groundCarrier;
    private Rigidbody groundCarrierBody;
    private Vector3 groundCarrierPosition;

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
        ClearGroundCarrier();

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
        ClearGroundCarrier();
        horizontalMovementDriver?.Invoke(Vector3.zero);
    }

    private void Update()
    {
        if (!isMovementReady)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        ApplyGroundCarrierMovement();

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
        Vector3 horizontalDisplacement = horizontalVelocity * deltaTime;

        float horizontalMovementDistance =
            horizontalDisplacement.magnitude;
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
        float wallCastRadius = Mathf.Max(
            playerWorldRadius - skin,
            InputDeadZone);
        if (horizontalMovementDistance > InputDeadZone &&
            TryGetBlockingWallHit(
                capsuleCenter + capsuleOffset,
                capsuleCenter - capsuleOffset,
                wallCastRadius,
                horizontalDisplacement,
                Vector3.zero,
                out RaycastHit wallHit))
        {
            horizontalVelocity = ProjectOnPlane(
                horizontalVelocity,
                wallHit.normal);
            horizontalVelocity.y = 0f;

            Vector3 slideDisplacement =
                horizontalVelocity * deltaTime;
            if (TryGetBlockingWallHit(
                    capsuleCenter + capsuleOffset,
                    capsuleCenter - capsuleOffset,
                    wallCastRadius,
                    slideDisplacement,
                    wallHit.normal,
                    out RaycastHit slideWallHit))
            {
                float slideDistance = slideDisplacement.magnitude;
                float allowedSlideDistance = Mathf.Clamp(
                    slideWallHit.distance - skin,
                    0f,
                    slideDistance);
                horizontalVelocity *=
                    allowedSlideDistance / slideDistance;
            }

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

    private bool TryGetBlockingWallHit(
        Vector3 capsuleTop,
        Vector3 capsuleBottom,
        float capsuleRadius,
        Vector3 displacement,
        Vector3 initialWallNormal,
        out RaycastHit wallHit)
    {
        wallHit = default;

        if (!TryGetNearestWallHit(
                capsuleTop,
                capsuleBottom,
                capsuleRadius,
                displacement,
                initialWallNormal,
                false,
                out RaycastHit fullBodyHit))
        {
            return false;
        }

        if (!IsGrounded ||
            stepOffset <= InputDeadZone ||
            !HasStableAuxiliaryGroundSupportAt(
                transform.position + displacement))
        {
            wallHit = fullBodyHit;
            return true;
        }

        bool foundNonGroundHit = TryGetNearestWallHit(
            capsuleTop,
            capsuleBottom,
            capsuleRadius,
            displacement,
            initialWallNormal,
            true,
            out RaycastHit nonGroundHit);

        Vector3 upperCapsuleBottom = Vector3.MoveTowards(
            capsuleBottom,
            capsuleTop,
            stepOffset);
        bool foundUpperBodyHit = TryGetNearestWallHit(
            capsuleTop,
            upperCapsuleBottom,
            capsuleRadius,
            displacement,
            initialWallNormal,
            false,
            out RaycastHit upperBodyHit);

        if (!foundNonGroundHit && !foundUpperBodyHit)
        {
            return false;
        }

        if (!foundUpperBodyHit ||
            (foundNonGroundHit &&
             nonGroundHit.distance <= upperBodyHit.distance))
        {
            wallHit = nonGroundHit;
            return true;
        }

        wallHit = upperBodyHit;
        return true;
    }

    private bool TryGetNearestWallHit(
        Vector3 capsuleTop,
        Vector3 capsuleBottom,
        float capsuleRadius,
        Vector3 displacement,
        Vector3 initialWallNormal,
        bool ignoreGroundColliders,
        out RaycastHit wallHit)
    {
        wallHit = default;

        float movementDistance = displacement.magnitude;
        if (movementDistance <= InputDeadZone)
        {
            return false;
        }

        Vector3 movementDirection = displacement / movementDistance;
        Vector3 initialHorizontalNormal = Vector3.ProjectOnPlane(
            initialWallNormal,
            Vector3.up);
        if (initialHorizontalNormal.sqrMagnitude > InputDeadZone)
        {
            initialHorizontalNormal.Normalize();
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            capsuleTop,
            capsuleBottom,
            capsuleRadius,
            movementDirection,
            wallHitBuffer,
            movementDistance + skin,
            wallMask.value | movableMask.value,
            QueryTriggerInteraction.Ignore);
        float nearestHitDistance = float.PositiveInfinity;
        bool foundWall = false;

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = wallHitBuffer[hitIndex];
            Vector3 hitHorizontalNormal = Vector3.ProjectOnPlane(
                hit.normal,
                Vector3.up);
            bool hasHorizontalNormal =
                hitHorizontalNormal.sqrMagnitude >
                InputDeadZone * InputDeadZone;
            bool isInitialWallSurface =
                initialHorizontalNormal.sqrMagnitude > InputDeadZone &&
                hasHorizontalNormal &&
                Vector3.Dot(
                    initialHorizontalNormal,
                    hitHorizontalNormal.normalized) >=
                SameWallNormalDotThreshold;

            if (hit.collider == null ||
                IsPlayerCollider(hit.collider) ||
                !hasHorizontalNormal ||
                isInitialWallSurface ||
                (ignoreGroundColliders &&
                 IsGroundCollider(hit.collider)) ||
                Vector3.Dot(
                    hitHorizontalNormal.normalized,
                    movementDirection) >= -InputDeadZone ||
                hit.distance >= nearestHitDistance)
            {
                continue;
            }

            wallHit = hit;
            nearestHitDistance = hit.distance;
            foundWall = true;
        }

        return foundWall;
    }

    private bool HasStableAuxiliaryGroundSupportAt(
        Vector3 playerPosition)
    {
        return TryGetAuxiliaryGroundSupport(
            GetGroundProbeCenter(playerPosition),
            true,
            out _);
    }

    private float GetPlayerWorldRadius()
    {
        return GetPlayerWorldRadius(playerCollider);
    }

    private static float GetPlayerWorldRadius(
        CapsuleCollider capsuleCollider)
    {
        if (capsuleCollider == null)
        {
            return 0f;
        }

        Vector3 colliderScale = capsuleCollider.transform.lossyScale;
        float horizontalScale = Mathf.Max(
            Mathf.Abs(colliderScale.x),
            Mathf.Abs(colliderScale.z));

        return capsuleCollider.radius * horizontalScale;
    }

    private void SnapOnGround()
    {
        bool wasGrounded = IsGrounded;
        if (TryGetGroundHit(out RaycastHit raycastHit))
        {
            ApplyGroundHit(raycastHit);
        }
        else if (wasGrounded &&
                 TryGetAuxiliaryGroundSupport(
                     out RaycastHit auxiliaryGroundHit))
        {
            ApplyGroundHit(auxiliaryGroundHit);
        }
        else
        {
            IsGrounded = false;
            groundNormal = Vector3.up;
            ClearGroundCarrier();
        }
    }

    private void ApplyGroundHit(RaycastHit groundHit)
    {
        Behaviour hitCarrier = GetGroundCarrier(groundHit.collider);
        float groundHeight = groundHit.point.y + playerHeight / 2f;
        float groundSkin = hitCarrier != null ? 0f : skin;

        IsGrounded = true;
        groundNormal = groundHit.normal;
        transform.position = new Vector3(
            transform.position.x,
            groundHeight - groundSkin,
            transform.position.z);
        SetGroundCarrier(hitCarrier, groundHit.rigidbody);
    }

    private void ApplyGroundCarrierMovement()
    {
        if (!IsGrounded ||
            groundCarrier == null ||
            groundCarrierBody == null ||
            !groundCarrier.isActiveAndEnabled)
        {
            ClearGroundCarrier();
            return;
        }

        Vector3 currentCarrierPosition = groundCarrierBody.position;
        transform.position +=
            currentCarrierPosition - groundCarrierPosition;
        groundCarrierPosition = currentCarrierPosition;
    }

    private void SetGroundCarrier(
        Behaviour carrier,
        Rigidbody carrierBody)
    {
        groundCarrier = carrier;
        groundCarrierBody = carrier != null ? carrierBody : null;
        groundCarrierPosition = groundCarrierBody != null
            ? groundCarrierBody.position
            : Vector3.zero;
    }

    private void ClearGroundCarrier()
    {
        groundCarrier = null;
        groundCarrierBody = null;
        groundCarrierPosition = Vector3.zero;
    }

    private bool TryGetGroundHit(out RaycastHit groundHit)
    {
        Vector3 rayOrigin = GetGroundProbeCenter();
        float probeDistance =
            GetGroundProbeDistance(centerGroundSnapDistance);
        return TryGetNearestProbeSurface(
                   rayOrigin,
                   probeDistance,
                   out groundHit) &&
               IsGroundCollider(groundHit.collider);
    }

    private bool TryGetAuxiliaryGroundSupport(
        out RaycastHit groundHit)
    {
        return TryGetAuxiliaryGroundSupport(
            GetGroundProbeCenter(),
            false,
            out groundHit);
    }

    private bool TryGetAuxiliaryGroundSupport(
        Vector3 groundProbeCenter,
        bool requireAllProbes,
        out RaycastHit groundHit)
    {
        groundHit = default;
        float groundProbeRadius =
            GetGroundProbeRadius(playerCollider);
        float probeDistance =
            GetGroundProbeDistance(auxiliaryGroundSnapDistance);

        if (groundProbeRadius <= InputDeadZone)
        {
            return false;
        }

        bool foundGround = false;
        int validGroundProbeCount = 0;
        for (int probeIndex = 0;
             probeIndex < GroundProbeDirections.Length;
             probeIndex++)
        {
            Vector3 rayOrigin =
                groundProbeCenter +
                GetGroundProbeDirection(probeIndex) * groundProbeRadius;
            bool foundProbeGround = TryGetNearestProbeSurface(
                                        rayOrigin,
                                        probeDistance,
                                        out RaycastHit nearestProbeHit) &&
                                    IsGroundCollider(
                                        nearestProbeHit.collider);

            if (!foundProbeGround)
            {
                if (requireAllProbes)
                {
                    groundHit = default;
                    return false;
                }

                continue;
            }

            validGroundProbeCount++;
            if (!foundGround ||
                nearestProbeHit.point.y > groundHit.point.y)
            {
                groundHit = nearestProbeHit;
                foundGround = true;
            }
        }

        return foundGround &&
               (!requireAllProbes ||
                validGroundProbeCount == GroundProbeDirections.Length);
    }

    private bool TryGetNearestProbeSurface(
        Vector3 rayOrigin,
        float probeDistance,
        out RaycastHit nearestHit)
    {
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            groundHitBuffer,
            probeDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        nearestHit = default;
        float nearestHitDistance = float.PositiveInfinity;

        for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
        {
            RaycastHit hit = groundHitBuffer[hitIndex];

            if (hit.collider == null ||
                IsPlayerCollider(hit.collider) ||
                hit.distance >= nearestHitDistance)
            {
                continue;
            }

            nearestHit = hit;
            nearestHitDistance = hit.distance;
        }

        return nearestHit.collider != null;
    }

    private Vector3 GetGroundProbeCenter()
    {
        return GetGroundProbeCenter(transform.position);
    }

    private Vector3 GetGroundProbeCenter(Vector3 playerPosition)
    {
        float currentGroundHeight =
            playerPosition.y - playerHeight / 2f + skin;
        return new Vector3(
            playerPosition.x,
            currentGroundHeight + stepOffset + RayOriginOffset,
            playerPosition.z);
    }

    private float GetGroundProbeRadius(
        CapsuleCollider capsuleCollider)
    {
        float playerWorldRadius =
            GetPlayerWorldRadius(capsuleCollider);
        return Mathf.Min(
            playerWorldRadius * GroundProbeRadiusScale,
            Mathf.Max(0f, playerWorldRadius - skin));
    }

    private float GetGroundProbeDistance(float snapDistance)
    {
        return stepOffset + snapDistance + skin + RayOriginOffset;
    }

    private Vector3 GetGroundProbeDirection(int probeIndex)
    {
        return Quaternion.AngleAxis(
                   auxiliaryGroundProbeRotation,
                   Vector3.up) *
               GroundProbeDirections[probeIndex];
    }

    private void OnDrawGizmosSelected()
    {
        CapsuleCollider capsuleCollider = playerCollider != null
            ? playerCollider
            : GetComponent<CapsuleCollider>();
        if (capsuleCollider == null)
        {
            return;
        }

        Vector3 probeCenter = GetGroundProbeCenter();
        float centerProbeDistance =
            GetGroundProbeDistance(centerGroundSnapDistance);
        float auxiliaryProbeDistance =
            GetGroundProbeDistance(auxiliaryGroundSnapDistance);

        Gizmos.color = Color.yellow;
        DrawGroundProbeGizmo(probeCenter, centerProbeDistance);

        float probeRadius = GetGroundProbeRadius(capsuleCollider);
        if (probeRadius <= InputDeadZone)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        for (int i = 0; i < GroundProbeDirections.Length; i++)
        {
            Vector3 probeOrigin =
                probeCenter + GetGroundProbeDirection(i) * probeRadius;
            DrawGroundProbeGizmo(probeOrigin, auxiliaryProbeDistance);
        }

        Gizmos.color = new Color(1f, 0.5f, 0f);
        Vector3 firstLimitPoint = Vector3.zero;
        Vector3 previousLimitPoint = Vector3.zero;
        for (int i = 0; i < GroundProbeDirections.Length; i++)
        {
            Vector3 probeOrigin =
                probeCenter + GetGroundProbeDirection(i) * probeRadius;
            Vector3 limitPoint =
                probeOrigin + Vector3.down * auxiliaryProbeDistance;

            Gizmos.DrawWireSphere(limitPoint, 0.025f);
            if (i == 0)
            {
                firstLimitPoint = limitPoint;
            }
            else
            {
                Gizmos.DrawLine(previousLimitPoint, limitPoint);
            }

            previousLimitPoint = limitPoint;
        }

        Gizmos.DrawLine(previousLimitPoint, firstLimitPoint);
    }

    private static void DrawGroundProbeGizmo(
        Vector3 origin,
        float length)
    {
        const float OriginRadius = 0.025f;
        Gizmos.DrawWireSphere(origin, OriginRadius);
        Gizmos.DrawLine(origin, origin + Vector3.down * length);
    }

    private static Behaviour GetGroundCarrier(Collider groundCollider)
    {
        if (groundCollider == null)
        {
            return null;
        }

        BombMovableIron iron =
            groundCollider.GetComponentInParent<BombMovableIron>();
        if (iron != null)
        {
            return iron;
        }

        return groundCollider.GetComponentInParent<BombRailPlatform>();
    }

    private bool IsPlayerCollider(Collider hitCollider)
    {
        Transform hitTransform = hitCollider.transform;
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    private static bool IsGroundCollider(Collider hitCollider)
    {
        return hitCollider != null &&
               (hitCollider.CompareTag(GroundTag) ||
                hitCollider.GetComponentInParent<BombRailPlatform>() != null ||
                hitCollider.GetComponentInParent<
                    SuperFinalFuse.BombThrowing.Mechanisms.BombJumpPad>() != null ||
                hitCollider.GetComponentInParent<
                    SuperFinalFuse.PlatformReveal.RevealablePlatform>() != null);
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
            centerGroundSnapDistance < 0f ||
            auxiliaryGroundSnapDistance < 0f ||
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
