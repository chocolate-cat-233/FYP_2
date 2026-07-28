using UnityEngine;
using UnityEngine.Events;

namespace SuperFinalFuse.Explosion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ShockwaveKnockback : MonoBehaviour, IShockwaveReactive
    {
        [Min(0f)]
        [SerializeField] private float impulseMultiplier = 1f;
        [SerializeField] private bool clearVelocityBeforeHit;
        [SerializeField] private UnityEvent onKnockedBack;

        private Rigidbody targetBody;

        private void Awake()
        {
            targetBody = GetComponent<Rigidbody>();
        }

        public void OnShockwaveHit(ShockwaveHit hit)
        {
            if (targetBody == null || targetBody.isKinematic)
            {
                return;
            }

            if (clearVelocityBeforeHit)
            {
                targetBody.linearVelocity = Vector3.zero;
                targetBody.angularVelocity = Vector3.zero;
            }

            Vector3 direction = hit.Direction;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = transform.position - hit.Origin;
                direction.y = 0f;
            }

            targetBody.AddForce(
                direction.normalized * hit.Strength * impulseMultiplier,
                ForceMode.Impulse);
            onKnockedBack?.Invoke();
        }
    }
}
