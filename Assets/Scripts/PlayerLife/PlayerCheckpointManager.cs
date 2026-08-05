using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Buffs;
using SuperFinalFuse.BombThrowing;

namespace SuperFinalFuse.PlayerLife
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class PlayerCheckpointManager : MonoBehaviour
    {
        [SerializeField] private Transform initialCheckpoint;
        [SerializeField] private Animator playerAnimator;
        [SerializeField] private UnityEvent onCheckpointSaved;
        [SerializeField] private UnityEvent onCheckpointRestored;

        private readonly List<ICheckpointStateParticipant> participants =
            new List<ICheckpointStateParticipant>();
        private readonly List<BombBuffSnapshot> savedBuffs =
            new List<BombBuffSnapshot>();
        private Vector3 savedPosition;
        private Quaternion savedRotation;
        private PlayerState savedPlayerState;
        private pl_stateManager stateManager;
        private BombBuffController buffController;
        private bool hasCheckpoint;
        private Transform currentCheckpoint;

        public bool HasCheckpoint => hasCheckpoint;
        public Vector3 SavedPosition => savedPosition;
        public Transform CurrentCheckpoint => currentCheckpoint;

        private void Awake()
        {
            stateManager = GetComponent<pl_stateManager>();
            buffController = GetComponent<BombBuffController>();
            if (playerAnimator == null)
            {
                playerAnimator = GetComponentInChildren<Animator>();
            }
        }

        private void Start()
        {
            // A checkpoint overlap may be detected before this late-execution
            // Start method. Never overwrite an already activated checkpoint
            // with the configured initial point.
            if (hasCheckpoint)
            {
                return;
            }

            Transform point = initialCheckpoint;
            if (point != null)
            {
                CaptureCheckpoint(point);
            }
            else
            {
                CaptureCheckpoint(transform.position, transform.rotation);
            }
        }

        public void CaptureCheckpoint(Transform checkpoint)
        {
            if (checkpoint == null)
            {
                return;
            }

            currentCheckpoint = checkpoint;
            CaptureCheckpoint(checkpoint.position, checkpoint.rotation);
        }

        public void CaptureCheckpoint(
            Vector3 position,
            Quaternion rotation)
        {
            savedPosition = position;
            savedRotation = rotation;
            savedPlayerState = stateManager != null
                ? stateManager.CurrentState
                : null;
            CaptureBuffs();
            CaptureParticipants();
            hasCheckpoint = true;
            onCheckpointSaved?.Invoke();
        }

        public void RestoreLatestCheckpoint()
        {
            if (!hasCheckpoint)
            {
                return;
            }

            ClearTransientBombObjects();
            foreach (ICheckpointStateParticipant participant in participants)
            {
                if (participant is MonoBehaviour behaviour &&
                    behaviour == null)
                {
                    continue;
                }
                participant?.RestoreCheckpointState();
            }

            RestoreBuffs();
            if (stateManager != null && savedPlayerState != null &&
                stateManager.CurrentState != savedPlayerState)
            {
                stateManager.ChangeState(savedPlayerState);
            }

            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody body in bodies)
            {
                if (body != null && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            transform.SetPositionAndRotation(savedPosition, savedRotation);
            if (playerAnimator != null)
            {
                playerAnimator.Rebind();
                playerAnimator.Update(0f);
            }

            Physics.SyncTransforms();
            onCheckpointRestored?.Invoke();
        }

        private static void ClearTransientBombObjects()
        {
            BombProjectile[] bombs = FindObjectsByType<BombProjectile>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (BombProjectile bomb in bombs)
            {
                if (bomb != null)
                {
                    bomb.Disarm();
                }
            }

            BombBlastWave[] waves = FindObjectsByType<BombBlastWave>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (BombBlastWave wave in waves)
            {
                if (wave != null)
                {
                    Destroy(wave.gameObject);
                }
            }
        }

        private void CaptureParticipants()
        {
            participants.Clear();
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is ICheckpointStateParticipant participant &&
                    behaviour.gameObject != gameObject)
                {
                    participant.CaptureCheckpointState();
                    participants.Add(participant);
                }
            }
        }

        private void CaptureBuffs()
        {
            savedBuffs.Clear();
            if (buffController == null)
            {
                return;
            }

            foreach (BombBuffRuntimeState buff in buffController.ActiveBuffs)
            {
                savedBuffs.Add(new BombBuffSnapshot(
                    buff.Type,
                    buff.Stacks,
                    buff.RemainingDuration,
                    buff.InfiniteDuration));
            }
        }

        private void RestoreBuffs()
        {
            if (buffController == null)
            {
                return;
            }

            buffController.ClearAllBuffs();
            buffController.ClearBombBlastImmunity();
            foreach (BombBuffSnapshot buff in savedBuffs)
            {
                buffController.AddBuff(
                    buff.Type,
                    buff.Stacks,
                    buff.Duration,
                    buff.Infinite);
            }
        }

        private readonly struct BombBuffSnapshot
        {
            public BombBuffSnapshot(
                BombBuffType type,
                int stacks,
                float duration,
                bool infinite)
            {
                Type = type;
                Stacks = stacks;
                Duration = duration;
                Infinite = infinite;
            }

            public BombBuffType Type { get; }
            public int Stacks { get; }
            public float Duration { get; }
            public bool Infinite { get; }
        }
    }
}
