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
    private Vector3 playerOffsetFromMovable;

    public int NearbyInteractableCount => nearbyInteractables.Count;

    private void Update()
    {
        RemoveInvalidInteractables();

        if (moveToStandPointRoutine != null &&
            (pendingMovable == null || !pendingMovable.isActiveAndEnabled))
        {
            CancelMoveToStandPoint();
        }

        if (isMovingMovable &&
            (activeMovable == null ||
             !activeMovable.isActiveAndEnabled ||
             playerMovement == null ||
             !playerMovement.isActiveAndEnabled))
        {
            StopMovableInteraction();
        }
    }

    private void LateUpdate()
    {
        if (!isMovingMovable ||
            activeMovable == null ||
            !activeMovable.isActiveAndEnabled)
        {
            return;
        }

        Vector3 positionBeforeFollow = transform.position;
        Vector3 playerPosition =
            activeMovable.transform.position + playerOffsetFromMovable;
        playerPosition.y = transform.position.y;
        transform.position = playerPosition;

        if (playerMovement != null)
        {
            playerMovement.ReportHorizontalMovementApplied(
                playerPosition - positionBeforeFollow);
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

        if (nearbyInteractables.Count == 0 && !isMovingMovable)
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



        foreach (Collider interactable in nearbyInteractables)
        {
            if ( interactable.gameObject.layer == LayerMask.NameToLayer("Movable"))
            {
                HandleMovable(interactable.gameObject);
                return;
            }

            if (layer == LayerMask.NameToLayer("Pump"))
            {
                HandlePump(interactable.gameObject);
                return;
            }
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

    protected void HandlePump(GameObject target)
    {
        target.GetComponent<HeatSystem.HeatNode>().Interact();
    }


    private IEnumerator MoveToStandPoint(
        Movable movable,
        Vector3 targetPosition)
    {
        playerMovement.MoveToTarget(
            targetPosition,
            movable.transform.position);

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
        activeMovable.SetCanMove(true);

        playerOffsetFromMovable =
            transform.position - activeMovable.transform.position;
        playerOffsetFromMovable.y = 0f;

        if (!playerMovement.TrySetHorizontalMovementDriver(
                MoveActiveMovable))
        {
            Debug.LogWarning(
                $"{name} could not start moving '{movable.name}' because " +
                "the horizontal movement driver is already in use.",
                this);
            RollBackMovableStart();
            yield break;
        }

        isMovingMovable = true;
    }

    private void MoveActiveMovable(Vector3 worldDelta)
    {
        if (activeMovable == null)
        {
            return;
        }

        Vector3 playerLocalDelta =
            transform.InverseTransformDirection(worldDelta);
        playerLocalDelta.x = 0f;

        Vector3 movableWorldDelta =
            transform.TransformDirection(playerLocalDelta);
        movableWorldDelta.y = 0f;

        activeMovable.Move(movableWorldDelta);
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

        if (nearbyInteractables.Count == 0 && !isMovingMovable)
        {
            UnsubscribeFromInput();
        }
    }

    private void StopMovableInteraction()
    {
        if (playerMovement != null)
        {
            playerMovement.ClearHorizontalMovementDriver(
                MoveActiveMovable);
        }

        PlayerState previousState = stateBeforeMoving;
        bool restoreState = isMovingMovable &&
                            stateManager != null &&
                            stateManager.CurrentState == movablePlayerState;

        if (activeMovable != null)
        {
            activeMovable.SetCanMove(false);
        }

        isMovingMovable = false;
        activeMovable = null;
        stateBeforeMoving = null;
        playerOffsetFromMovable = Vector3.zero;

        if (restoreState &&
            previousState != null &&
            previousState != movablePlayerState)
        {
            stateManager.ChangeState(previousState);
        }

        if (nearbyInteractables.Count == 0)
        {
            UnsubscribeFromInput();
        }
    }

    private void RollBackMovableStart()
    {
        PlayerState previousState = stateBeforeMoving;
        bool restoreState =
            stateManager != null &&
            stateManager.CurrentState == movablePlayerState;

        if (activeMovable != null)
        {
            activeMovable.SetCanMove(false);
        }

        isMovingMovable = false;
        activeMovable = null;
        stateBeforeMoving = null;
        playerOffsetFromMovable = Vector3.zero;

        if (restoreState &&
            previousState != null &&
            previousState != movablePlayerState)
        {
            stateManager.ChangeState(previousState);
        }

        if (nearbyInteractables.Count == 0)
        {
            UnsubscribeFromInput();
        }
    }

    private void RemoveInvalidInteractables()
    {
        for (int i = nearbyInteractables.Count - 1; i >= 0; i--)
        {
            Collider interactable = nearbyInteractables[i];

            if (interactable == null ||
                !interactable.enabled ||
                !interactable.gameObject.activeInHierarchy)
            {
                nearbyInteractables.RemoveAt(i);
            }
        }

        if (moveToStandPointRoutine != null &&
            !ContainsMovable(pendingMovable))
        {
            CancelMoveToStandPoint();
        }

        if (nearbyInteractables.Count == 0 &&
            moveToStandPointRoutine == null &&
            !isMovingMovable)
        {
            UnsubscribeFromInput();
            return;
        }

        if (nearbyInteractables.Count > 0)
        {
            SubscribeToInput();
        }
    }

    private bool ContainsMovable(Movable movable)
    {
        if (movable == null)
        {
            return false;
        }

        for (int i = 0; i < nearbyInteractables.Count; i++)
        {
            Collider interactable = nearbyInteractables[i];

            if (interactable != null &&
                interactable.GetComponent<Movable>() == movable)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDisable()
    {
        CancelMoveToStandPoint();
        StopMovableInteraction();
        UnsubscribeFromInput();
        nearbyInteractables.Clear();
    }
}
