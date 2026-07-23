using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public sealed class CinemachineRotationController : MonoBehaviour
{
    private const float AngleEpsilon = 0.01f;

    [Header("Rotation")]
    [Min(0.01f)]
    [SerializeField] private float rotationSpeed = 90f;

    private float targetRotationX;
    private float targetRotationY;

    public bool IsRotating { get; private set; }
    public float TargetRotationX => targetRotationX;
    public float TargetRotationY => targetRotationY;

    private void OnEnable()
    {
        if (rotationSpeed <= 0f)
        {
            Debug.LogError(
                $"{nameof(CinemachineRotationController)} on '{name}' requires " +
                "a Rotation Speed greater than zero.",
                this);
            enabled = false;
            return;
        }

        WarnIfCameraPipelineControlsRotation();
    }

    private void Update()
    {
        if (!IsRotating)
        {
            return;
        }

        Vector3 currentEulerAngles = transform.eulerAngles;
        float maxAngleDelta = rotationSpeed * Time.deltaTime;
        float nextRotationX = Mathf.MoveTowardsAngle(
            currentEulerAngles.x,
            targetRotationX,
            maxAngleDelta);
        float nextRotationY = Mathf.MoveTowardsAngle(
            currentEulerAngles.y,
            targetRotationY,
            maxAngleDelta);

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
            return;
        }

        IsRotating = true;
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
