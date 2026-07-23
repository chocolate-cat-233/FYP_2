using UnityEngine;

public class Movable : MonoBehaviour
{
    public Transform[] standPoint;

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
        worldDelta.y = 0f;
        transform.position += worldDelta;
    }
}
