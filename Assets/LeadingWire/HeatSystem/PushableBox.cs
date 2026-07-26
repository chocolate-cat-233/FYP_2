using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 可推箱子。玩家推动箱子来桥接断路点。
    ///
    /// 触点由设计师手动放置：在箱子下创建子物体，挂 BoxCollider(isTrigger=true)。
    /// 形状自由——I 形(两端)、L 形(直角)、T 形(三端)皆可。
    /// BreakPoint 检测到任意属于箱子的 Collider 进入即触发。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
   // [RequireComponent(typeof(BoxCollider))]
    public class PushableBox : MonoBehaviour
    {
        [Header("箱子设置")]
        [SerializeField, Tooltip("箱子的显示名称")]
        private string boxName = "箱子";

        [Header("视觉反馈")]
        [SerializeField, Tooltip("用于显示充能状态的 MeshRenderer")]
        private MeshRenderer targetRenderer;

        [SerializeField, Tooltip("无连接的颜色")]
        private Color idleColor = new Color(0.4f, 0.4f, 0.4f);

        [SerializeField, Tooltip("连接1个断点的颜色")]
        private Color partialColor = new Color(1f, 0.85f, 0f);

        [SerializeField, Tooltip("连接2+个断点的颜色（导通）")]
        private Color bridgingColor = Color.green;

        [Header("调试")]
        [SerializeField, Tooltip("当前移动方向（只读）")]
        private Vector3 debugMoveDirection;

        [SerializeField, Tooltip("当前连接的断点数")]
        private int debugConnectedCount;

        private Vector3 lastPosition;
        private Vector3 _lastMoveDirection;
        private Rigidbody rb;

        /// <summary>上一次移动的方向（世界坐标系）</summary>
        public Vector3 LastMoveDirection => _lastMoveDirection;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb != null)
                rb.constraints = RigidbodyConstraints.FreezeRotation;

            //// 确保本体有碰撞体（用于物理推动）
            //var col = GetComponent<BoxCollider>();
            //if (col != null && col.isTrigger)
            //    col.isTrigger = false; // 本体不参与触发，子触点才是 trigger

            lastPosition = transform.position;
        }

        private void Update()
        {
            TrackMovement();
            UpdateChargeVisual();
        }

        private void TrackMovement()
        {
            Vector3 pos = transform.position;
            Vector3 delta = pos - lastPosition;
            if (delta.magnitude > 0.001f)
            {
                _lastMoveDirection = delta.normalized;
                debugMoveDirection = _lastMoveDirection;
            }
            lastPosition = pos;
        }

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
                targetRenderer.material.color = bridgingColor;
            else if (count == 1)
                targetRenderer.material.color = partialColor;
            else
                targetRenderer.material.color = idleColor;
        }

        private int GetConnectedBreakPointCount()
        {
            int count = 0;
            var all = Object.FindObjectsByType<BreakPoint>(FindObjectsSortMode.None);
            foreach (var bp in all)
                if (bp.IsConnected && bp.ConnectedBox == this)
                    count++;
            return count;
        }

        /// <summary>
        /// 测试用：向指定方向推箱子。
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

            //Gizmos.color = Color.cyan;
            //Gizmos.DrawWireCube(transform.position, GetComponent<BoxCollider>()?.size ?? Vector3.one * 0.5f);

            if (_lastMoveDirection.magnitude > 0.01f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _lastMoveDirection * 0.5f);
            }

            var style = new GUIStyle();
            style.normal.textColor = Color.cyan;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, $"📦 {boxName}");
        }

        [ContextMenu("向北推 1 单位")] private void PushNorth() => Push(Vector3.forward);
        [ContextMenu("向南推 1 单位")] private void PushSouth() => Push(Vector3.back);
        [ContextMenu("向东推 1 单位")] private void PushEast() => Push(Vector3.right);
        [ContextMenu("向西推 1 单位")] private void PushWest() => Push(Vector3.left);
#endif
    }
}
