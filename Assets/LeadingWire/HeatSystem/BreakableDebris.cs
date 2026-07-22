using System.Collections;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 可破坏碎石/障碍物。放置在热管上，当爆炸级联到达时被炸碎。
    ///
    /// 作为 HeatLine 的子物体放置。
    /// 可关联到指定索引的管线段，或自动根据最近管线段触发。
    /// </summary>
    public class BreakableDebris : MonoBehaviour
    {
        [Header("触发设置")]
        [SerializeField, Tooltip("关联的管线段索引（-1 = 自动找最近的管线段）")]
        private int linkedSegmentIndex = -1;

        [SerializeField, Tooltip("触发半径（自动关联时使用）")]
        private float triggerRadius = 0.8f;

        [Header("爆炸效果")]
        [SerializeField, Tooltip("爆炸粒子预制体")]
        private GameObject explosionEffectPrefab;

        [SerializeField, Tooltip("碎片飞出力度")]
        [Range(1f, 20f)]
        private float fragmentForce = 5f;

        [SerializeField, Tooltip("碎片数量（无特效预制体时自动生成）")]
        [Range(3, 30)]
        private int fragmentCount = 8;

        [SerializeField, Tooltip("碎片存在时间（秒）")]
        [Range(0.5f, 5f)]
        private float fragmentLifetime = 1.5f;

        [Header("调试")]
        [SerializeField, Tooltip("是否已被引爆")]
        private bool hasExploded = false;

        /// <summary>已关联的管线段索引</summary>
        public int LinkedSegmentIndex => linkedSegmentIndex;

        /// <summary>是否已引爆</summary>
        public bool HasExploded => hasExploded;

        /// <summary>触发半径</summary>
        public float TriggerRadius => triggerRadius;

        /// <summary>
        /// 引爆该碎石，产生爆炸效果并销毁自身。
        /// </summary>
        public void Explode()
        {
            if (hasExploded) return;
            hasExploded = true;

            Debug.Log($"💥 [碎石: {name}] 被炸碎！");

            if (explosionEffectPrefab != null)
            {
                var fx = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
                Destroy(fx, fragmentLifetime);
            }
            else
            {
                // 无特效时自动生成飞溅碎片
                StartCoroutine(SpawnFragments());
            }

            // 延迟销毁（等粒子/碎片播完）
            Destroy(gameObject, 0.05f);
        }

        /// <summary>
        /// 生成飞溅碎片（无粒子预制体时的默认效果）。
        /// </summary>
        private IEnumerator SpawnFragments()
        {
            Vector3 center = transform.position;
            Color originalColor = GetComponent<MeshRenderer>() != null
                ? GetComponent<MeshRenderer>().material.color
                : new Color(0.4f, 0.35f, 0.3f);

            for (int i = 0; i < fragmentCount; i++)
            {
                var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frag.name = $"Fragment_{i}";
                frag.transform.position = center + Random.insideUnitSphere * 0.2f;
                frag.transform.localScale = Vector3.one * Random.Range(0.08f, 0.2f);
                frag.transform.rotation = Random.rotation;

                // 颜色
                var rend = frag.GetComponent<MeshRenderer>();
                if (rend != null)
                    rend.material.color = originalColor * Random.Range(0.7f, 1.2f);

                // 物理
                var rb = frag.AddComponent<Rigidbody>();
                rb.AddExplosionForce(fragmentForce, center, 1.5f, 1f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * fragmentForce * 2f, ForceMode.Impulse);

                Destroy(frag, fragmentLifetime);
            }

            yield return null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 未引爆时显示关联范围
            if (!hasExploded)
            {
                Gizmos.color = new Color(0.8f, 0.4f, 0.1f, 0.5f);
                Gizmos.DrawSphere(transform.position, triggerRadius * 0.5f);
                Gizmos.DrawWireCube(transform.position, transform.localScale);

                UnityEditor.Handles.color = new Color(0.8f, 0.4f, 0.1f);
                UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f,
                    linkedSegmentIndex >= 0 ? $"🪨 Seg[{linkedSegmentIndex}]" : "🪨 Auto");
            }
        }
#endif
    }
}
