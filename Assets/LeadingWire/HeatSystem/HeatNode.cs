using System.Collections.Generic;
using UnityEngine;

namespace HeatSystem
{
    /// <summary>
    /// 节点的逻辑类型。
    /// </summary>
    public enum NodeType
    {
        /// <summary>中继器：任意输入有热量即输出</summary>
        [Tooltip("中继器：任意一个输入有热量，就输出热量（逻辑或）")]
        Repeater,

        /// <summary>与门：所有输入都有热量才输出</summary>
        [Tooltip("与门：所有输入热管都传来热量，才输出热量（逻辑与）")]
        AndGate
    }

    /// <summary>
    /// 热量节点。接收上游热管的热量输入，根据节点类型判断是否向下游输出。
    ///
    /// - 中继器(Repeater)：1条或多条输入，任意一条有热量 → 输出开启
    /// - 与门(AndGate)：多条输入，全部输入都有热量 → 输出开启
    ///
    /// 在 Scene 中拖拽布置，连接在热管之间作为逻辑判断中枢。
    /// </summary>
    public class HeatNode : HeatProvider, IHeatAcceptor
    {
        [Header("节点设置")]
        [SerializeField, Tooltip("节点逻辑类型")]
        private NodeType nodeType = NodeType.Repeater;

        [SerializeField, Tooltip("与门模式下期望的输入热管总数")]
        private int expectedInputCount = 2;

        [SerializeField, Tooltip("节点的显示名称")]
        private string nodeName = "节点";

        [Header("调试")]
        [SerializeField, Tooltip("在 Inspector 中查看当前输出状态")]
        private bool debugIsOutputting = false;

        // 当前正在提供热量的输入热管集合
        private HashSet<HeatLine> activeInputLines = new HashSet<HeatLine>();

        // 所有曾经 ReceiveHeat 过的热管（用于与门判断）
        private HashSet<HeatLine> allKnownInputLines = new HashSet<HeatLine>();

        private bool _isOutputting = false;

        public override bool IsOutputting => _isOutputting;

        /// <summary>当前活跃的输入热管数量（只读）</summary>
        public int ActiveInputCount => activeInputLines.Count;

        /// <summary>节点逻辑类型</summary>
        public NodeType NodeType => nodeType;

        #region IHeatAcceptor

        public void ReceiveHeat(HeatLine line, bool hasHeat)
        {
            allKnownInputLines.Add(line);

            if (hasHeat)
                activeInputLines.Add(line);
            else
                activeInputLines.Remove(line);

            Debug.Log($"[节点: {nodeName}] 收到热管 [{line.name}] 热量={(hasHeat ? "ON" : "OFF")}, " +
                      $"活跃输入={activeInputLines.Count}/{GetTotalInputCount()}");

            Evaluate();
        }

        #endregion

        /// <summary>
        /// 评估输入条件，决定是否开启输出。
        /// </summary>
        private void Evaluate()
        {
            bool shouldOutput = nodeType switch
            {
                NodeType.Repeater => activeInputLines.Count > 0,
                NodeType.AndGate => activeInputLines.Count >= GetTotalInputCount(),
                _ => false
            };

            if (shouldOutput != _isOutputting)
            {
                _isOutputting = shouldOutput;
                debugIsOutputting = _isOutputting;

                if (_isOutputting)
                {
                    Debug.Log($"[节点: {nodeName}] ✅ 条件满足，开始输出热量！" +
                              $"({(nodeType == NodeType.Repeater ? "中继器" : "与门")})");
                }
                else
                {
                    Debug.Log($"[节点: {nodeName}] ❌ 条件不再满足，停止输出热量。");
                }

                NotifyOutputChanged();
                UpdateVisuals();
            }
        }

        /// <summary>
        /// 获取用于判断的输入总数。
        /// 中继器至少需要1个；与门使用 expectedInputCount 或自动检测到的输入数。
        /// </summary>
        private int GetTotalInputCount()
        {
            return nodeType switch
            {
                NodeType.Repeater => 1,
                // 取配置值和自动检测值中的较大者
                NodeType.AndGate => Mathf.Max(expectedInputCount, allKnownInputLines.Count),
                _ => 1
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (expectedInputCount < 1) expectedInputCount = 1;
            if (nodeType == NodeType.Repeater && expectedInputCount > 1)
                expectedInputCount = 1;
        }

        protected override void OnDrawGizmosSelected()
        {
            Color nodeColor = _isOutputting ? Color.yellow : new Color(1f, 0.6f, 0f);
            Gizmos.color = nodeColor;
            Gizmos.DrawSphere(transform.position, 0.25f);

            string label = nodeType == NodeType.Repeater
                ? $"⏺ {nodeName} (中继器)"
                : $"& {nodeName} (与门 {activeInputLines.Count}/{GetTotalInputCount()})";

            UnityEditor.Handles.color = nodeColor;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, label);
        }
#endif
    }
}
