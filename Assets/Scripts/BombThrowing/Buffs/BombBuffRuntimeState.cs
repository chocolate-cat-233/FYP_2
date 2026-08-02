using System;
using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Buffs
{
    [Serializable]
    public sealed class BombBuffRuntimeState
    {
        [SerializeField] private BombBuffType type;
        [SerializeField, Min(1)] private int stacks = 1;
        [SerializeField] private bool infiniteDuration;
        [SerializeField, Min(0f)] private float remainingDuration;

        public BombBuffType Type => type;
        public int Stacks => stacks;
        public bool InfiniteDuration => infiniteDuration;
        public float RemainingDuration => remainingDuration;

        public BombBuffRuntimeState(
            BombBuffType buffType,
            int initialStacks,
            float duration,
            bool infinite)
        {
            type = buffType;
            stacks = Mathf.Max(1, initialStacks);
            infiniteDuration = infinite;
            remainingDuration = Mathf.Max(0f, duration);
        }

        public void AddStacks(int amount)
        {
            stacks = Mathf.Max(0, stacks + amount);
        }

        public void RefreshDuration(float duration, bool infinite)
        {
            infiniteDuration = infinite;
            if (!infiniteDuration)
            {
                remainingDuration = Mathf.Max(0f, duration);
            }
        }

        public bool Tick(float deltaTime)
        {
            if (infiniteDuration)
            {
                return stacks > 0;
            }

            remainingDuration = Mathf.Max(0f, remainingDuration - deltaTime);
            return stacks > 0 && remainingDuration > 0f;
        }
    }
}
