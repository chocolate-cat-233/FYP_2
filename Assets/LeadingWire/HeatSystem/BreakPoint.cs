using System;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 断路点。热管上的断口，玩家从指定方向推入箱子来连接线路。
    ///
    /// 动态连接模型：箱子推入 Trigger → 连接建立 → 热管可流通
    ///               箱子离开 Trigger → 连接断开 → 热管阻断
    ///
    /// 作为 HeatLine 的子物体放置。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BreakPoint : MonoBehaviour
    {
        [Header("断路点设置")]
        [SerializeField, Tooltip("箱子必须从此方向推入（世界坐标系）")]
        private Vector3 requiredPushDirection = Vector3.forward;

        [SerializeField, Tooltip("方向容差角度（0~1，1=完全精确，0.9=约25°容差）")]
        [Range(0.7f, 1f)]
        private float directionTolerance = 0.9f;

        [Header("状态")]
        [SerializeField, Tooltip("编辑器中预设：开始时已连接")]
        private bool startConnected = false;

        [SerializeField, Tooltip("当前是否已连接（调试只读）")]
        private bool isConnected = false;

        [Header("视觉反馈")]
        [SerializeField, Tooltip("用于显示状态的 MeshRenderer")]
        private MeshRenderer targetRenderer;

        [SerializeField, Tooltip("连接后的颜色")]
        private Color connectedColor = Color.green;

        [SerializeField, Tooltip("未连接的颜色")]
        private Color disconnectedColor = new Color(0.3f, 0.3f, 0.3f);

        /// <summary>连接状态变化时触发</summary>
        public event Action OnConnectionChanged;

        /// <summary>当前是否已连接</summary>
        public bool IsConnected => isConnected;

        /// <summary>箱子需要推入的方向（世界坐标系）</summary>
        public Vector3 RequiredPushDirection => requiredPushDirection;

        // 当前保持连接的箱子（离开时断开）
        private PushableBox connectedBox;

        /// <summary>当前连接的箱子（公开只读）</summary>
        public PushableBox ConnectedBox => connectedBox;
        // 已连接箱子的 trigger collider 重叠计数（箱子可能有多个 trigger）
        private int connectedBoxOverlapCount = 0;

        private void Awake()
        {
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;
            if (col.size.magnitude < 0.01f)
                col.size = Vector3.one;
        }

        private void Start()
        {
            if (startConnected) Connect();
            UpdateVisuals();
        }

        #region Trigger Detection

        private void OnTriggerEnter(Collider other)
        {
            PushableBox box = other.GetComponent<PushableBox>()
                              ?? other.GetComponentInParent<PushableBox>();
            if (box == null) return;

            // 如果是已连接的箱子，增加重叠计数
            if (box == connectedBox)
            {
                connectedBoxOverlapCount++;
                Debug.Log($"[断路点: {name}] 箱子 [{box.name}] 额外 collider 进入, 重叠数={connectedBoxOverlapCount}");
                return;
            }

            // 已经连接了其他箱子，忽略
            if (isConnected) return;

            // 新箱子进入，检查方向
            Vector3 pushDir = box.LastMoveDirection;
            if (pushDir.magnitude < 0.01f)
            {
                pushDir = (transform.position - box.transform.position).normalized;
            }

            float dot = Vector3.Dot(pushDir.normalized, requiredPushDirection.normalized);
            Debug.Log($"[断路点: {name}] 箱子 [{box.name}] 进入, 方向={pushDir}, 匹配度={dot:F2}");

            if (dot >= directionTolerance)
            {
                connectedBoxOverlapCount = 1;
                Connect();
                connectedBox = box;
            }
            else
            {
                Debug.Log($"[断路点: {name}] 方向不匹配: 需要={requiredPushDirection.normalized}, 实际={pushDir}");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PushableBox box = other.GetComponent<PushableBox>()
                              ?? other.GetComponentInParent<PushableBox>();
            if (box == null) return;

            if (box == connectedBox)
            {
                connectedBoxOverlapCount--;
                Debug.Log($"[断路点: {name}] 箱子 [{box.name}] collider 离开, 剩余重叠={connectedBoxOverlapCount}");

                if (connectedBoxOverlapCount <= 0)
                {
                    connectedBoxOverlapCount = 0;
                    Debug.Log($"[断路点: {name}] 箱子 [{box.name}] 全部离开触发区，断开连接！");
                    Disconnect();
                    connectedBox = null;
                }
            }
        }

        #endregion

        #region Connection Control

        /// <summary>
        /// 连接断路点（跳过方向检查，用于调试）。
        /// </summary>
        public void Connect()
        {
            if (isConnected) return;
            isConnected = true;
            Debug.Log($"✅ [断路点: {name}] 已连接！");
            UpdateVisuals();
            OnConnectionChanged?.Invoke();
        }

        /// <summary>
        /// 断开断路点。
        /// </summary>
        public void Disconnect()
        {
            if (!isConnected) return;
            isConnected = false;
            connectedBox = null;
            Debug.Log($"⬛ [断路点: {name}] 已断开！");
            UpdateVisuals();
            OnConnectionChanged?.Invoke();
        }

        #endregion

        #region Visuals

        private void UpdateVisuals()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
                if (targetRenderer == null)
                    targetRenderer = GetComponentInChildren<MeshRenderer>();
            }
            if (targetRenderer == null) return;

            targetRenderer.material.color = isConnected ? connectedColor : disconnectedColor;
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        [ContextMenu("切换连接状态(Toggle)")]
        private void ToggleConnection()
        {
            if (isConnected)
                Disconnect();
            else
                Connect();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isConnected ? Color.green : Color.red;
            Vector3 pos = transform.position;
            Vector3 dirEnd = pos + requiredPushDirection.normalized * 0.4f;

            Gizmos.DrawLine(pos, dirEnd);

            // 小箭头
            Vector3 right = Vector3.Cross(Vector3.up, requiredPushDirection.normalized).normalized;
            if (right.magnitude < 0.01f) right = Vector3.right;
            Vector3 arrowBase = dirEnd - requiredPushDirection.normalized * 0.1f;
            Gizmos.DrawLine(dirEnd, arrowBase + right * 0.08f);
            Gizmos.DrawLine(dirEnd, arrowBase - right * 0.08f);

            UnityEditor.Handles.color = isConnected ? Color.green : Color.red;
            string status = isConnected ? "✅" : "❌";
            UnityEditor.Handles.Label(pos + Vector3.up * 0.2f, $"{name} {status}");
        }

        private void OnValidate()
        {
            if (requiredPushDirection.magnitude < 0.01f)
                requiredPushDirection = Vector3.forward;
        }
#endif

        #endregion
    }
}
