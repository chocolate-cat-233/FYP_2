using System;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 热量提供者基类。封装了 OnOutputChanged 事件的触发逻辑，
    /// HeatSource 和 HeatNode 继承此类。
    /// 继承此类的组件可以直接拖入 HeatLine 的 provider 字段。
    /// </summary>
    public abstract class HeatProvider : MonoBehaviour, IHeatProvider
    {
        public abstract bool IsOutputting { get; }

        public event Action OnOutputChanged;

        [Header("视觉反馈")]
        [SerializeField, Tooltip("用于显示状态的 MeshRenderer")]
        protected MeshRenderer targetRenderer;

        [SerializeField, Tooltip("有能量时的颜色（红色）")]
        protected Color energizedColor = new Color(1f, 0.15f, 0f);

        [SerializeField, Tooltip("无能量时的默认颜色")]
        protected Color defaultColor = new Color(0.3f, 0.3f, 0.3f);

        /// <summary>
        /// 子类在输出状态变化时调用此方法，通知下游热管重新评估。
        /// </summary>
        protected void NotifyOutputChanged()
        {
            OnOutputChanged?.Invoke();
        }

        /// <summary>
        /// 更新材质颜色以反映当前状态。
        /// 红色 = 有能量输出，灰色 = 无输出。
        /// </summary>
        protected virtual void UpdateVisuals()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
                if (targetRenderer == null)
                    targetRenderer = GetComponentInChildren<MeshRenderer>();
            }
            if (targetRenderer == null) return;
            targetRenderer.material.color = IsOutputting ? energizedColor : defaultColor;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器中选中时绘制连接可视化
        /// </summary>
        protected virtual void OnDrawGizmosSelected()
        {
            // 子类可覆写以自定义可视化
        }
#endif
    }
}
