using UnityEngine;

[CreateAssetMenu(
    fileName = "NewPlayerState",
    menuName = "Player/Player State")]
public sealed class PlayerState : ScriptableObject
{
    [Header("Movement")]
    [SerializeField] private bool canPlayerControlMovement = true;
    [SerializeField] private bool canPlayerTurn = true;
    [Min(0f)]
    [SerializeField] private float maxMoveSpeed = 5f;
    [Min(0f)]
    [SerializeField] private float maxStepOffset = 0.3f;

    public bool CanPlayerControlMovement => canPlayerControlMovement;
    public bool CanPlayerTurn => canPlayerTurn;
    public float MaxMoveSpeed => maxMoveSpeed;
    public float MaxStepOffset => maxStepOffset;
}
