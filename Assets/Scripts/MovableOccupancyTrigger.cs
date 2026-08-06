using System;
using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.IronDoorPuzzle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class MovableOccupancyTrigger : MonoBehaviour
    {
        [SerializeField] private LayerMask movableLayers = 1 << 7;

        private readonly Dictionary<Transform, HashSet<Collider>> occupants =
            new Dictionary<Transform, HashSet<Collider>>();
        private readonly List<Transform> staleOccupants =
            new List<Transform>();

        public bool IsOccupied => occupants.Count > 0;

        public event Action<bool> OccupancyChanged;

        private void Reset()
        {
            int movableLayer = LayerMask.NameToLayer("Movable");
            if (movableLayer >= 0)
            {
                movableLayers = 1 << movableLayer;
            }

            Collider trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        private void LateUpdate()
        {
            RemoveStaleOccupants();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || other.isTrigger)
            {
                return;
            }

            Transform occupant = GetOccupantRoot(other);
            if (occupant == null || !IsMovable(occupant.gameObject))
            {
                return;
            }

            bool wasOccupied = IsOccupied;
            if (!occupants.TryGetValue(occupant, out HashSet<Collider> colliders))
            {
                colliders = new HashSet<Collider>();
                occupants.Add(occupant, colliders);
            }

            colliders.Add(other);
            NotifyIfOccupancyChanged(wasOccupied);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null || other.isTrigger)
            {
                return;
            }

            Transform occupant = GetOccupantRoot(other);
            if (occupant == null ||
                !occupants.TryGetValue(occupant, out HashSet<Collider> colliders))
            {
                return;
            }

            bool wasOccupied = IsOccupied;
            colliders.Remove(other);
            if (colliders.Count == 0)
            {
                occupants.Remove(occupant);
            }

            NotifyIfOccupancyChanged(wasOccupied);
        }

        private void OnDisable()
        {
            bool wasOccupied = IsOccupied;
            occupants.Clear();
            NotifyIfOccupancyChanged(wasOccupied);
        }

        private Transform GetOccupantRoot(Collider other)
        {
            Rigidbody attachedBody = other.attachedRigidbody;
            return attachedBody != null
                ? attachedBody.transform
                : other.transform.root;
        }

        private bool IsMovable(GameObject candidate)
        {
            return candidate != null &&
                   (movableLayers.value & (1 << candidate.layer)) != 0;
        }

        private void RemoveStaleOccupants()
        {
            if (occupants.Count == 0)
            {
                return;
            }

            bool wasOccupied = IsOccupied;
            staleOccupants.Clear();

            foreach (KeyValuePair<Transform, HashSet<Collider>> entry in occupants)
            {
                Transform occupant = entry.Key;
                if (occupant == null ||
                    !occupant.gameObject.activeInHierarchy ||
                    !IsMovable(occupant.gameObject))
                {
                    staleOccupants.Add(occupant);
                    continue;
                }

                entry.Value.RemoveWhere(
                    collider => collider == null ||
                                !collider.enabled ||
                                !collider.gameObject.activeInHierarchy);
                if (entry.Value.Count == 0)
                {
                    staleOccupants.Add(occupant);
                }
            }

            foreach (Transform occupant in staleOccupants)
            {
                occupants.Remove(occupant);
            }

            NotifyIfOccupancyChanged(wasOccupied);
        }

        private void NotifyIfOccupancyChanged(bool previousValue)
        {
            if (previousValue != IsOccupied)
            {
                OccupancyChanged?.Invoke(IsOccupied);
            }
        }
    }
}
