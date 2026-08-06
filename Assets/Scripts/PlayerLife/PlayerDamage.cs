using UnityEngine;

namespace SuperFinalFuse.PlayerLife
{
    public enum PlayerDamageType
    {
        Generic,
        Lava,
        BombBlast,
        OwnBombBlast,
        EnemyCharge,
        Void
    }

    public readonly struct PlayerDamage
    {
        public PlayerDamage(
            int amount,
            PlayerDamageType type,
            GameObject source = null)
        {
            Amount = Mathf.Max(0, amount);
            Type = type;
            Source = source;
        }

        public int Amount { get; }
        public PlayerDamageType Type { get; }
        public GameObject Source { get; }
    }

    public interface IPlayerDamageable
    {
        bool TakeDamage(PlayerDamage damage);
    }
}
