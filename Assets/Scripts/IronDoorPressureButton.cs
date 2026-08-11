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

        private readonly Dictionary<Transform, HashSet<Collider>> playersOnButton =
            new Dictionary<Transform, HashSet<Collider>>();
        private readonly List<Transform> stalePlayers =
            new List<Transform>();

        private MaterialPropertyBlock indicatorProperties;
        private Quaternion door1InitialLocalRotation;
        private Quaternion door2InitialLocalRotation;
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
            Quaternion localZRotation =
                Quaternion.Euler(0f, 0f, doorLocalZDelta);
            door1.localRotation = door1InitialLocalRotation * localZRotation;
            door2.localRotation = door2InitialLocalRotation * localZRotation;

            Vector3 floorPosition = movingFloor.localPosition;
            floorPosition.z = floorTargetLocalZ;
            movingFloor.localPosition = floorPosition;

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
                controlOfficeStairCameraRange == null ||
                firstTryCameraRange == null || indicatorRenderer == null)
            {
                Debug.LogWarning(
                    $"{nameof(IronDoorPressureButton)} on '{name}' has " +
                    "missing puzzle references.",
                    this);
            }
        }
    }
}
