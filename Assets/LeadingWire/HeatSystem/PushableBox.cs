using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 可推箱子。有两个端点触发器，分别连接两段导线的断路点。
    /// 玩家推动箱子使两端同时进入两个断路点，连通线路。
    ///
    /// 结构：
    ///   PushableBox (Rigidbody + BoxCollider)
    ///   ├── Visual (Cube, 仅显示)
    ///   ├── EndA (子物体, BoxCollider isTrigger) — 连接上游断路点
    ///   └── EndB (子物体, BoxCollider isTrigger) — 连接下游断路点
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableBox : MonoBehaviour
    {
        [Header("箱子设置")]
        [SerializeField, Tooltip("箱子的显示名称")]
        private string boxName = "箱子";

        [SerializeField, Tooltip("端点触发器的半尺寸")]
        private float endTriggerSize = 0.35f;

        [SerializeField, Tooltip("两端点沿 Z 轴的偏移距离")]
        private float endOffset = 0.6f;

        [Header("视觉反馈")]
        [SerializeField, Tooltip("用于显示充能状态的 MeshRenderer")]
        private MeshRenderer targetRenderer;

        [SerializeField, Tooltip("无连接的颜色")]
        private Color idleColor = new Color(0.4f, 0.4f, 0.4f);

        [SerializeField, Tooltip("连接1个断点的颜色")]
        private Color partialColor = new Color(1f, 0.85f, 0f);

        [SerializeField, Tooltip("连接2个断点的颜色（导通）")]
        private Color bridgingColor = Color.green;

        [Header("调试")]
        [SerializeField, Tooltip("当前移动方向（只读）")]
        private Vector3 debugMoveDirection;

        [SerializeField, Tooltip("当前连接的断点数")]
        private int debugConnectedCount;

        private Vector3 lastPosition;
        private Vector3 _lastMoveDirection;

        /// <summary>上一次移动的方向（世界坐标系）</summary>
        public Vector3 LastMoveDirection => _lastMoveDirection;

        /// <summary>端点 A 的触发器</summary>
        public Collider EndpointA { get; private set; }

        /// <summary>端点 B 的触发器</summary>
        public Collider EndpointB { get; private set; }

        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.constraints = RigidbodyConstraints.FreezeRotation;

            lastPosition = transform.position;

            // 创建两个端点触发器子物体
            EndpointA = CreateEndpoint("EndA", Vector3.forward * endOffset);
            EndpointB = CreateEndpoint("EndB", Vector3.back * endOffset);
        }

        private Collider CreateEndpoint(string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.layer = gameObject.layer;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one * endTriggerSize * 2f;

            return col;
        }

        private void Update()
        {
            TrackMovement();
            UpdateChargeVisual();
        }

        /// <summary>
        /// 更新充能颜色：灰(0) → 黄(1) → 绿(2)
        /// </summary>
        private void UpdateChargeVisual()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
                if (targetRenderer == null)
                    targetRenderer = GetComponentInChildren<MeshRenderer>();
            }
            if (targetRenderer == null) return;

            int count = GetConnectedBreakPointCount();
            debugConnectedCount = count;

            if (count >= 2)
                targetRenderer.material.color = bridgingColor;   // 绿 — 导通
            else if (count == 1)
                targetRenderer.material.color = partialColor;    // 黄 — 部分
            else
                targetRenderer.material.color = idleColor;       // 灰 — 无
        }

        private int GetConnectedBreakPointCount()
        {
            int count = 0;
            var allBPs = Object.FindObjectsByType<BreakPoint>(FindObjectsSortMode.None);
            foreach (var bp in allBPs)
                if (bp.IsConnected && bp.ConnectedBox == this)
                    count++;
            return count;
        }

        private void TrackMovement()
        {
            Vector3 currentPos = transform.position;
            Vector3 delta = currentPos - lastPosition;

            if (delta.magnitude > 0.001f)
            {
                _lastMoveDirection = delta.normalized;
                debugMoveDirection = _lastMoveDirection;
            }

            lastPosition = currentPos;
        }

        /// <summary>
        /// 模拟向指定方向推箱子（用于测试）。
        /// </summary>
        public void Push(Vector3 direction, float distance = 1f)
        {
            transform.position += direction.normalized * distance;
            _lastMoveDirection = direction.normalized;
            debugMoveDirection = _lastMoveDirection;
            lastPosition = transform.position;
            Debug.Log($"[箱子: {boxName}] 被推向 {direction.normalized}, 距离={distance}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                lastPosition = transform.position;

            // 箱子本体
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);

            // 端点
            if (EndpointA != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(EndpointA.transform.position, Vector3.one * endTriggerSize * 2f);
            }
            if (EndpointB != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(EndpointB.transform.position, Vector3.one * endTriggerSize * 2f);
            }

            // 移动方向
            if (_lastMoveDirection.magnitude > 0.01f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _lastMoveDirection * 0.5f);
            }

            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"📦 {boxName}");
            UnityEditor.Handles.color = Color.green;
            UnityEditor.Handles.Label(EndpointA?.transform.position + Vector3.up * 0.2f ?? Vector3.zero, "A");
            UnityEditor.Handles.color = Color.blue;
            UnityEditor.Handles.Label(EndpointB?.transform.position + Vector3.up * 0.2f ?? Vector3.zero, "B");
        }

        [ContextMenu("向北推 1 单位")]
        private void PushNorth() => Push(Vector3.forward);

        [ContextMenu("向南推 1 单位")]
        private void PushSouth() => Push(Vector3.back);

        [ContextMenu("向东推 1 单位")]
        private void PushEast() => Push(Vector3.right);

        [ContextMenu("向西推 1 单位")]
        private void PushWest() => Push(Vector3.left);
#endif
    }
}
