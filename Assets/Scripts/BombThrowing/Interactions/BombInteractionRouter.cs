using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Interactions
{
    public static class BombInteractionRouter
    {
        public static BombHitResult Route(BombDirectHit hit)
        {
            if (hit.Collider == null)
            {
                return BombHitResult.Ignore;
            }

            MonoBehaviour[] behaviours =
                hit.Collider.GetComponentsInParent<MonoBehaviour>(true);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is not IBombDirectHitReceiver receiver)
                {
                    continue;
                }

                BombHitResult result = receiver.ReceiveBombHit(hit);
                if (result != BombHitResult.Ignore)
                {
                    return result;
                }
            }

            return BombHitResult.Ignore;
        }
    }
}
