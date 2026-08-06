using System.Collections.Generic;
using UnityEngine;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class EnemyChargeHitbox : MonoBehaviour
    {
        [SerializeField] private ChargingEnemyController controller;

        private readonly Dictionary<PlayerHealth, HashSet<Collider>>
            overlappingColliders =
                new Dictionary<PlayerHealth, HashSet<Collider>>();
        private readonly HashSet<PlayerHealth> damagedDuringContact =
            new HashSet<PlayerHealth>();

        internal void SetController(ChargingEnemyController owner)
        {
            controller = owner;
        }

        private void OnTriggerEnter(Collider other)
        {
            TrackCollider(other);
            TryDamage(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TrackCollider(other);
            TryDamage(other);
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerHealth health = other != null
                ? other.GetComponentInParent<PlayerHealth>()
                : null;
            if (health == null ||
                !overlappingColliders.TryGetValue(
                    health,
                    out HashSet<Collider> colliders))
            {
                return;
            }

            colliders.Remove(other);
            if (colliders.Count > 0)
            {
                return;
            }

            overlappingColliders.Remove(health);
            damagedDuringContact.Remove(health);
        }

        private void OnDisable()
        {
            overlappingColliders.Clear();
            damagedDuringContact.Clear();
        }

        private void TrackCollider(Collider other)
        {
            PlayerHealth health = other != null
                ? other.GetComponentInParent<PlayerHealth>()
                : null;
            if (health == null)
            {
                return;
            }

            if (!overlappingColliders.TryGetValue(
                    health,
                    out HashSet<Collider> colliders))
            {
                colliders = new HashSet<Collider>();
                overlappingColliders.Add(health, colliders);
            }

            colliders.Add(other);
        }

        private void TryDamage(Collider other)
        {
            PlayerHealth health = other != null
                ? other.GetComponentInParent<PlayerHealth>()
                : null;
            if (health == null ||
                damagedDuringContact.Contains(health) ||
                controller == null)
            {
                return;
            }

            if (controller.TryHitPlayer(other))
            {
                damagedDuringContact.Add(health);
            }
        }
    }
}
