using UnityEngine;

namespace SuperFinalFuse.BombThrowing
{
    public readonly struct BombBlastHit
    {
        public BombBlastHit(Vector3 origin, float radius, float distance)
        {
            Origin = origin;
            Radius = radius;
            Distance = distance;
        }

        public Vector3 Origin { get; }
        public float Radius { get; }
        public float Distance { get; }
    }
}
