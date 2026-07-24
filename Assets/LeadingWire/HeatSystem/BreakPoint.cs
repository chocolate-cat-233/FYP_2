using System;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 断路点。热管上的断口，箱子推入 Trigger → 连接，箱子离开 → 断开。
    /// 作为 HeatLine 的子物体放置。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BreakPoint : MonoBehaviour
    {
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

        /// <summary>当前连接的箱子（公开只读）</summary>
        public PushableBox ConnectedBox => connectedBox;

        private PushableBox connectedBox;
        private int connectedBoxOverlapCount;

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

        #region Trigger

        private void OnTriggerEnter(Collider other)
        {
            PushableBox box = other.GetComponent<PushableBox>()
                              ?? other.GetComponentInParent<PushableBox>();
            if (box == null) return;

            if (box == connectedBox)
            {
                connectedBoxOverlapCount++;
                return;
            }

            if (isConnected) return;

            Debug.Log($"[断路点: {name}] 箱子 [{box.name}] 进入 → 连接！");
            connectedBoxOverlapCount = 1;
            connectedBox = box;
            Connect();
        }

        private void OnTriggerExit(Collider other)
        {
            PushableBox box = other.GetComponent<PushableBox>()
                              ?? other.GetComponentInParent<PushableBox>();
            if (box == null) return;

            if (box == connectedBox)
            {
                connectedBoxOverlapCount--;
                if (connectedBoxOverlapCount <= 0)
                {
                    connectedBoxOverlapCount = 0;
                    Debug.Log($"[断路点: {name}] 箱子 [{box.name}] 全部离开 → 断开！");
                    connectedBox = null;
                    Disconnect();
                }
            }
        }

        #endregion

        #region Connection

        public void Connect()
        {
            if (isConnected) return;
            isConnected = true;
            Debug.Log($"✅ [断路点: {name}] 已连接！");
            UpdateVisuals();
            OnConnectionChanged?.Invoke();
        }

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

#if UNITY_EDITOR
        [ContextMenu("切换连接状态")]
        private void ToggleConnection()
        {
            if (isConnected) Disconnect(); else Connect();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isConnected ? Color.green : Color.red;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.25f);
            UnityEditor.Handles.color = isConnected ? Color.green : Color.red;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.2f,
                $"{name} {(isConnected ? "✅" : "❌")}");
        }
#endif
    }
}
