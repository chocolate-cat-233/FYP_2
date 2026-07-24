using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraRange : MonoBehaviour
{
    [Header("Camera Rotation")]
    [SerializeField] private CinemachineRotationController cameraController;
    [SerializeField] private float targetRotationX;
    [SerializeField] private float targetRotationY;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";

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

        cameraController.RotateTo(targetRotationX, targetRotationY);
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

        return isValid;
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
