using System;
using System.Collections.Generic;
using UnityEngine;

public class pl_stateManager : MonoBehaviour
{
    [Header("Player States")]
    [SerializeField] private List<PlayerState> states = new List<PlayerState>();
    [SerializeField] private PlayerState initialState;

    public PlayerState CurrentState { get; private set; }

    public event Action<PlayerState> StateChanged;

    private void Awake()
    {
        if (initialState == null)
        {
            Debug.LogError(
                $"{nameof(pl_stateManager)} on '{name}' requires an Initial State.",
                this);
            return;
        }

        if (!states.Contains(initialState))
        {
            Debug.LogError(
                $"Initial State '{initialState.name}' on '{name}' must exist in the States list.",
                this);
            return;
        }

        CurrentState = initialState;
    }

    public bool ChangeState(PlayerState newState)
    {
        if (newState == null)
        {
            Debug.LogWarning(
                $"{nameof(pl_stateManager)} on '{name}' cannot change to a null state.",
                this);
            return false;
        }

        if (!states.Contains(newState))
        {
            Debug.LogWarning(
                $"Player State '{newState.name}' is not registered on '{name}'.",
                this);
            return false;
        }

        if (CurrentState == newState)
        {
            return false;
        }

        CurrentState = newState;
        StateChanged?.Invoke(CurrentState);
        return true;
    }
}
