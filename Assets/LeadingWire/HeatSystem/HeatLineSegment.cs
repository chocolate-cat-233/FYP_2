using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 热管线段。热管视觉上被拆分为多个线段，从上游到下游排列。
    ///
    /// 作为 HeatLine 的子物体放置（手动排列或自动生成）。
    /// 当热量流过时，线段从靠近 Provider 的一端开始逐个点亮。
    /// </summary>
    public class HeatLineSegment : MonoBehaviour
    {
        [Header("颜色设置")]
        [SerializeField, Tooltip("无热量时的颜色")]
        private Color offColor = new Color(0.25f, 0.25f, 0.25f);

        [SerializeField, Tooltip("点亮瞬间的颜色（橙色，模拟爆炸传导）")]
        private Color energizingColor = new Color(1f, 0.5f, 0f);

        [SerializeField, Tooltip("有能量流动时的颜色")]
        private Color energizedColor = new Color(1f, 0.12f, 0f);

        [SerializeField, Tooltip("爆炸后无流动时的颜色")]
        private Color explodedColor = new Color(1f, 0.45f, 0f);

        [Header("调试")]
        [SerializeField, Tooltip("当前状态")]
        private SegmentState currentState = SegmentState.Off;

        private MeshRenderer _renderer;

        /// <summary>当前线段状态</summary>
        public SegmentState CurrentState => currentState;

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// 设置为无热量状态（灰色）
        /// </summary>
        public void SetOff()
        {
            currentState = SegmentState.Off;
            ApplyColor(offColor);
        }

        /// <summary>
        /// 设置为正在通电中（橙色，级联动画期间）
        /// </summary>
        public void SetEnergizing()
        {
            currentState = SegmentState.Energizing;
            ApplyColor(energizingColor);
        }

        /// <summary>
        /// 设置为已通电（红色，热流活跃）
        /// </summary>
        public void SetEnergized()
        {
            currentState = SegmentState.Energized;
            ApplyColor(energizedColor);
        }

        /// <summary>
        /// 设置为爆炸后无流动（橙色）
        /// </summary>
        public void SetExploded()
        {
            currentState = SegmentState.Exploded;
            ApplyColor(explodedColor);
        }

        private void ApplyColor(Color color)
        {
            if (_renderer != null)
                _renderer.material.color = color;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            Gizmos.color = currentState switch
            {
                SegmentState.Off => Color.gray,
                SegmentState.Energizing => new Color(1f, 0.5f, 0f),
                SegmentState.Energized => Color.red,
                SegmentState.Exploded => new Color(1f, 0.5f, 0f),
                _ => Color.gray
            };
            Gizmos.DrawWireCube(transform.position, transform.localScale * 1.1f);
        }
#endif
    }

    /// <summary>
    /// 线段视觉状态
    /// </summary>
    public enum SegmentState
    {
        Off,        // 灰色 — 无热量
        Energizing, // 橙色 — 正在被级联点亮
        Energized,  // 红色 — 有能量
        Exploded    // 橙色 — 已爆炸但无流动
    }
}
