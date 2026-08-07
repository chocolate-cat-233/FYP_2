using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.IronDoorPuzzle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class IronDoorPressureButton : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        [Header("Puzzle References")]
        [SerializeField] private MovableOccupancyTrigger firstDoorTrigger;
        [SerializeField] private MovableOccupancyTrigger secondDoorTrigger;
        [SerializeField] private Transform door1;
        [SerializeField] private Transform door2;
        [SerializeField] private Transform movingFloor;
        [SerializeField] private GameObject officeAirWall;
        [SerializeField] private CameraRange controlOfficeStairCameraRange;
        [SerializeField] private CameraRange firstTryCameraRange;

        [Header("Visual")]
        [SerializeField] private Renderer indicatorRenderer;
        [SerializeField] private Color unavailableColor =
            new Color(0.95f, 0.07f, 0f, 1f);
        [SerializeField] private Color readyColor =
            new Color(0.08f, 0.8f, 0.2f, 1f);

        [Header("Activation")]
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private float doorLocalZDelta = 90f;
        [SerializeField] private float floorTargetLocalZ = 7f;
        [SerializeField] private float controlOfficeStairRotationY = 45f;
        [SerializeField] private float firstTryRotationY;

        [Header("Animation")]
        [Min(0.01f)]
        [SerializeField] private float doorRotationDuration = 1f;
        [Min(0.01f)]
        [SerializeField] private float floorMovementDuration = 2f;
        [SerializeField] private AnimationCurve animationCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private readonly Dictionary<Transform, HashSet<Collider>> playersOnButton =
            new Dictionary<Transform, HashSet<Collider>>();
        private readonly List<Transform> stalePlayers =
            new List<Transform>();

        private MaterialPropertyBlock indicatorProperties;
        private Quaternion door1InitialLocalRotation;
        private Quaternion door2InitialLocalRotation;
        private Quaternion door1AnimationStartRotation;
        private Quaternion door2AnimationStartRotation;
        private Quaternion door1AnimationTargetRotation;
        private Quaternion door2AnimationTargetRotation;
        private Vector3 floorAnimationStartPosition;
        private Vector3 floorAnimationTargetPosition;
        private RigidbodyHierarchyCarrier rigidbodyCarrier;
        private float animationElapsed;
        private bool animationPlaying;
        private bool releaseCarriedBodiesOnNextFixedUpdate;
        private bool activated;

        public bool IsActivated => activated;
        public bool IsReady => activated ||
                               (firstDoorTrigger != null &&
                                secondDoorTrigger != null &&
                                firstDoorTrigger.IsOccupied &&
                                secondDoorTrigger.IsOccupied);

        private void Awake()
        {
            indicatorProperties = new MaterialPropertyBlock();

            if (door1 != null)
            {
                door1InitialLocalRotation = door1.localRotation;
            }

            if (door2 != null)
            {
                door2InitialLocalRotation = door2.localRotation;
            }

            if (movingFloor != null)
            {
                rigidbodyCarrier =
                    movingFloor.GetComponent<RigidbodyHierarchyCarrier>();
            }
        }

        private void OnEnable()
        {
            SubscribeToTriggers();
            RefreshState();
        }

        private void Start()
        {
            WarnAboutMissingReferences();
            RefreshState();
        }

        private void FixedUpdate()
        {
            if (releaseCarriedBodiesOnNextFixedUpdate)
            {
                releaseCarriedBodiesOnNextFixedUpdate = false;
                rigidbodyCarrier?.EndCarry();
            }

            if (animationPlaying)
            {
                UpdateActivationAnimation(Time.fixedDeltaTime);
            }
        }

        private void LateUpdate()
        {
            RemoveStalePlayers();
        }

        private void OnDisable()
        {
            UnsubscribeFromTriggers();
            playersOnButton.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (activated || other == null || other.isTrigger)
            {
                return;
            }

            Transform player = GetPlayerRoot(other);
            if (player == null)
            {
                return;
            }

            if (!playersOnButton.TryGetValue(
                    player,
                    out HashSet<Collider> colliders))
            {
                colliders = new HashSet<Collider>();
                playersOnButton.Add(player, colliders);
            }

            colliders.Add(other);
            RefreshState();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || other.isTrigger)
            {
                return;
            }

            Transform player = GetPlayerRoot(other);
            if (player == null ||
                !playersOnButton.TryGetValue(
                    player,
                    out HashSet<Collider> colliders))
            {
                return;
            }

            colliders.Remove(other);
            if (colliders.Count == 0)
            {
                playersOnButton.Remove(player);
            }
        }

        private void SubscribeToTriggers()
        {
            if (firstDoorTrigger != null)
            {
                firstDoorTrigger.OccupancyChanged += HandleOccupancyChanged;
            }

            if (secondDoorTrigger != null)
            {
                secondDoorTrigger.OccupancyChanged += HandleOccupancyChanged;
            }
        }

        private void UnsubscribeFromTriggers()
        {
            if (firstDoorTrigger != null)
            {
                firstDoorTrigger.OccupancyChanged -= HandleOccupancyChanged;
            }

            if (secondDoorTrigger != null)
            {
                secondDoorTrigger.OccupancyChanged -= HandleOccupancyChanged;
            }
        }

        private void HandleOccupancyChanged(bool isOccupied)
        {
            RefreshState();
        }

        private void RefreshState()
        {
            SetIndicatorColor(IsReady ? readyColor : unavailableColor);

            if (!activated && IsReady && playersOnButton.Count > 0)
            {
                Activate();
            }
        }

        private void Activate()
        {
            if (activated || door1 == null || door2 == null || movingFloor == null)
            {
                return;
            }

            activated = true;
            if (officeAirWall != null)
            {
                officeAirWall.SetActive(true);
            }

            BeginActivationAnimation();

            if (controlOfficeStairCameraRange != null)
            {
                controlOfficeStairCameraRange.SetTargetRotationY(
                    controlOfficeStairRotationY);
            }

            if (firstTryCameraRange != null)
            {
                firstTryCameraRange.SetTargetRotationY(firstTryRotationY);
            }

            playersOnButton.Clear();
            SetIndicatorColor(readyColor);
        }

        private void BeginActivationAnimation()
        {
            Quaternion localZRotation =
                Quaternion.Euler(0f, 0f, doorLocalZDelta);

            door1AnimationStartRotation = door1.localRotation;
            door2AnimationStartRotation = door2.localRotation;
            door1AnimationTargetRotation =
                door1InitialLocalRotation * localZRotation;
            door2AnimationTargetRotation =
                door2InitialLocalRotation * localZRotation;

            floorAnimationStartPosition = movingFloor.localPosition;
            floorAnimationTargetPosition = floorAnimationStartPosition;
            floorAnimationTargetPosition.z = floorTargetLocalZ;

            animationElapsed = 0f;
            releaseCarriedBodiesOnNextFixedUpdate = false;
            rigidbodyCarrier?.BeginCarry();
            animationPlaying = true;
            ApplyAnimationPose(0f, 0f);
        }

        private void UpdateActivationAnimation(float deltaTime)
        {
            if (door1 == null || door2 == null || movingFloor == null)
            {
                animationPlaying = false;
                rigidbodyCarrier?.EndCarry();
                return;
            }

            animationElapsed += Mathf.Max(0f, deltaTime);
            float safeDoorDuration = GetSafeDuration(doorRotationDuration);
            float safeFloorDuration = GetSafeDuration(floorMovementDuration);
            float doorProgress = Mathf.Clamp01(
                animationElapsed / safeDoorDuration);
            float floorProgress = Mathf.Clamp01(
                animationElapsed / safeFloorDuration);

            ApplyAnimationPose(doorProgress, floorProgress);

            if (doorProgress >= 1f && floorProgress >= 1f)
            {
                animationPlaying = false;
                releaseCarriedBodiesOnNextFixedUpdate = true;
            }
        }

        private void ApplyAnimationPose(
            float doorProgress,
            float floorProgress)
        {
            float curvedDoorProgress = doorProgress >= 1f
                ? 1f
                : EvaluateAnimationCurve(doorProgress);
            float curvedFloorProgress = floorProgress >= 1f
                ? 1f
                : EvaluateAnimationCurve(floorProgress);

            rigidbodyCarrier?.PrepareCarryStep();

            door1.localRotation = Quaternion.SlerpUnclamped(
                door1AnimationStartRotation,
                door1AnimationTargetRotation,
                curvedDoorProgress);
            door2.localRotation = Quaternion.SlerpUnclamped(
                door2AnimationStartRotation,
                door2AnimationTargetRotation,
                curvedDoorProgress);
            movingFloor.localPosition = Vector3.LerpUnclamped(
                floorAnimationStartPosition,
                floorAnimationTargetPosition,
                curvedFloorProgress);

            rigidbodyCarrier?.ApplyCarryStep();
        }

        private float EvaluateAnimationCurve(float progress)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            return animationCurve != null
                ? Mathf.Clamp01(animationCurve.Evaluate(clampedProgress))
                : clampedProgress;
        }

        private Transform GetPlayerRoot(Collider other)
        {
            Transform root = other.transform.root;
            return root != null && root.CompareTag(playerTag) ? root : null;
        }

        private void SetIndicatorColor(Color color)
        {
            if (indicatorRenderer == null)
            {
                return;
            }

            indicatorRenderer.GetPropertyBlock(indicatorProperties);
            indicatorProperties.SetColor(BaseColorId, color);
            indicatorProperties.SetColor(ColorId, color);
            indicatorRenderer.SetPropertyBlock(indicatorProperties);
        }

        private void RemoveStalePlayers()
        {
            if (activated || playersOnButton.Count == 0)
            {
                return;
            }

            stalePlayers.Clear();
            foreach (KeyValuePair<Transform, HashSet<Collider>> entry in
                     playersOnButton)
            {
                Transform player = entry.Key;
                if (player == null ||
                    !player.gameObject.activeInHierarchy ||
                    !player.CompareTag(playerTag))
                {
                    stalePlayers.Add(player);
                    continue;
                }

                entry.Value.RemoveWhere(
                    collider => collider == null ||
                                !collider.enabled ||
                                !collider.gameObject.activeInHierarchy);
                if (entry.Value.Count == 0)
                {
                    stalePlayers.Add(player);
                }
            }

            foreach (Transform player in stalePlayers)
            {
                playersOnButton.Remove(player);
            }
        }

        private void WarnAboutMissingReferences()
        {
            if (firstDoorTrigger == null || secondDoorTrigger == null ||
                door1 == null || door2 == null || movingFloor == null ||
                officeAirWall == null ||
                controlOfficeStairCameraRange == null ||
                firstTryCameraRange == null || indicatorRenderer == null ||
                rigidbodyCarrier == null ||
                !IsValidDuration(doorRotationDuration) ||
                !IsValidDuration(floorMovementDuration) ||
                animationCurve == null)
            {
                Debug.LogWarning(
                    $"{nameof(IronDoorPressureButton)} on '{name}' has " +
                    "missing puzzle references or invalid animation settings.",
                    this);
            }
        }

        private static bool IsValidDuration(float duration)
        {
            return duration > 0f &&
                   !float.IsNaN(duration) &&
                   !float.IsInfinity(duration);
        }

        private static float GetSafeDuration(float duration)
        {
            return IsValidDuration(duration) ? duration : 0.0001f;
        }

        private void OnDestroy()
        {
            rigidbodyCarrier?.EndCarry();
        }
    }
}
