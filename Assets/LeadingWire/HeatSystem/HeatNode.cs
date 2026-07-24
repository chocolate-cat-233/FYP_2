using System.Collections.Generic;
using System.Linq;
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
    /// 热量节点（泵）。接收上游热量后不会自动转发——
    /// 需要玩家交互触发，向下游热管释放爆炸。
    ///
    /// 交互条件：
    ///   1. 输入条件满足（中继器：任意输入 / 与门：全部输入）
    ///   2. 所有下游热管的断路点已连接且有下游节点
    ///
    /// 状态：
    ///   待机(灰) → 蓄能中(黄) → 已激活(红)
    /// </summary>
    public class HeatNode : HeatProvider, IHeatAcceptor
    {
        [Header("节点设置")]
        [SerializeField, Tooltip("作为热源（无输入，自动蓄能，可交互激活）")]
        private bool isSource = false;

        [SerializeField, Tooltip("节点逻辑类型（非热源时有效）")]
        private NodeType nodeType = NodeType.Repeater;

        [SerializeField, Tooltip("与门模式下期望的输入热管总数")]
        private int expectedInputCount = 2;

        [SerializeField, Tooltip("节点的显示名称")]
        private string nodeName = "节点";

        [Header("交互")]
        [SerializeField, Tooltip("编辑器中预设：开始时已激活")]
        private bool startActivated = false;

        [SerializeField, Tooltip("需要玩家交互才能触发输出（关闭则为自动转发）")]
        private bool requirePlayerActivation = true;

        [Header("视觉反馈")]
        [SerializeField, Tooltip("蓄能中的颜色（黄色——输入满足，等待玩家交互）")]
        private Color primedColor = new Color(1f, 0.85f, 0f);

        [Header("调试")]
        [SerializeField, Tooltip("在 Inspector 中查看当前输出状态")]
        private bool debugIsOutputting = false;

        [SerializeField, Tooltip("在 Inspector 中查看是否已蓄能")]
        private bool debugIsPrimed = false;

        // 当前正在提供热量的输入热管集合
        private HashSet<HeatLine> activeInputLines = new HashSet<HeatLine>();

        // 所有曾经 ReceiveHeat 过的热管（用于与门判断）
        private HashSet<HeatLine> allKnownInputLines = new HashSet<HeatLine>();

        // 从该节点获取热量的下游热管（Provider == this）
        private HeatLine[] downstreamLines;

        private bool _isOutputting = false;
        private bool _isPrimed = false; // 输入条件满足，等待交互

        public override bool IsOutputting => _isOutputting;

        /// <summary>当前活跃的输入热管数量（只读）</summary>
        public int ActiveInputCount => activeInputLines.Count;

        /// <summary>节点逻辑类型</summary>
        public NodeType NodeType => nodeType;

        /// <summary>下游热管（只读）</summary>
        public IReadOnlyList<HeatLine> DownstreamLines => downstreamLines;

        /// <summary>是否已蓄能，等待玩家交互</summary>
        public bool IsPrimed => _isPrimed;

        private void Start()
        {
            // 缓存下游热管
            downstreamLines = Object.FindObjectsByType<HeatLine>(FindObjectsSortMode.None)
                .Where(l => l.Provider == this)
                .ToArray();

            // 热源模式：自动蓄能
            if (isSource)
            {
                _isPrimed = true;
                debugIsPrimed = true;
                Debug.Log($"[节点: {nodeName}] 🔥 热源模式，自动蓄能！");
            }

            // 初始激活（跳过交互，直接输出）
            if (startActivated)
            {
                _isPrimed = false;
                _isOutputting = true;
                debugIsOutputting = true;
                debugIsPrimed = false;
                Debug.Log($"[节点: {nodeName}] ⚡ 初始激活！");
                NotifyOutputChanged();
            }

            UpdateVisuals();
        }

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

        #region Evaluation

        /// <summary>
        /// 评估输入条件。条件满足时：
        /// - 若 requirePlayerActivation = true → 进入「蓄能」状态，等待玩家交互
        /// - 若 requirePlayerActivation = false → 自动输出（兼容旧行为）
        /// </summary>
        private void Evaluate()
        {
            // 热源模式：输入始终满足
            bool inputMet = isSource || nodeType switch
            {
                NodeType.Repeater => activeInputLines.Count > 0,
                NodeType.AndGate => activeInputLines.Count >= GetTotalInputCount(),
                _ => false
            };

            if (requirePlayerActivation)
            {
                // 手动模式：输入满足 → 蓄能，不等同于输出
                if (inputMet != _isPrimed)
                {
                    _isPrimed = inputMet;
                    debugIsPrimed = _isPrimed;

                    if (_isPrimed)
                    {
                        Debug.Log($"[节点: {nodeName}] ⚡ 蓄能完成，等待玩家交互！" +
                                  $"({(nodeType == NodeType.Repeater ? "中继器" : "与门")})");
                    }
                    else
                    {
                        // 输入断开：清除蓄能，如果已激活则掉电
                        if (_isOutputting)
                        {
                            _isOutputting = false;
                            debugIsOutputting = false;
                            Debug.Log($"[节点: {nodeName}] 💔 输入断开，输出强制掉电！");
                            NotifyOutputChanged();
                        }
                        else
                        {
                            Debug.Log($"[节点: {nodeName}] ⬛ 蓄能解除（输入条件不再满足）。");
                        }
                    }

                    UpdateVisuals();
                }
            }
            else
            {
                // 自动模式：输入满足 → 直接输出
                if (inputMet != _isOutputting)
                {
                    _isOutputting = inputMet;
                    debugIsOutputting = _isOutputting;

                    if (_isOutputting)
                        Debug.Log($"[节点: {nodeName}] ✅ 自动输出！" +
                                  $"({(nodeType == NodeType.Repeater ? "中继器" : "与门")})");
                    else
                        Debug.Log($"[节点: {nodeName}] ❌ 条件不再满足，停止输出。");

                    NotifyOutputChanged();
                    UpdateVisuals();
                }
            }
        }

        private int GetTotalInputCount()
        {
            return nodeType switch
            {
                NodeType.Repeater => 1,
                NodeType.AndGate => Mathf.Max(expectedInputCount, allKnownInputLines.Count),
                _ => 1
            };
        }

        #endregion

        #region Player Interaction

        /// <summary>
        /// 玩家是否可以与这个节点交互。
        /// 条件：已蓄能 + 所有下游热管就绪
        /// </summary>
        public bool CanInteract()
        {
            if (!_isPrimed) return false;
            if (_isOutputting) return false; // 已经激活了

            foreach (var line in downstreamLines)
            {
                if (!line.IsReadyToFlow)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 玩家交互：释放爆炸到下游热管。
        /// </summary>
        /// <returns>是否成功激活</returns>
        public bool Interact()
        {
            if (!CanInteract())
            {
                string reason = "";
                if (!_isPrimed) reason = "尚未蓄能（输入不满足）";
                else if (_isOutputting) reason = "已经激活";
                else
                {
                    foreach (var line in downstreamLines)
                    {
                        if (!line.IsReadyToFlow)
                            reason += $"[{line.name}] 未就绪 ";
                    }
                }
                Debug.Log($"[节点: {nodeName}] ❌ 无法交互: {reason}");
                return false;
            }

            _isOutputting = true;
            _isPrimed = false;
            debugIsOutputting = true;
            debugIsPrimed = false;

            Debug.Log($"[节点: {nodeName}] 💥 玩家激活！释放爆炸到 {downstreamLines.Length} 条下游热管！");
            NotifyOutputChanged();
            UpdateVisuals();

            return true;
        }

        /// <summary>
        /// 获取不能交互的原因（用于 UI 提示）。
        /// </summary>
        public string GetInteractHint()
        {
            if (_isOutputting) return "已激活";
            if (isSource) return "可以交互（热源）";
            if (!_isPrimed)
            {
                int needed = GetTotalInputCount();
                int have = activeInputLines.Count;
                return $"输入不足 ({have}/{needed})";
            }
            foreach (var line in downstreamLines)
            {
                if (!line.IsReadyToFlow)
                {
                    if (line.Acceptor == null)
                        return $"下游 [{line.name}] 未连接节点";
                    // Check breakpoints
                    var bps = line.BreakPoints;
                    int disconnected = 0;
                    foreach (var bp in bps)
                        if (!bp.IsConnected) disconnected++;
                    if (disconnected > 0)
                        return $"下游 [{line.name}] 断路点未连接 ({disconnected}个)";
                }
            }
            return "可以交互";
        }

#if UNITY_EDITOR
        [ContextMenu("模拟交互(Interact)")]
        private void InteractContextMenu() => Interact();

        [ContextMenu("查看交互状态")]
        private void CheckStatusContextMenu()
        {
            Debug.Log($"[节点: {nodeName}] 类型={nodeType}, " +
                      $"蓄能={_isPrimed}, 输出={_isOutputting}, " +
                      $"输入={activeInputLines.Count}/{GetTotalInputCount()}, " +
                      $"下游={downstreamLines.Length}条, " +
                      $"可交互={CanInteract()}, " +
                      $"提示={GetInteractHint()}");
        }
#endif

        #endregion

        #region Visuals

        protected override void UpdateVisuals()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
                if (targetRenderer == null)
                    targetRenderer = GetComponentInChildren<MeshRenderer>();
            }
            if (targetRenderer == null) return;

            if (_isOutputting)
                targetRenderer.material.color = energizedColor;  // 红 — 已激活
            else if (_isPrimed)
                targetRenderer.material.color = primedColor;     // 黄 — 蓄能中
            else
                targetRenderer.material.color = defaultColor;    // 灰 — 待机
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (expectedInputCount < 1) expectedInputCount = 1;
            if (nodeType == NodeType.Repeater && expectedInputCount > 1)
                expectedInputCount = 1;
        }

        protected override void OnDrawGizmosSelected()
        {
            Color nodeColor;
            if (_isOutputting)
                nodeColor = Color.red;
            else if (_isPrimed)
                nodeColor = new Color(1f, 0.85f, 0f);
            else
                nodeColor = new Color(0.5f, 0.5f, 0.5f);

            Gizmos.color = nodeColor;
            Gizmos.DrawSphere(transform.position, 0.25f);

            string state = _isOutputting ? "💥已激活" : (_isPrimed ? "⚡蓄能中" : "待机");
            string typeLabel = isSource ? "🔥" : (nodeType == NodeType.Repeater ? "⏺" : "&");
            int downstreamCount = downstreamLines != null ? downstreamLines.Length : 0;
            string label = $"{typeLabel} {nodeName} [{state}]\n" +
                           $"输入={activeInputLines.Count}/{GetTotalInputCount()} " +
                           $"下游={downstreamCount}条";
            if (_isPrimed && downstreamLines != null)
                label += $"\n可交互={CanInteract()}";

            UnityEditor.Handles.color = nodeColor;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f, label);
        }
#endif

        #endregion
    }
}
