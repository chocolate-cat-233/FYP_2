using System.Collections.Generic;
using UnityEngine;
using SuperFinalFuse.BombThrowing.Buffs;
using SuperFinalFuse.BombThrowing.Mechanisms;

namespace SuperFinalFuse.BombThrowing
{
    [DisallowMultipleComponent]
    public sealed class BombTrajectoryPreview : MonoBehaviour
    {
        [Header("Trajectory")]
        [Min(0.5f)]
        [SerializeField] private float maximumPreviewTime = 5f;
        [Range(0.01f, 0.2f)]
        [SerializeField] private float simulationStep = 0.04f;
        [SerializeField] private LayerMask collisionLayers = ~0;

        [Header("Dashed line")]
        [Min(0.01f)]
        [SerializeField] private float lineWidth = 0.06f;
        [Min(0.05f)]
        [SerializeField] private float dashWorldLength = 0.3f;
        [SerializeField] private Color safeColor = Color.white;
        [SerializeField] private Color dangerousColor = Color.red;

        private readonly List<Vector3> points = new List<Vector3>(128);
        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
        private LineRenderer line;
        private Material lineMaterial;
        private Texture2D dashTexture;
        private Collider[] playerColliders;

        public bool IsVisible => line != null && line.enabled;
        public bool IsDangerous { get; private set; }

        private void Awake()
        {
            playerColliders = GetComponentsInChildren<Collider>();
            CreateLine();
            Hide();
        }

        public void Show(BombThrower thrower)
        {
            if (thrower == null ||
                !thrower.TryGetTrajectoryData(
                    out Vector3 origin,
                    out Vector3 velocity,
                    out BombProjectile prefab))
            {
                Hide();
                return;
            }

            if (line == null)
            {
                CreateLine();
            }

            Simulate(origin, velocity, prefab);
            line.enabled = points.Count > 1;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            Color color = IsDangerous ? dangerousColor : safeColor;
            line.startColor = color;
            line.endColor = color;
            if (lineMaterial != null)
            {
                lineMaterial.color = color;
                float length = CalculateLineLength();
                lineMaterial.mainTextureScale = new Vector2(
                    Mathf.Max(1f, length / dashWorldLength),
                    1f);
            }
        }

        public void Hide()
        {
            IsDangerous = false;
            if (line != null)
            {
                line.enabled = false;
                line.positionCount = 0;
            }
        }

        private void Simulate(
            Vector3 origin,
            Vector3 initialVelocity,
            BombProjectile prefab)
        {
            points.Clear();
            points.Add(origin);
            IsDangerous = false;

            float gravityMultiplier = Mathf.Max(
                0f,
                prefab.EffectiveGravityMultiplier);
            Vector3 gravity = Physics.gravity * gravityMultiplier;
            float radius = GetBombRadius(prefab);
            Vector3 position = origin;
            Vector3 velocity = initialVelocity;
            bool predictsExplosion = false;
            Vector3 explosionPoint = Vector3.zero;

            int steps = Mathf.CeilToInt(
                maximumPreviewTime / simulationStep);
            for (int step = 0; step < steps; step++)
            {
                velocity += gravity * simulationStep;
                Vector3 displacement = velocity * simulationStep;
                float distance = displacement.magnitude;
                if (distance <= 0.00001f)
                {
                    continue;
                }

                int hitCount = Physics.SphereCastNonAlloc(
                    position,
                    radius,
                    displacement / distance,
                    hitBuffer,
                    distance,
                    collisionLayers,
                    QueryTriggerInteraction.Collide);
                if (TryGetNearestValidHit(
                        hitCount,
                        out RaycastHit nearestHit))
                {
                    position += displacement.normalized * nearestHit.distance;
                    points.Add(position);
                    predictsExplosion =
                        PredictsExplosionOn(nearestHit.collider);
                    explosionPoint = position;
                    break;
                }

                position += displacement;
                points.Add(position);
            }

            if (predictsExplosion)
            {
                Vector3 nearestPlayerPoint = GetClosestPlayerPoint(
                    explosionPoint);
                IsDangerous =
                    Vector3.Distance(explosionPoint, nearestPlayerPoint) <=
                    prefab.BlastRadius;
            }
        }

        private bool TryGetNearestValidHit(
            int hitCount,
            out RaycastHit nearest)
        {
            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                Collider target = hitBuffer[i].collider;
                if (target == null || IsPlayerCollider(target) ||
                    hitBuffer[i].distance >= nearestDistance)
                {
                    continue;
                }

                nearest = hitBuffer[i];
                nearestDistance = hitBuffer[i].distance;
            }
            return nearest.collider != null;
        }

        private bool PredictsExplosionOn(Collider target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.GetComponentInParent<BombPipe>() != null ||
                target.GetComponentInParent<BombJumpPad>() != null ||
                target.GetComponentInParent<BombRailPlatform>() != null ||
                target.GetComponentInParent<BombAntiAirZone>() != null)
            {
                return false;
            }

            BombTeleportTarget teleport =
                target.GetComponentInParent<BombTeleportTarget>();
            if (teleport != null)
            {
                BombBuffController buffs =
                    GetComponent<BombBuffController>();
                return buffs == null ||
                       !buffs.HasBuff(BombBuffType.TeleportBomb);
            }
            return true;
        }

        private bool IsPlayerCollider(Collider target)
        {
            if (playerColliders == null)
            {
                return false;
            }
            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider == target)
                {
                    return true;
                }
            }
            return false;
        }

        private Vector3 GetClosestPlayerPoint(Vector3 point)
        {
            Vector3 closest = transform.position;
            float closestDistance = float.PositiveInfinity;
            if (playerColliders == null)
            {
                return closest;
            }

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider == null || !playerCollider.enabled)
                {
                    continue;
                }
                Vector3 candidate = playerCollider.ClosestPoint(point);
                float distance = (candidate - point).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private static float GetBombRadius(BombProjectile prefab)
        {
            SphereCollider sphere = prefab.GetComponentInChildren<SphereCollider>();
            if (sphere != null)
            {
                Vector3 scale = sphere.transform.lossyScale;
                return sphere.radius * Mathf.Max(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z));
            }
            return 0.4f;
        }

        private void CreateLine()
        {
            line = GetComponent<LineRenderer>();
            if (line == null)
            {
                line = gameObject.AddComponent<LineRenderer>();
            }
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = lineWidth;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            dashTexture = new Texture2D(16, 1, TextureFormat.RGBA32, false)
            {
                name = "Runtime Bomb Trajectory Dash",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            for (int x = 0; x < dashTexture.width; x++)
            {
                dashTexture.SetPixel(
                    x,
                    0,
                    x < dashTexture.width / 2
                        ? Color.white
                        : new Color(1f, 1f, 1f, 0f));
            }
            dashTexture.Apply();

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader != null)
            {
                lineMaterial = new Material(shader)
                {
                    name = "Runtime Bomb Trajectory Material",
                    mainTexture = dashTexture
                };
                line.material = lineMaterial;
            }
        }

        private float CalculateLineLength()
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector3.Distance(points[i - 1], points[i]);
            }
            return length;
        }

        private void OnDestroy()
        {
            if (lineMaterial != null)
            {
                Destroy(lineMaterial);
            }
            if (dashTexture != null)
            {
                Destroy(dashTexture);
            }
        }
    }
}
