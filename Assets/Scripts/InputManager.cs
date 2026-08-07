using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputManager : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference interactAction;

    private bool isInputReady;
    private InputAction bombAimAction;
    private InputAction bombThrowAction;

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
    public bool BombAimHeld =>
        isInputReady && bombAimAction != null && bombAimAction.IsPressed();
    public bool BombAimPressedThisFrame =>
        isInputReady && bombAimAction != null &&
        bombAimAction.WasPressedThisFrame();
    public bool BombThrowPressedThisFrame =>
        isInputReady && bombThrowAction != null &&
        bombThrowAction.WasPressedThisFrame();

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
        bombAimAction.Enable();
        bombThrowAction.Enable();
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
        bombAimAction?.Disable();
        bombThrowAction?.Disable();
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

        InputActionMap playerMap = moveAction.action.actionMap;
        bombAimAction = playerMap?.FindAction("BombAim", false);
        bombThrowAction = playerMap?.FindAction("BombThrow", false);
        if (bombAimAction == null || bombThrowAction == null)
        {
            Debug.LogError(
                $"{nameof(InputManager)} on '{name}' requires BombAim and " +
                "BombThrow actions in the same action map as Move.",
                this);
            return false;
        }

        if (bombAimAction.type != InputActionType.Button ||
            bombThrowAction.type != InputActionType.Button)
        {
            Debug.LogError(
                "BombAim and BombThrow must both be Button actions.",
                this);
            return false;
        }

        return true;
    }
}
