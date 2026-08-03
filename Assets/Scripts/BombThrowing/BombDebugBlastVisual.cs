using UnityEngine;

namespace SuperFinalFuse.BombThrowing
{
    public sealed class BombDebugBlastVisual : MonoBehaviour
    {
        private float targetRadius;
        private float duration;
        private float elapsed;
        private LineRenderer line;

        public static void Create(Vector3 center, float radius, Color color)
        {
            GameObject target = new GameObject("Bomb Blast Debug Ring");
            target.transform.position = center;
            BombDebugBlastVisual visual = target.AddComponent<BombDebugBlastVisual>();
            visual.Initialize(radius, color);
        }

        private void Initialize(float radius, Color color)
        {
            targetRadius = radius;
            duration = 0.3f;
            line = gameObject.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = 96;
            line.widthMultiplier = 0.12f;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader) { color = color };
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float radius = Mathf.Lerp(0f, targetRadius, elapsed / duration);
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    0.05f,
                    Mathf.Sin(angle) * radius));
            }

            if (elapsed >= duration)
            {
                Destroy(line.material);
                Destroy(gameObject);
            }
        }
    }
}
