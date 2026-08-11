using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
[RequireComponent(typeof(CinemachineCameraOffset))]
public sealed class CinemachineRotationController : MonoBehaviour
{
    private const float AngleEpsilon = 0.01f;
    private const float OffsetEpsilon = 0.001f;

    [Header("Rotation")]
    [Min(0.01f)]
    [SerializeField] private float rotationSmoothTime = 0.35f;
    [Min(0.01f)]
    [FormerlySerializedAs("rotationSpeed")]
    [SerializeField] private float maxRotationSpeed = 90f;

    [Header("Offset")]
    [Min(0.01f)]
    [SerializeField] private float offsetSmoothTime = 0.4f;
    [Min(0.01f)]
    [SerializeField] private float maxOffsetSpeed = 8f;

    private CinemachineCameraOffset cameraOffset;
    private float targetRotationX;
    private float targetRotationY;
    private float rotationVelocityX;
    private float rotationVelocityY;
    private Vector3 targetOffset;
    private Vector3 offsetVelocity;

    public bool IsRotating { get; private set; }
    public bool IsOffsetMoving { get; private set; }
    public float TargetRotationX => targetRotationX;
    public float TargetRotationY => targetRotationY;
    public Vector3 TargetOffset => targetOffset;

    private void Awake()
    {
        cameraOffset = GetComponent<CinemachineCameraOffset>();
    }

    private void OnEnable()
    {
        cameraOffset = GetComponent<CinemachineCameraOffset>();

        if (!ValidateConfiguration())
        {
            enabled = false;
            return;
        }

        Vector3 currentEulerAngles = transform.eulerAngles;
        targetRotationX = currentEulerAngles.x;
        targetRotationY = currentEulerAngles.y;
        targetOffset = cameraOffset.Offset;
        ResetMotionState();

        WarnIfCameraPipelineControlsRotation();
    }

    private void OnDisable()
    {
        ResetMotionState();
    }

    private void Update()
    {
        UpdateRotation();
        UpdateOffset();
    }

    private void UpdateRotation()
    {
        if (!IsRotating)
        {
            return;
        }

        Vector3 currentEulerAngles = transform.eulerAngles;
        float nextRotationX = Mathf.SmoothDampAngle(
            currentEulerAngles.x,
            targetRotationX,
            ref rotationVelocityX,
            rotationSmoothTime,
            maxRotationSpeed,
            Time.deltaTime);
        float nextRotationY = Mathf.SmoothDampAngle(
            currentEulerAngles.y,
            targetRotationY,
            ref rotationVelocityY,
            rotationSmoothTime,
            maxRotationSpeed,
            Time.deltaTime);

        SetRotationXY(currentEulerAngles, nextRotationX, nextRotationY);

        bool reachedTargetX =
            Mathf.Abs(Mathf.DeltaAngle(nextRotationX, targetRotationX)) <=
            AngleEpsilon;
        bool reachedTargetY =
            Mathf.Abs(Mathf.DeltaAngle(nextRotationY, targetRotationY)) <=
            AngleEpsilon;

        if (reachedTargetX && reachedTargetY)
        {
            SetRotationXY(
                transform.eulerAngles,
                targetRotationX,
                targetRotationY);
            IsRotating = false;
            rotationVelocityX = 0f;
            rotationVelocityY = 0f;
        }
    }

    private void UpdateOffset()
    {
        if (!IsOffsetMoving)
        {
            return;
        }

        Vector3 nextOffset = Vector3.SmoothDamp(
            cameraOffset.Offset,
            targetOffset,
            ref offsetVelocity,
            offsetSmoothTime,
            maxOffsetSpeed,
            Time.deltaTime);

        cameraOffset.Offset = nextOffset;

        if ((nextOffset - targetOffset).sqrMagnitude <=
            OffsetEpsilon * OffsetEpsilon)
        {
            cameraOffset.Offset = targetOffset;
            IsOffsetMoving = false;
            offsetVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Smoothly rotates the Cinemachine Camera Transform to the supplied
    /// world-space X and Y rotations.
    /// </summary>
    public void RotateTo(float newTargetRotationX, float newTargetRotationY)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (float.IsNaN(newTargetRotationX) ||
            float.IsInfinity(newTargetRotationX) ||
            float.IsNaN(newTargetRotationY) ||
            float.IsInfinity(newTargetRotationY))
        {
            Debug.LogError(
                $"{nameof(RotateTo)} on '{name}' requires finite X and Y angles.",
                this);
            return;
        }

        float normalizedTargetX = Mathf.Repeat(newTargetRotationX, 360f);
        float normalizedTargetY = Mathf.Repeat(newTargetRotationY, 360f);

        if (IsRotating &&
            Mathf.Abs(Mathf.DeltaAngle(targetRotationX, normalizedTargetX)) <=
            AngleEpsilon &&
            Mathf.Abs(Mathf.DeltaAngle(targetRotationY, normalizedTargetY)) <=
            AngleEpsilon)
        {
            return;
        }

        targetRotationX = normalizedTargetX;
        targetRotationY = normalizedTargetY;
        Vector3 currentEulerAngles = transform.eulerAngles;
        bool isAtTargetX =
            Mathf.Abs(Mathf.DeltaAngle(
                currentEulerAngles.x,
                targetRotationX)) <= AngleEpsilon;
        bool isAtTargetY =
            Mathf.Abs(Mathf.DeltaAngle(
                currentEulerAngles.y,
                targetRotationY)) <= AngleEpsilon;

        if (isAtTargetX && isAtTargetY)
        {
            SetRotationXY(
                currentEulerAngles,
                targetRotationX,
                targetRotationY);
            IsRotating = false;
            rotationVelocityX = 0f;
            rotationVelocityY = 0f;
            return;
        }

        IsRotating = true;
    }

    /// <summary>
    /// Smoothly moves the Cinemachine camera-space offset to the supplied
    /// target.
    /// </summary>
    public void OffsetTo(Vector3 newTargetOffset)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (!IsFinite(newTargetOffset))
        {
            Debug.LogError(
                $"{nameof(OffsetTo)} on '{name}' requires a finite offset.",
                this);
            return;
        }

        if (IsOffsetMoving &&
            (targetOffset - newTargetOffset).sqrMagnitude <=
            OffsetEpsilon * OffsetEpsilon)
        {
            return;
        }

        targetOffset = newTargetOffset;

        if ((cameraOffset.Offset - targetOffset).sqrMagnitude <=
            OffsetEpsilon * OffsetEpsilon)
        {
            cameraOffset.Offset = targetOffset;
            IsOffsetMoving = false;
            offsetVelocity = Vector3.zero;
            return;
        }

        IsOffsetMoving = true;
    }

    /// <summary>
    /// Updates only the world-space X target while preserving the current
    /// Y target.
    /// </summary>
    public void RotateToX(float newTargetRotationX)
    {
        float preservedRotationY = IsRotating
            ? targetRotationY
            : transform.eulerAngles.y;

        RotateTo(newTargetRotationX, preservedRotationY);
    }

    /// <summary>
    /// Updates only the world-space Y target while preserving the current
    /// X target.
    /// </summary>
    public void RotateToY(float newTargetRotationY)
    {
        float preservedRotationX = IsRotating
            ? targetRotationX
            : transform.eulerAngles.x;

        RotateTo(preservedRotationX, newTargetRotationY);
    }

    private void SetRotationXY(
        Vector3 currentEulerAngles,
        float rotationX,
        float rotationY)
    {
        transform.rotation = Quaternion.Euler(
            rotationX,
            rotationY,
            currentEulerAngles.z);
    }

    private bool ValidateConfiguration()
    {
        bool isValid = true;

        if (cameraOffset == null)
        {
            Debug.LogError(
                $"{nameof(CinemachineRotationController)} on '{name}' " +
                $"requires a {nameof(CinemachineCameraOffset)} component.",
                this);
            isValid = false;
        }

        if (!IsPositiveFinite(rotationSmoothTime) ||
            !IsPositiveFinite(maxRotationSpeed))
        {
            Debug.LogError(
                $"{nameof(CinemachineRotationController)} on '{name}' " +
                "requires finite Rotation Smooth Time and Max Rotation " +
                "Speed values greater than zero.",
                this);
            isValid = false;
        }

        if (!IsPositiveFinite(offsetSmoothTime) ||
            !IsPositiveFinite(maxOffsetSpeed))
        {
            Debug.LogError(
                $"{nameof(CinemachineRotationController)} on '{name}' " +
                "requires finite Offset Smooth Time and Max Offset Speed " +
                "values greater than zero.",
                this);
            isValid = false;
        }

        return isValid;
    }

    private void ResetMotionState()
    {
        IsRotating = false;
        IsOffsetMoving = false;
        rotationVelocityX = 0f;
        rotationVelocityY = 0f;
        offsetVelocity = Vector3.zero;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private void WarnIfCameraPipelineControlsRotation()
    {
        CinemachineComponentBase[] pipelineComponents =
            GetComponents<CinemachineComponentBase>();

        foreach (CinemachineComponentBase pipelineComponent in
                 pipelineComponents)
        {
            if (pipelineComponent.enabled &&
                pipelineComponent.Stage == CinemachineCore.Stage.Aim)
            {
                Debug.LogWarning(
                    $"{pipelineComponent.GetType().Name} on '{name}' controls " +
                    "the Cinemachine Camera orientation and may overwrite " +
                    $"{nameof(CinemachineRotationController)} Transform " +
                    "rotation.",
                    this);
                return;
            }
        }
    }
}
