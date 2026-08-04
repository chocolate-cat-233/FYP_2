using UnityEngine;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing.Buffs
{
    [DisallowMultipleComponent]
    public sealed class BombTeleportTarget : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [SerializeField] private Transform destination;
        [SerializeField] private bool copyDestinationRotation = true;
        [Min(0f)]
        [Tooltip("Bomb-blast immunity granted after a successful teleport.")]
        [SerializeField] private float postTeleportImmunityDuration = 1f;

        public bool TryTeleport(GameObject throwingPlayer)
        {
            if (throwingPlayer == null || destination == null)
            {
                return false;
            }

            BombBuffController buffs =
                throwingPlayer.GetComponent<BombBuffController>();
            if (buffs == null ||
                !buffs.HasBuff(BombBuffType.TeleportBomb))
            {
                return false;
            }

            Rigidbody[] bodies =
                throwingPlayer.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody body in bodies)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Quaternion rotation = copyDestinationRotation
                ? destination.rotation
                : throwingPlayer.transform.rotation;
            throwingPlayer.transform.SetPositionAndRotation(
                destination.position,
                rotation);
            Physics.SyncTransforms();

            buffs.GrantBombBlastImmunity(postTeleportImmunityDuration);
            buffs.TryConsume(BombBuffType.TeleportBomb);
            return true;
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (hit.OwnerType != BombOwnerType.Player)
            {
                return BombHitResult.Ignore;
            }

            return TryTeleport(hit.Owner)
                ? BombHitResult.Explode
                : BombHitResult.Ignore;
        }
    }
}
