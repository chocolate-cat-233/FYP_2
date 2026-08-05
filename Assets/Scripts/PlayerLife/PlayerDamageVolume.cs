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
        [Min(0.01f)]
        [SerializeField] private float repeatedDamageInterval = 1f;

        private Collider damageVolume;
        private PlayerHealth playerInside;
        private float nextRepeatedDamageTime;

        private void Awake()
        {
            damageVolume = GetComponent<Collider>();
            damageVolume.isTrigger = true;
        }

        private void Reset()
        {
            Collider volume = GetComponent<Collider>();
            volume.isTrigger = true;
        }

        private void Update()
        {
            PlayerHealth health = playerInside != null
                ? playerInside
                : FindFirstObjectByType<PlayerHealth>();
            if (health == null || damageVolume == null)
            {
                return;
            }

            bool overlapsPlayer = false;
            Collider[] playerColliders =
                health.GetComponentsInChildren<Collider>();
            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null && playerCollider.enabled &&
                    !playerCollider.isTrigger &&
                    damageVolume.bounds.Intersects(playerCollider.bounds))
                {
                    overlapsPlayer = true;
                    break;
                }
            }

            if (!overlapsPlayer)
            {
                if (playerInside == health)
                {
                    playerInside = null;
                }
                return;
            }

            if (playerInside == null)
            {
                EnterDamageVolume(health);
            }
            else if (!damageOncePerEntry &&
                     Time.time >= nextRepeatedDamageTime)
            {
                ApplyDamage(health);
                nextRepeatedDamageTime =
                    Time.time + repeatedDamageInterval;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PlayerHealth health =
                other.GetComponentInParent<PlayerHealth>();
            if (health != null && playerInside != health)
            {
                EnterDamageVolume(health);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!damageOncePerEntry &&
                Time.time >= nextRepeatedDamageTime)
            {
                PlayerHealth health =
                    other.GetComponentInParent<PlayerHealth>();
                if (health != null)
                {
                    ApplyDamage(health);
                    nextRepeatedDamageTime =
                        Time.time + repeatedDamageInterval;
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerHealth health =
                other.GetComponentInParent<PlayerHealth>();
            if (health != null && playerInside == health)
            {
                playerInside = null;
            }
        }

        private void EnterDamageVolume(PlayerHealth health)
        {
            playerInside = health;
            ApplyDamage(health);
            nextRepeatedDamageTime = Time.time + repeatedDamageInterval;
        }

        private void ApplyDamage(PlayerHealth health)
        {
            health.TakeDamage(new PlayerDamage(
                damage,
                damageType,
                gameObject));
        }
    }
}
