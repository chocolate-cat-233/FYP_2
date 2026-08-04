using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Buffs
{
    [DisallowMultipleComponent]
    public sealed class BombBuffController : MonoBehaviour
    {
        [SerializeField] private List<BombBuffRuntimeState> activeBuffs =
            new List<BombBuffRuntimeState>();

        private float bombBlastImmunityUntil;

        public event Action BuffsChanged;
        public IReadOnlyList<BombBuffRuntimeState> ActiveBuffs => activeBuffs;
        public bool IsBombBlastImmune =>
            Time.time < bombBlastImmunityUntil;
        public float BombBlastImmunityRemaining => Mathf.Max(
            0f,
            bombBlastImmunityUntil - Time.time);

        private void Update()
        {
            bool changed = false;
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                if (!activeBuffs[i].Tick(Time.deltaTime))
                {
                    activeBuffs.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                BuffsChanged?.Invoke();
            }
        }

        public bool HasBuff(BombBuffType type)
        {
            return GetState(type) != null;
        }

        public int GetStacks(BombBuffType type)
        {
            return GetState(type)?.Stacks ?? 0;
        }

        public void AddBuff(
            BombBuffType type,
            int stacks = 1,
            float duration = 30f,
            bool infiniteDuration = false)
        {
            BombBuffRuntimeState state = GetState(type);
            if (state == null)
            {
                activeBuffs.Add(new BombBuffRuntimeState(
                    type,
                    Mathf.Max(1, stacks),
                    duration,
                    infiniteDuration));
            }
            else
            {
                state.AddStacks(Mathf.Max(1, stacks));
                state.RefreshDuration(duration, infiniteDuration);
            }

            BuffsChanged?.Invoke();
        }

        public bool RemoveBuff(BombBuffType type)
        {
            BombBuffRuntimeState state = GetState(type);
            bool removed = state != null && activeBuffs.Remove(state);
            if (removed)
            {
                BuffsChanged?.Invoke();
            }

            return removed;
        }

        public bool AddStacks(BombBuffType type, int amount = 1)
        {
            BombBuffRuntimeState state = GetState(type);
            if (state == null || amount <= 0)
            {
                return false;
            }

            state.AddStacks(amount);
            BuffsChanged?.Invoke();
            return true;
        }

        public bool RemoveStacks(BombBuffType type, int amount = 1)
        {
            BombBuffRuntimeState state = GetState(type);
            if (state == null || amount <= 0)
            {
                return false;
            }

            state.AddStacks(-amount);
            if (state.Stacks <= 0)
            {
                activeBuffs.Remove(state);
            }

            BuffsChanged?.Invoke();
            return true;
        }

        public bool TryConsume(BombBuffType type, int amount = 1)
        {
            BombBuffRuntimeState state = GetState(type);
            if (state == null || state.Stacks < amount || amount <= 0)
            {
                return false;
            }

            return RemoveStacks(type, amount);
        }

        public void ClearAllBuffs()
        {
            if (activeBuffs.Count == 0)
            {
                return;
            }

            activeBuffs.Clear();
            BuffsChanged?.Invoke();
        }

        public void GrantBombBlastImmunity(float duration)
        {
            if (duration <= 0f)
            {
                return;
            }

            bombBlastImmunityUntil = Mathf.Max(
                bombBlastImmunityUntil,
                Time.time + duration);
            BuffsChanged?.Invoke();
        }

        public void ClearBombBlastImmunity()
        {
            bombBlastImmunityUntil = 0f;
            BuffsChanged?.Invoke();
        }

        private BombBuffRuntimeState GetState(BombBuffType type)
        {
            return activeBuffs.Find(state => state.Type == type);
        }
    }
}
