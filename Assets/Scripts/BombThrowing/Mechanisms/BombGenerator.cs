using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    [DisallowMultipleComponent]
    public sealed class BombGenerator : MonoBehaviour,
        ICheckpointStateParticipant
    {
        [Header("References")]
        [SerializeField] private BombProjectile bombPrefab;
        [Tooltip("Bombs spawn here and travel along this Transform's forward axis.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Only these generator colliders are ignored by spawned bombs. " +
                 "Do not assign unrelated mechanism colliders.")]
        [SerializeField] private Collider[] generatorCollidersToIgnore;

        [Header("Generation")]
        [Min(0.01f)]
        [SerializeField] private float spawnInterval = 2f;
        [Min(0f)]
        [SerializeField] private float launchSpeed = 8f;
        [SerializeField] private bool startGenerating = true;
        [SerializeField] private bool spawnImmediately = true;
        [Min(0)]
        [Tooltip("Zero means unlimited. Completed bombs no longer count toward the limit.")]
        [SerializeField] private int maximumActiveBombs;

        [Header("Events")]
        [SerializeField] private UnityEvent onGenerationStarted;
        [SerializeField] private UnityEvent onGenerationStopped;
        [SerializeField] private UnityEvent onBombGenerated;

        private readonly HashSet<BombProjectile> activeBombs =
            new HashSet<BombProjectile>();

        private float timeUntilNextBomb;
        private bool savedGenerating;
        private float savedTimeUntilNextBomb;

        public bool IsGenerating { get; private set; }
        public int ActiveBombCount
        {
            get
            {
                RemoveDestroyedBombReferences();
                return activeBombs.Count;
            }
        }

        private void Awake()
        {
            if (spawnPoint == null)
            {
                spawnPoint = transform;
            }

            if (generatorCollidersToIgnore == null ||
                generatorCollidersToIgnore.Length == 0)
            {
                // Deliberately use only this GameObject. Broad child searches
                // can accidentally include unrelated level mechanisms.
                generatorCollidersToIgnore = GetComponents<Collider>();
            }
        }

        private void Start()
        {
            if (startGenerating)
            {
                StartGenerating();
            }
        }

        private void Update()
        {
            if (!IsGenerating)
            {
                return;
            }

            RemoveDestroyedBombReferences();
            timeUntilNextBomb -= Time.deltaTime;
            if (timeUntilNextBomb > 0f)
            {
                return;
            }

            if (maximumActiveBombs > 0 &&
                activeBombs.Count >= maximumActiveBombs)
            {
                // Keep the timer ready so a replacement is generated as soon
                // as an active bomb completes.
                timeUntilNextBomb = 0f;
                return;
            }

            GenerateOnce();
            timeUntilNextBomb = spawnInterval;
        }

        public void StartGenerating()
        {
            if (IsGenerating)
            {
                return;
            }

            IsGenerating = true;
            timeUntilNextBomb = spawnImmediately ? 0f : spawnInterval;
            onGenerationStarted?.Invoke();
        }

        public void StopGenerating()
        {
            if (!IsGenerating)
            {
                return;
            }

            IsGenerating = false;
            onGenerationStopped?.Invoke();
        }

        public void ToggleGenerating()
        {
            if (IsGenerating)
            {
                StopGenerating();
            }
            else
            {
                StartGenerating();
            }
        }

        [ContextMenu("Generate One Bomb")]
        public BombProjectile GenerateOnce()
        {
            if (bombPrefab == null || spawnPoint == null)
            {
                Debug.LogWarning(
                    $"{nameof(BombGenerator)} on '{name}' requires a Bomb " +
                    "Prefab and Spawn Point.",
                    this);
                return null;
            }

            RemoveDestroyedBombReferences();
            if (maximumActiveBombs > 0 &&
                activeBombs.Count >= maximumActiveBombs)
            {
                return null;
            }

            Vector3 direction = spawnPoint.forward.sqrMagnitude > 0.001f
                ? spawnPoint.forward.normalized
                : Vector3.forward;
            BombProjectile bomb = Instantiate(
                bombPrefab,
                spawnPoint.position,
                Quaternion.LookRotation(direction, Vector3.up));
            bomb.Completed += HandleBombCompleted;
            activeBombs.Add(bomb);
            bomb.Launch(
                direction * launchSpeed,
                gameObject,
                BombOwnerType.Generator);
            bomb.IgnoreCollisionsWith(generatorCollidersToIgnore);
            onBombGenerated?.Invoke();
            return bomb;
        }

        /// <summary>Void wrapper for buttons and UnityEvents.</summary>
        public void GenerateBomb()
        {
            GenerateOnce();
        }

        private void HandleBombCompleted(BombProjectile bomb)
        {
            if (bomb != null)
            {
                bomb.Completed -= HandleBombCompleted;
            }

            activeBombs.Remove(bomb);
        }

        private void RemoveDestroyedBombReferences()
        {
            activeBombs.RemoveWhere(bomb => bomb == null);
        }

        private void OnDestroy()
        {
            foreach (BombProjectile bomb in activeBombs)
            {
                if (bomb != null)
                {
                    bomb.Completed -= HandleBombCompleted;
                }
            }

            activeBombs.Clear();
        }

        public void CaptureCheckpointState()
        {
            savedGenerating = IsGenerating;
            savedTimeUntilNextBomb = timeUntilNextBomb;
        }

        public void RestoreCheckpointState()
        {
            BombProjectile[] bombs = new BombProjectile[activeBombs.Count];
            activeBombs.CopyTo(bombs);
            foreach (BombProjectile bomb in bombs)
            {
                if (bomb != null)
                {
                    bomb.Completed -= HandleBombCompleted;
                    bomb.Disarm();
                }
            }
            activeBombs.Clear();
            IsGenerating = savedGenerating;
            timeUntilNextBomb = savedTimeUntilNextBomb;
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = spawnPoint != null ? spawnPoint : transform;
            Vector3 direction = origin.forward.sqrMagnitude > 0.001f
                ? origin.forward.normalized
                : Vector3.forward;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(origin.position, 0.15f);
            Gizmos.DrawRay(origin.position, direction * 2f);
        }
    }
}
