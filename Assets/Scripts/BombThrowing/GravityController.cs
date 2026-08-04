using UnityEngine;

namespace SuperFinalFuse
{


    [RequireComponent(typeof(Rigidbody))]
    public sealed class GravityController : MonoBehaviour
    {
        [Header("Gravity")]
        [SerializeField] private float gravityMultiplier = 1f;

        private Rigidbody Body;

        public float GravityMultiplier
        {
            get => gravityMultiplier;
            set => gravityMultiplier = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (Body != null)
            {
                Body.useGravity = false;
            }
        }

        private void FixedUpdate()
        {
            if (Body == null || Body.isKinematic)
            {
                return;
            }

            // Safety net: BombProjectile restores its captured Rigidbody values
            // (including useGravity) on Launch, so re-disable built-in gravity
            // here. Without this the bomb would be pulled by both gravity
            // sources at once. Script execution order does not matter.
            Body.useGravity = false;

            if (gravityMultiplier > 0f)
            {
                Body.linearVelocity +=
                    Physics.gravity * gravityMultiplier * Time.fixedDeltaTime;
            }
        }
    }
}
