using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.IronDoorPuzzle
{
    [DisallowMultipleComponent]
    public sealed class RigidbodyHierarchyCarrier : MonoBehaviour
    {
        private sealed class CarriedBodyState
        {
            public Rigidbody Body;
            public bool IsKinematic;
            public RigidbodyConstraints Constraints;
            public RigidbodyInterpolation Interpolation;
            public CollisionDetectionMode CollisionDetection;
            public Vector3 StepLocalPosition;
            public Quaternion StepLocalRotation;
            public readonly List<MonoBehaviour> PausedBehaviours =
                new List<MonoBehaviour>();
        }

        private readonly List<CarriedBodyState> carriedBodies =
            new List<CarriedBodyState>();
        private bool carrying;

        internal void BeginCarry()
        {
            if (carrying)
            {
                return;
            }

            carriedBodies.Clear();
            Rigidbody[] childBodies =
                GetComponentsInChildren<Rigidbody>();

            foreach (Rigidbody childBody in childBodies)
            {
                if (childBody == null || childBody.transform == transform)
                {
                    continue;
                }

                CarriedBodyState bodyState = new CarriedBodyState
                {
                    Body = childBody,
                    IsKinematic = childBody.isKinematic,
                    Constraints = childBody.constraints,
                    Interpolation = childBody.interpolation,
                    CollisionDetection = childBody.collisionDetectionMode
                };

                PauseBodyBehaviours(childBody, bodyState);

                if (!childBody.isKinematic)
                {
                    childBody.linearVelocity = Vector3.zero;
                    childBody.angularVelocity = Vector3.zero;
                }

                childBody.constraints = RigidbodyConstraints.None;
                childBody.isKinematic = true;
                childBody.interpolation = RigidbodyInterpolation.Interpolate;
                childBody.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousSpeculative;
                carriedBodies.Add(bodyState);
            }

            carrying = carriedBodies.Count > 0;
        }

        internal void PrepareCarryStep()
        {
            if (!carrying)
            {
                return;
            }

            Quaternion inverseCarrierRotation =
                Quaternion.Inverse(transform.rotation);
            foreach (CarriedBodyState bodyState in carriedBodies)
            {
                Rigidbody childBody = bodyState.Body;
                if (childBody == null)
                {
                    continue;
                }

                bodyState.StepLocalPosition =
                    transform.InverseTransformPoint(childBody.position);
                bodyState.StepLocalRotation =
                    inverseCarrierRotation * childBody.rotation;
            }
        }

        internal void ApplyCarryStep()
        {
            if (!carrying)
            {
                return;
            }

            foreach (CarriedBodyState bodyState in carriedBodies)
            {
                Rigidbody childBody = bodyState.Body;
                if (childBody == null)
                {
                    continue;
                }

                childBody.MovePosition(
                    transform.TransformPoint(bodyState.StepLocalPosition));
                childBody.MoveRotation(
                    transform.rotation * bodyState.StepLocalRotation);
            }
        }

        internal void EndCarry()
        {
            if (!carrying)
            {
                return;
            }

            foreach (CarriedBodyState bodyState in carriedBodies)
            {
                RestoreBody(bodyState);
            }

            carriedBodies.Clear();
            carrying = false;
        }

        private static void PauseBodyBehaviours(
            Rigidbody childBody,
            CarriedBodyState bodyState)
        {
            MonoBehaviour[] behaviours =
                childBody.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null && behaviour.enabled)
                {
                    bodyState.PausedBehaviours.Add(behaviour);
                    behaviour.enabled = false;
                }
            }
        }

        private static void RestoreBody(CarriedBodyState bodyState)
        {
            Rigidbody childBody = bodyState.Body;
            if (childBody != null)
            {
                childBody.constraints = bodyState.Constraints;
                childBody.isKinematic = bodyState.IsKinematic;
                childBody.interpolation = bodyState.Interpolation;
                childBody.collisionDetectionMode =
                    bodyState.CollisionDetection;

                if (!childBody.isKinematic)
                {
                    childBody.linearVelocity = Vector3.zero;
                    childBody.angularVelocity = Vector3.zero;
                }
            }

            foreach (MonoBehaviour behaviour in bodyState.PausedBehaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }
        }

        private void OnDisable()
        {
            EndCarry();
        }
    }
}
