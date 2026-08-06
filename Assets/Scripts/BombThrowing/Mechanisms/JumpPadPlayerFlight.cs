using UnityEngine;
using SuperFinalFuse.PlayerLife;

namespace SuperFinalFuse.BombThrowing.Mechanisms
{
    [DisallowMultipleComponent]
    public sealed class JumpPadPlayerFlight : MonoBehaviour
    {
        private const float Skin = 0.02f;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[24];
        private pl_Move playerMovement;
        private CapsuleCollider playerCollider;
        private PlayerHealth health;
        private Vector3 velocity;
        private Collider launchPadCollider;

        public bool IsFlying { get; private set; }

        private void Awake()
        {
            playerMovement = GetComponent<pl_Move>();
            playerCollider = GetComponent<CapsuleCollider>();
            health = GetComponent<PlayerHealth>();
        }

        public void Launch(
            Vector3 launchVelocity,
            Collider sourcePadCollider = null)
        {
            velocity = launchVelocity;
            launchPadCollider = sourcePadCollider;
            IsFlying = true;
            if (playerMovement != null)
            {
                playerMovement.enabled = false;
            }

        }

        private void FixedUpdate()
        {
            if (!IsFlying)
            {
                return;
            }
            if (health != null && health.IsDead)
            {
                StopFlight(false);
                return;
            }

            velocity += Physics.gravity * Time.fixedDeltaTime;
            Vector3 displacement = velocity * Time.fixedDeltaTime;
            if (displacement.sqrMagnitude <= 0.000001f ||
                playerCollider == null)
            {
                transform.position += displacement;
                return;
            }

            GetCapsule(
                out Vector3 top,
                out Vector3 bottom,
                out float radius);
            float distance = displacement.magnitude;
            int count = Physics.CapsuleCastNonAlloc(
                top,
                bottom,
                radius,
                displacement / distance,
                hitBuffer,
                distance + Skin,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            RaycastHit nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider target = hitBuffer[i].collider;
                if (target == null ||
                    target.transform == transform ||
                    target.transform.IsChildOf(transform) ||
                    (target == launchPadCollider && velocity.y > 0f) ||
                    hitBuffer[i].distance >= nearestDistance)
                {
                    continue;
                }
                nearest = hitBuffer[i];
                nearestDistance = hitBuffer[i].distance;
            }

            if (nearest.collider == null)
            {
                transform.position += displacement;
                return;
            }

            float safeDistance = Mathf.Max(0f, nearest.distance - Skin);
            transform.position += displacement.normalized * safeDistance;
            if (velocity.y <= 0f && nearest.normal.y >= 0.45f)
            {
                StopFlight(true);
                return;
            }

            velocity = Vector3.ProjectOnPlane(velocity, nearest.normal);
        }

        private void GetCapsule(
            out Vector3 top,
            out Vector3 bottom,
            out float radius)
        {
            Vector3 scale = playerCollider.transform.lossyScale;
            float horizontalScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.z));
            radius = playerCollider.radius * horizontalScale;
            float height = Mathf.Max(
                playerCollider.height * Mathf.Abs(scale.y),
                radius * 2f);
            Vector3 center = playerCollider.transform.TransformPoint(
                playerCollider.center);
            float halfSegment = Mathf.Max(height * 0.5f - radius, 0f);
            Vector3 offset = playerCollider.transform.up * halfSegment;
            top = center + offset;
            bottom = center - offset;
        }

        private void StopFlight(bool restoreMovement)
        {
            IsFlying = false;
            velocity = Vector3.zero;
            launchPadCollider = null;
            if (restoreMovement && playerMovement != null)
            {
                playerMovement.enabled = true;
            }
        }

        private void OnDisable()
        {
            IsFlying = false;
            velocity = Vector3.zero;
            launchPadCollider = null;
        }
    }
}
