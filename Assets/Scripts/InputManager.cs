using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputManager : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference interactAction;

    private bool isInputReady;

    /// <summary>
    /// The current device-independent movement input.
    /// </summary>
    public Vector2 MoveInput => isInputReady
        ? moveAction.action.ReadValue<Vector2>()
        : Vector2.zero;

    /// <summary>
    /// Raised once when the interact button is performed.
    /// </summary>
    public event Action InteractPressed;

    private void OnEnable()
    {
        if (!ValidateActions())
        {
            enabled = false;
            return;
        }

        interactAction.action.performed += OnInteractPerformed;
        moveAction.action.Enable();
        interactAction.action.Enable();
        isInputReady = true;
    }

    private void OnDisable()
    {
        if (!isInputReady)
        {
            return;
        }

        interactAction.action.performed -= OnInteractPerformed;
        moveAction.action.Disable();
        interactAction.action.Disable();
        isInputReady = false;
    }

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        InteractPressed?.Invoke();
    }

    private bool ValidateActions()
    {
        if (moveAction == null || moveAction.action == null)
        {
            Debug.LogError(
                $"{nameof(InputManager)} on '{name}' requires a Move action reference.",
                this);
            return false;
        }

        if (interactAction == null || interactAction.action == null)
        {
            Debug.LogError(
                $"{nameof(InputManager)} on '{name}' requires an Interact action reference.",
                this);
            return false;
        }

        if (moveAction.action.type != InputActionType.Value ||
            moveAction.action.expectedControlType != "Vector2")
        {
            Debug.LogError(
                $"The Move action assigned to {nameof(InputManager)} on '{name}' " +
                "must be a Value action with a Vector2 control type.",
                this);
            return false;
        }

        string interactControlType = interactAction.action.expectedControlType;
        bool hasValidInteractControlType = string.IsNullOrEmpty(interactControlType) ||
                                           interactControlType == "Button";

        if (interactAction.action.type != InputActionType.Button ||
            !hasValidInteractControlType)
        {
            Debug.LogError(
                $"The Interact action assigned to {nameof(InputManager)} on '{name}' " +
                "must be a Button action.",
                this);
            return false;
        }

        return true;
    }
}
