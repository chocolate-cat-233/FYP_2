using UnityEngine;

public class Movable : MonoBehaviour
{
    public Transform[] standPoint;

    private bool canMove;
    private Rigidbody movableRigidbody;
    private RigidbodyConstraints unlockedConstraints;

    public bool CanMove => canMove;

    private void Awake()
    {
        movableRigidbody = GetComponent<Rigidbody>();

        if (movableRigidbody == null)
        {
            Debug.LogError(
                $"{nameof(Movable)} on '{name}' requires a Rigidbody.",
                this);
            return;
        }

        unlockedConstraints = movableRigidbody.constraints;
        movableRigidbody.constraints =
            unlockedConstraints | RigidbodyConstraints.FreezePosition;
        movableRigidbody.Sleep();
    }

    public void SetCanMove(bool value)
    {
        canMove = value;

        if (movableRigidbody == null)
        {
            return;
        }

        if (canMove)
        {
            movableRigidbody.constraints = unlockedConstraints;
            movableRigidbody.WakeUp();
            return;
        }

        movableRigidbody.linearVelocity = Vector3.zero;
        movableRigidbody.angularVelocity = Vector3.zero;
        movableRigidbody.constraints =
            unlockedConstraints | RigidbodyConstraints.FreezePosition;
        movableRigidbody.Sleep();
    }

    public Transform GetStandPoint(Transform playerTransform)
    {
        if (standPoint == null || standPoint.Length == 0)
        {
            Debug.LogWarning($"{name} has no Stand Point.", this);
            return null;
        }

        Transform closestStandPoint = null;
        float shortestDistance = float.PositiveInfinity;

        foreach (Transform point in standPoint)
        {
            if (point == null)
            {
                continue;
            }

            float distance = Vector3.Distance(
                point.position,
                playerTransform.position);

            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                closestStandPoint = point;
            }
        }

        if (closestStandPoint == null)
        {
            Debug.LogWarning($"{name} has no valid Stand Point.", this);
        }

        return closestStandPoint;
    }

    public void Move(Vector3 worldDelta)
    {
        if (!canMove)
        {
            return;
        }

        worldDelta.y = 0f;
        transform.position += worldDelta;
    }
}
