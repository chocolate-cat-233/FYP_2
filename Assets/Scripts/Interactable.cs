using System.Collections.Generic;
using UnityEngine;

public class Interactable : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private Collider interactionTrigger;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";

    private readonly HashSet<Collider> playerColliders = new HashSet<Collider>();
    private bool isSubscribed;

    public bool IsActive { get; private set; }
    public bool IsPlayerInRange => playerColliders.Count > 0;

    private void Reset()
    {
        EnsureTriggerCollider();
    }

    private void Awake()
    {
        EnsureTriggerCollider();

        if (interactionTrigger == null || !interactionTrigger.isTrigger)
        {
            Debug.LogError(
                $"{nameof(Interactable)} on '{name}' requires a trigger Collider.",
                this);
            enabled = false;
        }

        if (inputManager == null)
        {
            Debug.LogError(
                $"{nameof(Interactable)} on '{name}' requires an InputManager reference.",
                this);
            enabled = false;
        }
    }

    private void EnsureTriggerCollider()
    {
        if (interactionTrigger == null || !interactionTrigger.isTrigger)
        {
            interactionTrigger = FindTriggerCollider();
        }

        if (interactionTrigger == null)
        {
            interactionTrigger = gameObject.AddComponent<BoxCollider>();
            interactionTrigger.isTrigger = true;
        }
    }

    private Collider FindTriggerCollider()
    {
        Collider[] colliders = GetComponents<Collider>();

        foreach (Collider currentCollider in colliders)
        {
            if (currentCollider.isTrigger)
            {
                return currentCollider;
            }
        }

        return null;
    }

    private void OnDisable()
    {
        UnsubscribeFromInput();
        playerColliders.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled || !IsPlayerCollider(other))
        {
            return;
        }

        playerColliders.Add(other);
        SubscribeToInput();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!playerColliders.Remove(other) || IsPlayerInRange)
        {
            return;
        }

        UnsubscribeFromInput();
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other.GetComponentInParent<pl_Move>() != null)
        {
            return true;
        }

        if (other.CompareTag(playerTag))
        {
            return true;
        }

        return other.transform.root.CompareTag(playerTag);
    }

    private void SubscribeToInput()
    {
        if (isSubscribed || inputManager == null)
        {
            return;
        }

        inputManager.InteractPressed += HandleInteractPressed;
        isSubscribed = true;
    }

    private void UnsubscribeFromInput()
    {
        if (!isSubscribed || inputManager == null)
        {
            return;
        }

        inputManager.InteractPressed -= HandleInteractPressed;
        isSubscribed = false;
    }

    private void HandleInteractPressed()
    {
        if (!IsPlayerInRange)
        {
            return;
        }

        IsActive = !IsActive;
        OnInteract(IsActive);
    }

    /// <summary>
    /// Called after the mechanism state changes.
    /// Override this method in subclasses to implement the mechanism behavior.
    /// </summary>
    protected virtual void OnInteract(bool isActive)
    {
    }
}
