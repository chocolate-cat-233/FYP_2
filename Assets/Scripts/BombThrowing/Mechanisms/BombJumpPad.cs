using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SuperFinalFuse.BombThrowing.Interactions;
using SuperFinalFuse.PlayerLife;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    public enum JumpPadDirection
    {
        Forward,
        Backward,
        Left,
        Right
    }

    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class BombJumpPad : MonoBehaviour,
        IBombDirectHitReceiver
    {
        [Header("Direction")]
        [SerializeField] private JumpPadDirection direction =
            JumpPadDirection.Forward;

        [Header("Activation")]
        [Min(0f)]
        [SerializeField] private float launchDelay = 0.25f;
        [Min(0f)]
        [SerializeField] private float topDetectionHeight = 0.25f;

        [Header("Bomb launch")]
        [Min(0f)]
        [SerializeField] private float bombLaunchDistance = 6f;
        [Min(0.01f)]
        [SerializeField] private float bombLaunchHeight = 3f;

        [Header("Player launch")]
        [Min(0f)]
        [SerializeField] private float playerLaunchDistance = 6f;
        [Min(0.01f)]
        [SerializeField] private float playerLaunchHeight = 3f;
        [Min(0f)]
        [SerializeField] private float playerDetectionHeight = 0.2f;

        [Header("Events")]
        [SerializeField] private UnityEvent onBombLaunched;
        [SerializeField] private UnityEvent onPlayerLaunched;

        [Header("Trajectory preview")]
        [SerializeField] private bool showBombTrajectory = true;
        [SerializeField] private bool showPlayerTrajectory = true;

        private readonly Dictionary<BombProjectile, float> pendingBombs =
            new Dictionary<BombProjectile, float>();
        private Collider padCollider;
        private PlayerHealth playerInside;
        private float playerLaunchTime;

        private void Awake()
        {
            padCollider = GetComponent<Collider>();
        }

        private void Update()
        {
            UpdatePendingBombs();
            DetectPlayer();
        }

        public BombHitResult ReceiveBombHit(BombDirectHit hit)
        {
            if (hit.Projectile == null)
            {
                return BombHitResult.Ignore;
            }
            if (!pendingBombs.ContainsKey(hit.Projectile) &&
                IsBombContactOnTop(hit))
            {
                pendingBombs.Add(hit.Projectile, Time.time + launchDelay);
            }
            return BombHitResult.ContinueFlight;
        }

        private bool IsBombContactOnTop(BombDirectHit hit)
        {
            if (padCollider == null || hit.Collider != padCollider ||
                hit.Normal.y < 0.45f)
            {
                return false;
            }

            Bounds padBounds = padCollider.bounds;
            return hit.Point.y >= padBounds.max.y - topDetectionHeight;
        }

        private void UpdatePendingBombs()
        {
            if (pendingBombs.Count == 0 || padCollider == null)
            {
                return;
            }

            List<BombProjectile> remove = new List<BombProjectile>();
            foreach (KeyValuePair<BombProjectile, float> pair in pendingBombs)
            {
                BombProjectile bomb = pair.Key;
                if (bomb == null || !IsAbovePad(bomb))
                {
                    remove.Add(bomb);
                    continue;
                }
                if (Time.time < pair.Value)
                {
                    continue;
                }

                Vector3 velocity = CalculateLaunchVelocity(
                    bombLaunchDistance,
                    bombLaunchHeight);
                if (bomb.RedirectVelocity(velocity))
                {
                    onBombLaunched?.Invoke();
                }
                remove.Add(bomb);
            }

            foreach (BombProjectile bomb in remove)
            {
                pendingBombs.Remove(bomb);
            }
        }

        private bool IsAbovePad(BombProjectile bomb)
        {
            Collider[] colliders = bomb.GetComponentsInChildren<Collider>();
            bool found = false;
            Bounds bombBounds = default;
            foreach (Collider bombCollider in colliders)
            {
                if (bombCollider == null || !bombCollider.enabled ||
                    bombCollider.isTrigger)
                {
                    continue;
                }
                if (!found)
                {
                    bombBounds = bombCollider.bounds;
                    found = true;
                }
                else
                {
                    bombBounds.Encapsulate(bombCollider.bounds);
                }
            }
            return found && IsOnTop(bombBounds);
        }

        private void DetectPlayer()
        {
            PlayerHealth player = playerInside != null
                ? playerInside
                : FindFirstObjectByType<PlayerHealth>();
            if (player == null || padCollider == null || player.IsDead)
            {
                return;
            }

            Collider playerCollider =
                player.GetComponent<CapsuleCollider>();
            if (playerCollider == null)
            {
                return;
            }

            Bounds playerBounds = playerCollider.bounds;
            bool isOnPad = IsPlayerOnTop(playerBounds);

            if (!isOnPad)
            {
                if (playerInside == player)
                {
                    playerInside = null;
                    playerLaunchTime = 0f;
                }
                return;
            }
            if (playerInside != player)
            {
                playerInside = player;
                playerLaunchTime = Time.time + launchDelay;
                return;
            }
            if (Time.time < playerLaunchTime)
            {
                return;
            }

            playerInside = null;
            playerLaunchTime = 0f;
            JumpPadPlayerFlight flight =
                player.GetComponent<JumpPadPlayerFlight>();
            if (flight == null)
            {
                flight = player.gameObject.AddComponent<JumpPadPlayerFlight>();
            }
            flight.Launch(
                CalculateLaunchVelocity(
                    playerLaunchDistance,
                    playerLaunchHeight),
                padCollider);
            onPlayerLaunched?.Invoke();
        }

        private bool IsPlayerOnTop(Bounds playerBounds)
        {
            Vector3 rayOrigin = playerBounds.center;
            float rayDistance = playerBounds.extents.y +
                                playerDetectionHeight;
            if (!padCollider.Raycast(
                    new Ray(rayOrigin, Vector3.down),
                    out RaycastHit hit,
                    rayDistance))
            {
                return false;
            }

            float feetToSurface = playerBounds.min.y - hit.point.y;
            return hit.normal.y >= 0.45f &&
                   feetToSurface >= -playerDetectionHeight &&
                   feetToSurface <= playerDetectionHeight;
        }

        private bool IsOnTop(Bounds objectBounds)
        {
            Bounds padBounds = padCollider.bounds;
            bool overlapsHorizontally =
                objectBounds.max.x > padBounds.min.x &&
                objectBounds.min.x < padBounds.max.x &&
                objectBounds.max.z > padBounds.min.z &&
                objectBounds.min.z < padBounds.max.z;
            bool nearTop =
                objectBounds.min.y <= padBounds.max.y + topDetectionHeight &&
                objectBounds.min.y >= padBounds.max.y - topDetectionHeight &&
                objectBounds.center.y > padBounds.center.y;
            return overlapsHorizontally && nearTop;
        }

        private Vector3 CalculateLaunchVelocity(
            float distance,
            float height,
            float gravityMultiplier = 1f)
        {
            float gravity = Mathf.Max(
                0.01f,
                Mathf.Abs(Physics.gravity.y) * gravityMultiplier);
            float verticalSpeed = Mathf.Sqrt(2f * gravity * height);
            float sameHeightFlightTime = 2f * verticalSpeed / gravity;
            float horizontalSpeed = sameHeightFlightTime > 0.001f
                ? distance / sameHeightFlightTime
                : 0f;
            return GetWorldDirection() * horizontalSpeed +
                   Vector3.up * verticalSpeed;
        }

        private Vector3 GetWorldDirection()
        {
            Vector3 result = direction switch
            {
                JumpPadDirection.Backward => -transform.forward,
                JumpPadDirection.Left => -transform.right,
                JumpPadDirection.Right => transform.right,
                _ => transform.forward
            };
            result.y = 0f;
            return result.sqrMagnitude > 0.001f
                ? result.normalized
                : Vector3.forward;
        }

        private void OnDrawGizmosSelected()
        {
            Collider previewCollider = padCollider != null
                ? padCollider
                : GetComponent<Collider>();
            Vector3 origin = previewCollider != null
                ? new Vector3(
                    previewCollider.bounds.center.x,
                    previewCollider.bounds.max.y,
                    previewCollider.bounds.center.z)
                : transform.position;

            if (showBombTrajectory)
            {
                float bombGravityMultiplier =
                    GetBombPreviewGravityMultiplier();
                DrawTrajectory(
                    origin,
                    CalculateLaunchVelocity(
                        bombLaunchDistance,
                        bombLaunchHeight),
                    Color.green,
                    bombGravityMultiplier);
            }
            if (showPlayerTrajectory)
            {
                DrawTrajectory(
                    origin,
                    CalculateLaunchVelocity(
                        playerLaunchDistance,
                        playerLaunchHeight),
                    Color.cyan,
                    1f);
            }
        }

        private static void DrawTrajectory(
            Vector3 origin,
            Vector3 launchVelocity,
            Color color,
            float gravityMultiplier)
        {
            Gizmos.color = color;
            Vector3 previous = origin;
            const int steps = 48;
            float gravity = Mathf.Max(
                0.01f,
                Mathf.Abs(Physics.gravity.y) * gravityMultiplier);
            float duration = 2f * launchVelocity.y / gravity;
            for (int i = 1; i <= steps; i++)
            {
                float time = duration * i / steps;
                Vector3 effectiveGravity =
                    Physics.gravity * gravityMultiplier;
                Vector3 point = origin + launchVelocity * time +
                    effectiveGravity * (0.5f * time * time);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        private float GetBombPreviewGravityMultiplier()
        {
            if (Application.isPlaying && pendingBombs.Count > 0)
            {
                foreach (BombProjectile bomb in pendingBombs.Keys)
                {
                    if (bomb != null)
                    {
                        return Mathf.Max(
                            0.01f,
                            bomb.EffectiveGravityMultiplier);
                    }
                }
            }

#if UNITY_EDITOR
            const string bombPrefabPath =
                "Assets/Prefab/Objects2/Bomb.prefab";
            GameObject bombPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(bombPrefabPath);
            if (bombPrefab != null)
            {
                GravityController gravityController =
                    bombPrefab.GetComponent<GravityController>();
                if (gravityController != null && gravityController.enabled)
                {
                    return Mathf.Max(
                        0.01f,
                        gravityController.GravityMultiplier);
                }

                Rigidbody body = bombPrefab.GetComponent<Rigidbody>();
                if (body != null && body.useGravity)
                {
                    return 1f;
                }
            }
#endif

            return 1f;
        }

    }
}
