using UnityEngine;

namespace SuperFinalFuse.Grass
{
    /// <summary>
    /// Marks an object as a grass pusher: grass blades rendered by
    /// Custom/GrassGeometry bend away from this collider. Add to the
    /// player, bombs, moving crates, etc.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrassPusher : MonoBehaviour
    {
        [Header("Push")]
        [Min(0.05f)]
        [Tooltip("Influence radius in world units. Blades closer than " +
                 "this bend away, strongest at the centre.")]
        [SerializeField] private float radius = 1.5f;
        [Min(0f)]
        [Tooltip("How hard blades are pushed. 1 = blades at the centre " +
                 "bend a full blade-height away.")]
        [SerializeField] private float strength = 1f;
        [Tooltip("Push from the collider's bounds center instead of the " +
                 "transform position.")]
        [SerializeField] private bool useColliderCenter = true;

        private Collider cachedCollider;

        public float Radius => radius;
        public float Strength => strength;

        public Vector3 PushCenter
        {
            get
            {
                if (useColliderCenter && cachedCollider != null)
                {
                    return cachedCollider.bounds.center;
                }

                return transform.position;
            }
        }

        private void Awake()
        {
            cachedCollider = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            GrassPushManager.Instance.Register(this);
        }

        private void OnDisable()
        {
            GrassPushManager.Instance.Unregister(this);
        }
    }
}
