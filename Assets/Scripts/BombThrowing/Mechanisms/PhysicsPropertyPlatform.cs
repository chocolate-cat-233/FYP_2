using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class PhysicsPropertyPlatform : MonoBehaviour
    {
        [Tooltip("Prevents the freely falling platform from tipping over while " +
                 "still allowing it to fall under gravity.")]
        [SerializeField] private bool freezeRotation = true;

        private Rigidbody platformBody;

        private void Reset()
        {
            ConfigureBody();
        }

        private void Awake()
        {
            ConfigureBody();
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
    }
}
