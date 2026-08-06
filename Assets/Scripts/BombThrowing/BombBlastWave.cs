using System.Collections.Generic;
using UnityEngine;

namespace SuperFinalFuse.BombThrowing
{
    /// <summary>
    /// Runtime shockwave created by a thrown bomb. Gameplay detection and the
    /// optional debug ring use the same expanding radius.
    /// </summary>
    public sealed class BombBlastWave : MonoBehaviour
    {
        private readonly HashSet<IBombBlastReactive> hitReactives =
            new HashSet<IBombBlastReactive>();

        private float maximumRadius;
        private float expansionSpeed;
        private float currentRadius;
        private LayerMask targetLayers;
        private bool showRing;
        private Color ringColor;
        private LineRenderer ring;
        private Material runtimeMaterial;
        private GameObject blastOwner;

        public static void Create(
            Vector3 center,
            float radius,
            float speed,
            LayerMask layers,
            bool showDebugRing,
            Color debugColor,
            GameObject owner = null)
        {
            GameObject target = new GameObject("Bomb Blast Wave");
            target.transform.position = center;
            BombBlastWave wave = target.AddComponent<BombBlastWave>();
            wave.Initialize(
                radius,
                speed,
                layers,
                showDebugRing,
                debugColor,
                owner);
        }

        private void Initialize(
            float radius,
            float speed,
            LayerMask layers,
            bool debugRing,
            Color debugColor,
            GameObject owner)
        {
            maximumRadius = Mathf.Max(0.01f, radius);
            expansionSpeed = Mathf.Max(0.01f, speed);
            targetLayers = layers;
            showRing = debugRing;
            ringColor = debugColor;
            blastOwner = owner;

            if (showRing)
            {
                CreateRing();
                UpdateRing();
            }
        }

        private void Update()
        {
            currentRadius = Mathf.Min(
                maximumRadius,
                currentRadius + expansionSpeed * Time.deltaTime);

            DetectReachedTargets();
            if (showRing)
            {
                UpdateRing();
            }

            if (currentRadius >= maximumRadius)
            {
                Destroy(gameObject);
            }
        }

        private void DetectReachedTargets()
        {
            Collider[] colliders = Physics.OverlapSphere(
                transform.position,
                currentRadius,
                targetLayers,
                QueryTriggerInteraction.Collide);

            foreach (Collider targetCollider in colliders)
            {
                MonoBehaviour[] behaviours =
                    targetCollider.GetComponentsInParent<MonoBehaviour>(true);

                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour is not IBombBlastReactive reactive ||
                        !hitReactives.Add(reactive))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(
                        transform.position,
                        targetCollider.bounds.ClosestPoint(transform.position));
                    reactive.OnBombBlast(new BombBlastHit(
                        transform.position,
                        maximumRadius,
                        distance,
                        blastOwner));
                    break;
                }
            }
        }

        private void CreateRing()
        {
            ring = gameObject.AddComponent<LineRenderer>();
            ring.loop = true;
            ring.useWorldSpace = false;
            ring.positionCount = 96;
            ring.widthMultiplier = 0.12f;
            ring.startColor = ringColor;
            ring.endColor = ringColor;
            ring.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                runtimeMaterial = new Material(shader)
                {
                    name = "Runtime Bomb Blast Wave Material",
                    color = ringColor
                };
                ring.material = runtimeMaterial;
            }
        }

        private void UpdateRing()
        {
            if (ring == null)
            {
                return;
            }

            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * currentRadius,
                    0.05f,
                    Mathf.Sin(angle) * currentRadius));
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }
    }
}
