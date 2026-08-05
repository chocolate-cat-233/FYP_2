using UnityEngine;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class PhysicsPropertyPlatform : MonoBehaviour,
        ICheckpointStateParticipant
    {
        [Tooltip("Prevents the freely falling platform from tipping over while " +
                 "still allowing it to fall under gravity.")]
        [SerializeField] private bool freezeRotation = true;
        [Tooltip("When enabled, every respawn restores the platform to its " +
                 "Play Mode starting pose. Disable it only when checkpoints " +
                 "should save a later platform position.")]
        [SerializeField] private bool restoreToGameStart = true;

        private Rigidbody platformBody;
        private Vector3 savedPosition;
        private Quaternion savedRotation;
        private bool savedUseGravity;
        private bool savedIsKinematic;
        private RigidbodyConstraints savedConstraints;
        private RigidbodyInterpolation savedInterpolation;
        private bool hasSavedState;

        private void Reset()
        {
            ConfigureBody();
        }

        private void Awake()
        {
            ConfigureBody();
            CaptureCurrentState();
        }

        private void ConfigureBody()
        {
            if (platformBody == null)
            {
                platformBody = GetComponent<Rigidbody>();
            }

            if (platformBody == null)
            {
                return;
            }

            platformBody.isKinematic = false;
            platformBody.useGravity = true;
            platformBody.interpolation = RigidbodyInterpolation.Interpolate;
            platformBody.collisionDetectionMode =
                CollisionDetectionMode.Continuous;

            if (freezeRotation)
            {
                platformBody.constraints |=
                    RigidbodyConstraints.FreezeRotation;
            }
            else
            {
                platformBody.constraints &=
                    ~RigidbodyConstraints.FreezeRotation;
            }
        }

        public void CaptureCheckpointState()
        {
            if (restoreToGameStart && hasSavedState)
            {
                return;
            }

            CaptureCurrentState();
        }

        private void CaptureCurrentState()
        {
            if (platformBody == null)
            {
                platformBody = GetComponent<Rigidbody>();
            }
            if (platformBody == null)
            {
                return;
            }

            savedPosition = platformBody.position;
            savedRotation = platformBody.rotation;
            savedUseGravity = platformBody.useGravity;
            savedIsKinematic = platformBody.isKinematic;
            savedConstraints = platformBody.constraints;
            savedInterpolation = platformBody.interpolation;
            hasSavedState = true;
        }

        public void RestoreCheckpointState()
        {
            if (!hasSavedState || platformBody == null)
            {
                return;
            }

            // Freeze while teleporting so the old falling velocity cannot be
            // applied at the restored support position.
            platformBody.interpolation = RigidbodyInterpolation.None;
            platformBody.isKinematic = true;
            transform.SetPositionAndRotation(savedPosition, savedRotation);
            platformBody.position = savedPosition;
            platformBody.rotation = savedRotation;
            platformBody.constraints = savedConstraints;
            Physics.SyncTransforms();

            platformBody.isKinematic = savedIsKinematic;
            platformBody.useGravity = savedUseGravity;
            platformBody.interpolation = savedInterpolation;
            if (!platformBody.isKinematic)
            {
                // Do not put the restored body to sleep. Removing its support
                // collider later does not reliably wake a sleeping Rigidbody,
                // which makes gravity appear to be disabled. Start clean and
                // let Unity settle it onto the restored support again.
                platformBody.linearVelocity = Vector3.zero;
                platformBody.angularVelocity = Vector3.zero;
                platformBody.WakeUp();
            }
        }
    }
}
