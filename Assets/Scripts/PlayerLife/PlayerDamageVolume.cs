using UnityEngine;

namespace SuperFinalFuse.PlayerLife
{
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class PlayerDamageVolume : MonoBehaviour
    {
        [Min(1)]
        [SerializeField] private int damage = 2;
        [SerializeField] private PlayerDamageType damageType =
            PlayerDamageType.Lava;
        [SerializeField] private bool damageOncePerEntry = true;

        private void Reset()
        {
            Collider volume = GetComponent<Collider>();
            volume.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Damage(other);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!damageOncePerEntry)
            {
                Damage(other);
            }
        }

        private void Damage(Collider other)
        {
            PlayerHealth health =
                other.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                health.TakeDamage(new PlayerDamage(
                    damage,
                    damageType,
                    gameObject));
            }
        }
    }
}
