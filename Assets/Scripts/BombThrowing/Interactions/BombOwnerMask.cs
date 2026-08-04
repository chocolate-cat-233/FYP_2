using System;

namespace SuperFinalFuse.BombThrowing.Interactions
{
    [Flags]
    public enum BombOwnerMask
    {
        None = 0,
        Player = 1 << 0,
        Generator = 1 << 1,
        Environment = 1 << 2,
        All = Player | Generator | Environment
    }

    public static class BombOwnerMaskUtility
    {
        public static bool Contains(this BombOwnerMask mask, BombOwnerType type)
        {
            BombOwnerMask value = type switch
            {
                BombOwnerType.Player => BombOwnerMask.Player,
                BombOwnerType.Generator => BombOwnerMask.Generator,
                _ => BombOwnerMask.Environment
            };
            return (mask & value) != 0;
        }
    }
}
