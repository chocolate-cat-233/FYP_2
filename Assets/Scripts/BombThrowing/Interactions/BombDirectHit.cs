using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Interactions
{
    public readonly struct BombDirectHit
    {
        public BombDirectHit(
            BombProjectile projectile,
            Collider collider,
            Vector3 point,
            Vector3 normal,
            Vector3 relativeVelocity,
            bool isSolidCollision)
        {
            Projectile = projectile;
            Collider = collider;
            Point = point;
            Normal = normal;
            RelativeVelocity = relativeVelocity;
            IsSolidCollision = isSolidCollision;
        }

        public BombProjectile Projectile { get; }
        public Collider Collider { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public Vector3 RelativeVelocity { get; }
        public bool IsSolidCollision { get; }
        public GameObject Owner => Projectile != null ? Projectile.Owner : null;
        public BombOwnerType OwnerType => Projectile != null
            ? Projectile.OwnerType
            : BombOwnerType.Environment;
    }
}
