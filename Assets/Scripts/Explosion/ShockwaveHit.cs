using UnityEngine;

namespace SuperFinalFuse.Explosion
{
    public readonly struct ShockwaveHit
    {
        public ShockwaveHit(
            Vector3 origin,
            Vector3 point,
            Vector3 direction,
            float distance,
            float normalizedDistance,
            float strength)
        {
            Origin = origin;
            Point = point;
            Direction = direction;
            Distance = distance;
            NormalizedDistance = normalizedDistance;
            Strength = strength;
        }

        public Vector3 Origin { get; }
        public Vector3 Point { get; }
        public Vector3 Direction { get; }
        public float Distance { get; }
        public float NormalizedDistance { get; }
        public float Strength { get; }
    }
}
