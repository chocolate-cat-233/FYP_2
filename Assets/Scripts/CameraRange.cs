using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraRange : MonoBehaviour
{
    [Header("Camera Rotation")]
    [SerializeField] private CinemachineRotationController cameraController;
    [SerializeField] private float targetRotationX;
    [SerializeField] private float targetRotationY;

    [Header("Camera Offset")]
    [SerializeField] private bool modifyOffset;
    [Tooltip("Target offset in Cinemachine camera-local space.")]
    [SerializeField] private Vector3 targetOffset;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";

    private readonly HashSet<Collider> playerCollidersInside =
        new HashSet<Collider>();

    private void OnEnable()
    {
        if (!ValidateConfiguration())
        {
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled || !IsPlayer(other))
        {
            return;
        }

        playerCollidersInside.Add(other);
        cameraController.RotateTo(targetRotationX, targetRotationY);

        if (modifyOffset)
        {
            cameraController.OffsetTo(targetOffset);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other != null)
        {
            playerCollidersInside.Remove(other);
        }
    }

    private void OnDisable()
    {
        playerCollidersInside.Clear();
    }

    public void SetTargetRotationY(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            Debug.LogWarning(
                $"{nameof(CameraRange)} on '{name}' ignored a non-finite " +
                "Target Rotation Y value.",
                this);
            return;
        }

        targetRotationY = value;
        playerCollidersInside.RemoveWhere(
            collider => collider == null ||
                        !collider.enabled ||
                        !collider.gameObject.activeInHierarchy);

        if (isActiveAndEnabled && playerCollidersInside.Count > 0)
        {
            cameraController.RotateTo(targetRotationX, targetRotationY);
        }
    }

    private bool IsPlayer(Collider other)
    {
        Transform currentTransform = other.transform;

        while (currentTransform != null)
        {
            if (currentTransform.tag == playerTag)
            {
                return true;
            }

            currentTransform = currentTransform.parent;
        }

        return false;
    }

    private bool ValidateConfiguration()
    {
        bool isValid = true;

        if (cameraController == null)
        {
            Debug.LogError(
                $"{nameof(CameraRange)} on '{name}' requires a " +
                $"{nameof(CinemachineRotationController)} reference.",
                this);
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(playerTag))
        {
            Debug.LogError(
                $"{nameof(CameraRange)} on '{name}' requires a Player Tag.",
                this);
            isValid = false;
        }

        if (!HasTriggerCollider())
        {
            Debug.LogError(
                $"{nameof(CameraRange)} on '{name}' requires an enabled " +
                "Trigger Collider on the same GameObject.",
                this);
            isValid = false;
        }

        if (modifyOffset && !IsFinite(targetOffset))
        {
            Debug.LogError(
                $"{nameof(CameraRange)} on '{name}' requires a finite " +
                "Target Offset when Modify Offset is enabled.",
                this);
            isValid = false;
        }

        return isValid;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private bool HasTriggerCollider()
    {
        Collider[] colliders = GetComponents<Collider>();

        foreach (Collider currentCollider in colliders)
        {
            if (currentCollider.enabled && currentCollider.isTrigger)
            {
                return true;
            }
        }

        return false;
    }
}
