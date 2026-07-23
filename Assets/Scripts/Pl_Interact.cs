using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pl_Interact : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] private pl_Move playerMovement;
    [SerializeField] private pl_stateManager stateManager;
    [SerializeField] private PlayerState movablePlayerState;

    [Header("Detection")]
    [SerializeField] private string interactableTag = "Interactable";

    private readonly List<Collider> nearbyInteractables = new List<Collider>();
    private bool isSubscribed;
    private bool isMovingMovable;
    private Coroutine moveToStandPointRoutine;
    private Movable pendingMovable;
    private Movable activeMovable;
    private PlayerState stateBeforeMoving;

    public int NearbyInteractableCount => nearbyInteractables.Count;

    private void Update()
    {
        if (moveToStandPointRoutine != null &&
            (pendingMovable == null || !pendingMovable.isActiveAndEnabled))
        {
            CancelMoveToStandPoint();
        }

        if (isMovingMovable &&
            (activeMovable == null || !activeMovable.isActiveAndEnabled))
        {
            StopMovableInteraction();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(interactableTag) ||
            nearbyInteractables.Contains(other))
        {
            return;
        }

        nearbyInteractables.Add(other);

        if (nearbyInteractables.Count == 1)
        {
            SubscribeToInput();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!nearbyInteractables.Remove(other))
        {
            return;
        }

        Movable exitedMovable = other.GetComponent<Movable>();

        if (exitedMovable != null && exitedMovable == pendingMovable)
        {
            CancelMoveToStandPoint();
        }

        if (exitedMovable != null && exitedMovable == activeMovable)
        {
            StopMovableInteraction();
        }

        if (nearbyInteractables.Count == 0)
        {
            UnsubscribeFromInput();
        }
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
        if (!isSubscribed)
        {
            return;
        }

        if (inputManager != null)
        {
            inputManager.InteractPressed -= HandleInteractPressed;
        }

        isSubscribed = false;
    }

    private void HandleInteractPressed()
    {
        if (isMovingMovable)
        {
            StopMovableInteraction();
            return;
        }

        if (moveToStandPointRoutine != null)
        {
            return;
        }

        int movableLayer = LayerMask.NameToLayer("Movable");

        foreach (Collider interactable in nearbyInteractables)
        {
            if (interactable == null ||
                interactable.gameObject.layer != movableLayer)
            {
                continue;
            }

            HandleMovable(interactable.gameObject);
            return;
        }
    }

    protected virtual void HandleMovable(GameObject target)
    {
        if (playerMovement == null ||
            stateManager == null ||
            movablePlayerState == null)
        {
            Debug.LogWarning($"{name} is missing a Movable reference.", this);
            return;
        }

        Movable movable = target.GetComponent<Movable>();
        if (movable == null)
        {
            return;
        }

        Transform standPoint = movable.GetStandPoint(transform);
        if (standPoint == null)
        {
            return;
        }

        pendingMovable = movable;
        moveToStandPointRoutine = StartCoroutine(
            MoveToStandPoint(movable, standPoint.position));
    }

    private IEnumerator MoveToStandPoint(
        Movable movable,
        Vector3 targetPosition)
    {
        playerMovement.MoveToTarget(targetPosition);

        while (playerMovement.IsMovingToTarget)
        {
            if (movable == null || !movable.isActiveAndEnabled)
            {
                playerMovement.CancelMoveToTarget();
                pendingMovable = null;
                moveToStandPointRoutine = null;
                yield break;
            }

            yield return null;
        }

        pendingMovable = null;
        moveToStandPointRoutine = null;

        if (!playerMovement.isActiveAndEnabled ||
            movable == null ||
            !movable.isActiveAndEnabled)
        {
            yield break;
        }

        stateBeforeMoving = stateManager.CurrentState;

        if (stateBeforeMoving != movablePlayerState &&
            !stateManager.ChangeState(movablePlayerState))
        {
            stateBeforeMoving = null;
            yield break;
        }

        activeMovable = movable;
        isMovingMovable = true;
        playerMovement.MovementApplied += MoveActiveMovable;
    }

    private void MoveActiveMovable(Vector3 worldDelta)
    {
        if (activeMovable != null)
        {
            activeMovable.Move(worldDelta);
        }
    }

    private void CancelMoveToStandPoint()
    {
        bool wasMovingToStandPoint = moveToStandPointRoutine != null;

        if (moveToStandPointRoutine != null)
        {
            StopCoroutine(moveToStandPointRoutine);
            moveToStandPointRoutine = null;
        }

        pendingMovable = null;

        if (wasMovingToStandPoint && playerMovement != null)
        {
            playerMovement.CancelMoveToTarget();
        }
    }

    private void StopMovableInteraction()
    {
        if (playerMovement != null)
        {
            playerMovement.MovementApplied -= MoveActiveMovable;
        }

        PlayerState previousState = stateBeforeMoving;
        bool restoreState = isMovingMovable &&
                            stateManager != null &&
                            stateManager.CurrentState == movablePlayerState;

        isMovingMovable = false;
        activeMovable = null;
        stateBeforeMoving = null;

        if (restoreState &&
            previousState != null &&
            previousState != movablePlayerState)
        {
            stateManager.ChangeState(previousState);
        }
    }

    private void OnDisable()
    {
        CancelMoveToStandPoint();
        StopMovableInteraction();
        UnsubscribeFromInput();
        nearbyInteractables.Clear();
    }
}
