using System;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing;
using SuperFinalFuse.BombThrowing.Buffs;

namespace SuperFinalFuse.PlayerLife
{
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : MonoBehaviour,
        IPlayerDamageable,
        IBombBlastReactive
    {
        [Header("Health")]
        [Min(1)]
        [SerializeField] private int maximumHealth = 2;
        [SerializeField, Min(0)] private int currentHealth = 2;

        [Header("Bomb damage")]
        [Min(0)]
        [SerializeField] private int normalBombBlastDamage = 1;
        [Min(0)]
        [SerializeField] private int ownBombBlastDamage = 2;

        [Header("Events")]
        [SerializeField] private UnityEvent<int> onHealthChanged;
        [SerializeField] private UnityEvent onDied;
        [SerializeField] private UnityEvent onFullHealthRestored;

        private bool isDead;

        public event Action<PlayerDamage> Damaged;
        public event Action Died;
        public int MaximumHealth => maximumHealth;
        public int CurrentHealth => currentHealth;
        public bool IsDead => isDead;

        private void Awake()
        {
            maximumHealth = Mathf.Max(1, maximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0, maximumHealth);
            if (currentHealth <= 0)
            {
                currentHealth = maximumHealth;
            }
        }

        public bool TakeDamage(PlayerDamage damage)
        {
            if (isDead || damage.Amount <= 0)
            {
                return false;
            }

            currentHealth = Mathf.Max(0, currentHealth - damage.Amount);
            Damaged?.Invoke(damage);
            onHealthChanged?.Invoke(currentHealth);
            if (currentHealth > 0)
            {
                return true;
            }

            isDead = true;
            Died?.Invoke();
            onDied?.Invoke();
            return true;
        }

        public void OnBombBlast(BombBlastHit hit)
        {
            BombBuffController buffs = GetComponent<BombBuffController>();
            if (buffs != null && buffs.IsBombBlastImmune)
            {
                return;
            }

            bool isOwnBomb = hit.Owner != null &&
                (hit.Owner == gameObject ||
                 transform.IsChildOf(hit.Owner.transform) ||
                 hit.Owner.transform.IsChildOf(transform));
            TakeDamage(new PlayerDamage(
                isOwnBomb ? ownBombBlastDamage : normalBombBlastDamage,
                isOwnBomb
                    ? PlayerDamageType.OwnBombBlast
                    : PlayerDamageType.BombBlast,
                hit.Owner));
        }

        public void Kill(PlayerDamageType type, GameObject source = null)
        {
            TakeDamage(new PlayerDamage(currentHealth, type, source));
        }

        public void RestoreFullHealth()
        {
            maximumHealth = Mathf.Max(1, maximumHealth);
            currentHealth = maximumHealth;
            isDead = false;
            onHealthChanged?.Invoke(currentHealth);
            onFullHealthRestored?.Invoke();
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1, maximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0, maximumHealth);
        }
    }
}
