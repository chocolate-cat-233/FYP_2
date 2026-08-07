using UnityEngine;
using UnityEngine.Serialization;
using System.Text;
using SuperFinalFuse.BombThrowing.Buffs;
using SuperFinalFuse.BombThrowing.Interactions;

namespace SuperFinalFuse.BombThrowing
{
    public enum BombThrowTrajectory
    {
        Horizontal,
        Angled
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BombTrajectoryPreview))]
    public sealed class BombThrower : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BombProjectile bombPrefab;
        [SerializeField] private Transform throwOrigin;
        [Tooltip("Optional. Only used when Use Camera Direction is enabled.")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private InputManager inputManager;

        [Header("Throw")]
        [Min(0.1f)]
        [Tooltip("Launch speed in world units per second. In Angled mode this " +
                 "is the total initial speed before angle decomposition.")]
        [FormerlySerializedAs("maximumThrowDistance")]
        [SerializeField] private float throwSpeed = 8f;
        [SerializeField] private BombThrowTrajectory trajectory =
            BombThrowTrajectory.Horizontal;
        [Range(0f, 89f)]
        [Tooltip("Launch angle above the horizontal plane. Only used by " +
                 "the Angled trajectory mode.")]
        [SerializeField] private float angledThrowDegrees = 30f;
        [Min(0.1f)]
        [Tooltip("Only used as the spawn height when Throw Origin is empty.")]
        [FormerlySerializedAs("assumedDropHeight")]
        [SerializeField] private float fallbackThrowHeight = 1.5f;
        [Tooltip("Off: throw toward the player's current facing direction. " +
                 "On: throw toward the camera's horizontal forward direction.")]
        [SerializeField] private bool useCameraDirection;
        [Min(1f)]
        [Tooltip("Failsafe: the throw lock is released after this many seconds " +
                 "even if a projectile completion callback is lost.")]
        [SerializeField] private float maximumThrowLockDuration = 10f;

        private BombProjectile activeBomb;
        private BombTrajectoryPreview trajectoryPreview;

        private int activeBombInstanceId;
        private float activeBombLaunchTime;
        private bool inputBlocked;
        private string lastDiagnosticEvent = "Not initialized";
        private int throwAttemptCount;
        private int successfulThrowCount;

        public bool CanThrow => !inputBlocked && activeBombInstanceId == 0;
        public bool IsInputBlocked => inputBlocked;
        public bool IsAimInputHeld =>
            inputManager != null && inputManager.BombAimHeld;
        public float ThrowPreparationProgress => CanThrow
            ? 1f
            : Mathf.Clamp01(CurrentLockAge /
                Mathf.Max(0.01f, maximumThrowLockDuration));
        public int ActiveBombInstanceId => activeBombInstanceId;
        public float CurrentLockAge => activeBombInstanceId == 0
            ? 0f
            : Time.unscaledTime - activeBombLaunchTime;
        public string LastDiagnosticEvent => lastDiagnosticEvent;

        private void Update()
        {
            RefreshThrowLock();

            if (inputBlocked)
            {
                CancelTrajectoryAim();
                return;
            }

            UpdateTrajectoryInput();
        }

        private void UpdateTrajectoryInput()
        {
            if (inputManager == null)
            {
                CancelTrajectoryAim();
                return;
            }

            bool confirmPressed =
                inputManager.BombThrowPressedThisFrame;

            // Left click always remains a valid immediate throw. Holding the
            // aim action only adds the optional trajectory preview.
            if (confirmPressed)
            {
                TryThrow();
                trajectoryPreview?.Hide();
            }

            if (!inputManager.BombAimHeld)
            {
                CancelTrajectoryAim();
                return;
            }

            trajectoryPreview?.Show(this);
        }

        private void CancelTrajectoryAim()
        {
            trajectoryPreview?.Hide();
        }

        [ContextMenu("Throw Bomb")]
        public bool TryThrow()
        {
            throwAttemptCount++;

            if (inputBlocked)
            {
                lastDiagnosticEvent =
                    $"Throw rejected: input is blocked ({Time.unscaledTime:0.00}s)";
                return false;
            }

            if (activeBombInstanceId != 0)
            {
                lastDiagnosticEvent =
                    $"Throw rejected: bomb lock #{activeBombInstanceId}, " +
                    $"age={CurrentLockAge:0.00}s";
                return false;
            }

            if (bombPrefab == null)
            {
                lastDiagnosticEvent = "Throw rejected: Bomb Prefab is missing";
                return false;
            }

            Vector3 origin = throwOrigin != null
                ? throwOrigin.position
                : transform.position + Vector3.up * fallbackThrowHeight;
            Vector3 direction = GetHorizontalThrowDirection();

            activeBomb = Instantiate(
                bombPrefab,
                origin,
                Quaternion.LookRotation(direction, Vector3.up));
            activeBombInstanceId = activeBomb.GetInstanceID();
            activeBombLaunchTime = Time.unscaledTime;
            activeBomb.Completed += HandleBombCompleted;
            activeBomb.Launch(
                CalculateLaunchVelocity(direction),
                gameObject,
                BombOwnerType.Player);
            successfulThrowCount++;
            lastDiagnosticEvent =
                $"Throw succeeded: bomb #{activeBombInstanceId}";
            return true;
        }

        private Vector3 GetHorizontalThrowDirection()
        {
            Vector3 direction = useCameraDirection && cameraTransform != null
                ? cameraTransform.forward
                : transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = transform.forward;
                direction.y = 0f;
            }

            return direction.normalized;
        }

        private Vector3 CalculateLaunchVelocity(Vector3 horizontalDirection)
        {
            if (trajectory == BombThrowTrajectory.Horizontal)
            {
                return horizontalDirection * throwSpeed;
            }

            float angleRadians = angledThrowDegrees * Mathf.Deg2Rad;
            float horizontalSpeed = throwSpeed * Mathf.Cos(angleRadians);
            float upwardSpeed = throwSpeed * Mathf.Sin(angleRadians);
            return horizontalDirection * horizontalSpeed +
                   Vector3.up * upwardSpeed;
        }

        public bool TryGetTrajectoryData(
            out Vector3 origin,
            out Vector3 velocity,
            out BombProjectile prefab)
        {
            prefab = bombPrefab;
            origin = throwOrigin != null
                ? throwOrigin.position
                : transform.position + Vector3.up * fallbackThrowHeight;
            velocity = CalculateLaunchVelocity(GetHorizontalThrowDirection());
            return prefab != null && !inputBlocked;
        }

        private void HandleBombCompleted(BombProjectile bomb)
        {
            int completedInstanceId = bomb != null
                ? bomb.GetInstanceID()
                : 0;

            if (bomb != null)
            {
                bomb.Completed -= HandleBombCompleted;
            }

            if (completedInstanceId == activeBombInstanceId)
            {
                lastDiagnosticEvent =
                    $"Completion received for bomb #{completedInstanceId}";
                ClearThrowLock();
            }
            else
            {
                lastDiagnosticEvent =
                    $"Ignored completion #{completedInstanceId}; " +
                    $"current lock is #{activeBombInstanceId}";
            }
        }

        private void RefreshThrowLock()
        {
            if (activeBombInstanceId == 0)
            {
                return;
            }

            // Unity's destroyed-object null handling is reliable here and does
            // not depend on the projectile event reaching this component.
            if (activeBomb == null ||
                activeBomb.HasExploded ||
                !activeBomb.gameObject.activeInHierarchy)
            {
                lastDiagnosticEvent =
                    $"Polling cleared bomb lock #{activeBombInstanceId}";
                ClearThrowLock();
                return;
            }

            if (Time.unscaledTime - activeBombLaunchTime < maximumThrowLockDuration)
            {
                return;
            }

            Debug.LogWarning(
                $"{nameof(BombThrower)} on '{name}' released a stale bomb " +
                $"lock after {maximumThrowLockDuration:0.##} seconds.",
                this);
            lastDiagnosticEvent =
                $"Failsafe cleared stale lock #{activeBombInstanceId}";
            activeBomb.Explode();
            ClearThrowLock();
        }

        private void ClearThrowLock()
        {
            if (activeBomb != null)
            {
                activeBomb.Completed -= HandleBombCompleted;
            }

            activeBomb = null;
            activeBombInstanceId = 0;
            activeBombLaunchTime = 0f;
        }

        /// <summary>
        /// Blocks player input without disabling this component, so projectile
        /// cleanup and the failsafe timer continue to run.
        /// </summary>
        public void SetInputBlocked(bool value)
        {
            inputBlocked = value;
            lastDiagnosticEvent = value
                ? "Input blocked by respawn/control flow"
                : "Input unblocked by respawn/control flow";
        }

        public void ForceClearThrowLockForDiagnostics()
        {
            int clearedId = activeBombInstanceId;
            ClearThrowLock();
            inputBlocked = false;
            lastDiagnosticEvent =
                $"Diagnostic force unlock cleared bomb #{clearedId}";
        }

        public string GetDiagnosticReport()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("=== BombThrower Diagnostic ===");
            report.AppendLine($"Player: {name}");
            report.AppendLine($"Component enabled: {enabled}");
            report.AppendLine($"GameObject activeInHierarchy: {gameObject.activeInHierarchy}");
            report.AppendLine($"Application focused: {Application.isFocused}");
            report.AppendLine($"Input blocked: {inputBlocked}");
            report.AppendLine($"CanThrow: {CanThrow}");
            report.AppendLine($"Bomb prefab assigned: {bombPrefab != null}");
            report.AppendLine($"Active bomb reference alive: {activeBomb != null}");
            report.AppendLine($"Active bomb instance ID: {activeBombInstanceId}");
            report.AppendLine($"Lock age (unscaled): {CurrentLockAge:0.00}s");
            report.AppendLine($"Attempts / successes: {throwAttemptCount} / {successfulThrowCount}");
            report.AppendLine($"InputManager assigned: {inputManager != null}");
            report.AppendLine($"BombAim held: {IsAimInputHeld}");
            report.AppendLine($"Time scale: {Time.timeScale:0.###}");
            report.AppendLine($"Last event: {lastDiagnosticEvent}");
            return report.ToString();
        }

        private void OnEnable()
        {
            if (inputManager == null)
            {
                inputManager = GetComponent<InputManager>();
            }

            if (GetComponent<BombBuffController>() == null)
            {
                gameObject.AddComponent<BombBuffController>();
            }

            trajectoryPreview = GetComponent<BombTrajectoryPreview>();
            if (trajectoryPreview == null && Application.isPlaying)
            {
                trajectoryPreview =
                    gameObject.AddComponent<BombTrajectoryPreview>();
            }

            lastDiagnosticEvent = "BombThrower enabled";
        }

        private void OnDisable()
        {
            CancelTrajectoryAim();
            lastDiagnosticEvent = "BombThrower disabled";
            if (!Application.isPlaying)
            {
                return;
            }

            Debug.LogWarning(
                $"{nameof(BombThrower)} on '{name}' was disabled. " +
                $"Lock=#{activeBombInstanceId}, blocked={inputBlocked}.",
                this);
        }

        private void OnDestroy()
        {
            if (activeBomb != null)
            {
                activeBomb.Completed -= HandleBombCompleted;
            }
        }
    }
}
