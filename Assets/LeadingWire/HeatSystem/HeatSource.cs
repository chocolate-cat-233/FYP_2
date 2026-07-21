using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 热源。始终输出热量，是整个热量网络的起点。
    /// 在 Scene 中拖拽布置，作为每条热量链路的根节点。
    /// </summary>
    public class HeatSource : HeatProvider
    {
        [Header("热源设置")]
        [SerializeField, Tooltip("热源的显示名称")]
        private string sourceName = "热源";

        /// <summary>热源始终输出热量</summary>
        public override bool IsOutputting => true;

        private void Start()
        {
            // 场景启动后通知下游热管：热源已就绪
            NotifyOutputChanged();
            UpdateVisuals();
            Debug.Log($"[热源: {sourceName}] 已启动，开始输出热量。");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器中修改任何字段时，立即通知下游更新
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && Application.isPlaying)
                    NotifyOutputChanged();
            };
        }

        protected override void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.position, 0.3f);

            // 绘制图标文字
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, $"🔥 {sourceName}");
        }
#endif
    }
}
