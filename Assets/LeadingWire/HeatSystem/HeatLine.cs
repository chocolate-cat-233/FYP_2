using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace HeatSystem
{
    /// <summary>
    /// 热管。连接一个热量提供者(HeatProvider)到一个热量接收节点(HeatNode)。
    ///
    /// 逻辑流程：
    /// 上游 Provider 输出 ON → 检查所有断路点已修复 →
    /// 热量流入热管 → 爆炸！→ 通知下游 HeatNode 接收热量
    ///
    /// 断路点(BreakPoint)作为热管的子物体放置。
    /// </summary>
    public class HeatLine : MonoBehaviour
    {
        [Header("连接设置")]
        [SerializeField, Tooltip("上游热量提供者（热源或节点）")]
        private HeatProvider provider;

        [SerializeField, Tooltip("下游热量接收节点")]
        private HeatNode acceptor;

        [Header("事件")]
        [SerializeField, Tooltip("热量开始流动时触发（爆炸效果等）")]
        private UnityEvent onHeatStartFlowing;

        [SerializeField, Tooltip("热量停止流动时触发")]
        private UnityEvent onHeatStopFlowing;

        [Header("路径拐点")]
        [SerializeField, Tooltip("管线拐弯路径点（按顺序排列）")]
        private Transform[] waypoints;

        [Header("视觉反馈")]
        [SerializeField, Tooltip("用于显示状态的 MeshRenderer")]
        private MeshRenderer targetRenderer;

        [SerializeField, Tooltip("有能量流动时的颜色（红色）")]
        private Color energizedColor = new Color(1f, 0.15f, 0f);

        [SerializeField, Tooltip("爆炸后的颜色（橙色）")]
        private Color explodedColor = new Color(1f, 0.5f, 0f);

        [SerializeField, Tooltip("已连通等待引爆的颜色（蓝色）")]
        private Color readyColor = new Color(0.2f, 0.5f, 1f);

        [SerializeField, Tooltip("无热量未连通（灰色）")]
        private Color defaultColor = new Color(0.3f, 0.3f, 0.3f);

        [Header("管线段（逐节点亮）")]
        [SerializeField, Tooltip("线段数量（0 = 使用手动子物体）")]
        [Range(0, 50)]
        private int segmentCount = 8;

        [SerializeField, Tooltip("线段预制体（可选，留空则自动创建 Cube）")]
        private GameObject segmentPrefab;

        [SerializeField, Tooltip("每段点亮间隔（秒）")]
        [Range(0.01f, 0.5f)]
        private float cascadeDelay = 0.06f;

        [SerializeField, Tooltip("级联动画中每段橙色闪持续（秒）")]
        [Range(0.01f, 0.3f)]
        private float energizingDuration = 0.04f;

        [Header("调试")]
        [SerializeField, Tooltip("在 Inspector 中查看当前热流状态")]
        private bool debugIsFlowing = false;

        [SerializeField, Tooltip("在 Inspector 中查看是否已爆炸过")]
        private bool debugHasExploded = false;

        // 该热管下的所有断路点
        private BreakPoint[] breakPoints;

        // 该热管上的碎石障碍物
        private BreakableDebris[] debrisList;

        // 当前是否正在输送热量
        private bool isFlowing = false;

        // 是否曾经被引爆过（持久状态，用于爆炸后保持橙色）
        private bool hasExploded = false;

        // 管线分段
        private List<HeatLineSegment> segments = new List<HeatLineSegment>();
        private Coroutine cascadeCoroutine;

        /// <summary>当前是否正在输送热量</summary>
        public bool IsFlowing => isFlowing;

        /// <summary>下游线路是否就绪（断路点全连接 + 有下游节点），供泵节点检查</summary>
        public bool IsReadyToFlow => AllBreakPointsConnected() && acceptor != null;

        /// <summary>上游提供者</summary>
        public HeatProvider Provider => provider;

        /// <summary>下游接收节点</summary>
        public HeatNode Acceptor => acceptor;

        /// <summary>所有管线段（只读）</summary>
        public IReadOnlyList<HeatLineSegment> Segments => segments;

        #region Unity Lifecycle

        private void Awake()
        {
            // 扫描子物体中的断路点（包含未激活的物体）
            breakPoints = GetComponentsInChildren<BreakPoint>(true);

            // 扫描子物体中的碎石障碍物
            debrisList = GetComponentsInChildren<BreakableDebris>(true);
            // 自动关联碎石到最近的管线段
            AutoLinkDebris();

            // 初始化管线段：先尝试使用手动放置的子物体，否则自动生成
            InitializeSegments();
        }

        private void OnEnable()
        {
            // 订阅 Provider 的输出变化
            if (provider != null)
                provider.OnOutputChanged += OnProviderChanged;

            // 订阅所有断路点的连接变化事件
            foreach (var bp in breakPoints)
                bp.OnConnectionChanged += OnBreakPointChanged;
        }

        private void OnDisable()
        {
            if (provider != null)
                provider.OnOutputChanged -= OnProviderChanged;

            foreach (var bp in breakPoints)
                bp.OnConnectionChanged -= OnBreakPointChanged;

            if (cascadeCoroutine != null)
            {
                StopCoroutine(cascadeCoroutine);
                cascadeCoroutine = null;
            }
        }

        private void Start()
        {
            // 场景加载后首次评估（确保所有组件的 OnEnable 已完成）
            Evaluate();
            // 初始化所有线段为关闭状态
            SetAllSegmentsState(SegmentState.Off);
            // 根据初始状态更新颜色（例如预连接断点 → 蓝）
            UpdateVisuals();
        }

        #endregion

        #region Evaluation

        /// <summary>
        /// Provider 输出变化回调
        /// </summary>
        private void OnProviderChanged()
        {
            Evaluate();
            UpdateVisuals();
        }

        /// <summary>
        /// 断路点连接状态变化回调（线可能从灰→蓝，即使没流动）
        /// </summary>
        private void OnBreakPointChanged()
        {
            Evaluate();
            UpdateVisuals();
        }

        /// <summary>
        /// 评估热管是否应该输送热量。
        /// 条件：Provider 正在输出 + 所有断路点已修复。
        /// </summary>
        [ContextMenu("手动评估")]
        public void Evaluate()
        {
            bool providerOutputting = provider != null && provider.IsOutputting;
            bool allBreakPointsConnected = AllBreakPointsConnected();
            bool hasAcceptor = acceptor != null;

            // 三个条件：上游有输出 + 所有断路点已连接 + 下游有接收节点
            bool shouldFlow = providerOutputting && allBreakPointsConnected && hasAcceptor;

            Debug.Log($"[热管: {name}] 评估: " +
                      $"Provider={(provider != null ? provider.name : "无")}({(providerOutputting ? "ON" : "OFF")}), " +
                      $"断路点={GetConnectedBreakPointCount()}/{breakPoints.Length}, " +
                      $"下游节点={(hasAcceptor ? acceptor.name : "❌未设置")}, " +
                      $"结果={(shouldFlow ? "🔥流动" : "⬛阻断")}");

            if (shouldFlow != isFlowing)
            {
                isFlowing = shouldFlow;
                debugIsFlowing = isFlowing;

                if (isFlowing)
                {
                    OnHeatStartFlowing();
                }
                else
                {
                    OnHeatStopFlowing();
                }

                UpdateVisuals();

                // 通知下游节点：接通时由级联动画完成后通知，断开时立即通知
                if (acceptor != null && !isFlowing)
                {
                    acceptor.ReceiveHeat(this, false);
                }
                else if (acceptor == null)
                {
                    Debug.LogWarning($"[热管: {name}] 未设置下游节点(acceptor)！");
                }
            }
        }

        #endregion

        #region Break Points

        private bool AllBreakPointsConnected()
        {
            if (breakPoints == null) return false;
            foreach (var bp in breakPoints)
            {
                if (bp == null) continue;
                if (!bp.IsConnected) return false;
            }
            return true;
        }

        private int GetConnectedBreakPointCount()
        {
            if (breakPoints == null) return 0;
            int count = 0;
            foreach (var bp in breakPoints)
                if (bp != null && bp.IsConnected) count++;
            return count;
        }

        /// <summary>
        /// 获取此热管上的所有断路点（只读）
        /// </summary>
        public BreakPoint[] BreakPoints => breakPoints;

        #endregion

        #region Heat Flow Events

        private void OnHeatStartFlowing()
        {
            hasExploded = true;
            debugHasExploded = true;
            Debug.Log($"🔥🔥🔥 [热管: {name}] 热量引爆！线路连通！");
            onHeatStartFlowing?.Invoke();

            // 启动逐段点亮动画
            if (cascadeCoroutine != null) StopCoroutine(cascadeCoroutine);
            cascadeCoroutine = StartCoroutine(CascadeLightUp());
        }

        private void OnHeatStopFlowing()
        {
            Debug.Log($"⬛ [热管: {name}] 热量断开。");
            onHeatStopFlowing?.Invoke();

            // 停止级联动画
            if (cascadeCoroutine != null)
            {
                StopCoroutine(cascadeCoroutine);
                cascadeCoroutine = null;
            }

            // 全部设为爆炸后状态（橙色）
            SetAllSegmentsState(SegmentState.Exploded);
        }

        /// <summary>
        /// 更新材质颜色：
        /// 红色 = 有能量正在流动
        /// 橙色 = 曾经爆炸过，无能量流
        /// 蓝色 = 已连通，等待引爆（未爆过）
        /// 灰色 = 未连通
        /// </summary>
        private void UpdateVisuals()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
                if (targetRenderer == null)
                    targetRenderer = GetComponentInChildren<MeshRenderer>();
            }
            if (targetRenderer == null) return;

            if (isFlowing)
                targetRenderer.material.color = energizedColor;  // 红 — 有能量
            else if (hasExploded)
                targetRenderer.material.color = explodedColor;   // 橙 — 爆炸后
            else if (IsReadyToFlow)
                targetRenderer.material.color = readyColor;      // 蓝 — 连通，待引爆
            else
                targetRenderer.material.color = defaultColor;    // 灰 — 未连通
        }

        #endregion

        #region Segments (管线段)

        /// <summary>
        /// 初始化管线段：优先使用子物体中已有的 HeatLineSegment，
        /// 否则根据 segmentCount 自动生成。
        /// </summary>
        private void InitializeSegments()
        {
            // 先查找已手动放置的子物体线段
            segments.Clear();
            GetComponentsInChildren(true, segments);

            if (segments.Count > 0)
            {
                // 使用手动放置的线段，按与 Provider 的距离排序
                segments.Sort((a, b) =>
                {
                    float distA = provider != null ? Vector3.Distance(provider.transform.position, a.transform.position) : 0;
                    float distB = provider != null ? Vector3.Distance(provider.transform.position, b.transform.position) : 0;
                    return distA.CompareTo(distB);
                });

                Debug.Log($"[热管: {name}] 发现 {segments.Count} 个手动放置的管线段");
            }
            else if (segmentCount > 0)
            {
                // 自动生成管线段
                GenerateSegments();
                Debug.Log($"[热管: {name}] 自动生成 {segments.Count} 个管线段");
            }
        }

        /// <summary>
        /// 自动生成管线段。沿 Provider → HeatLine(本物体) → Acceptor 路径均匀分布。
        /// 编辑器下也可调用，会立即在 Scene 视图中看到结果。
        /// </summary>
        public void GenerateSegments()
        {
            // 清理旧的自动生成线段
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.GetComponent<HeatLineSegment>() != null)
                {
                    // 仅清理之前自动生成的（不含断路点）
                    if (child.GetComponent<BreakPoint>() == null)
                    {
                        if (Application.isPlaying)
                            Destroy(child.gameObject);
                        else
                            DestroyImmediate(child.gameObject);
                    }
                }
            }

            segments.Clear();

            for (int i = 0; i < segmentCount; i++)
            {
                GameObject segGo;
                if (segmentPrefab != null)
                {
                    segGo = Instantiate(segmentPrefab, transform);
                }
                else
                {
                    segGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    segGo.transform.SetParent(transform);
                    // 移除 Cube 自带的碰撞体，保留 MeshRenderer
                    DestroyImmediate(segGo.GetComponent<Collider>());
                }

                segGo.name = $"Segment_{i:D2}";
                segGo.transform.localScale = Vector3.one * 0.25f;

                var segment = segGo.GetComponent<HeatLineSegment>();
                if (segment == null)
                    segment = segGo.AddComponent<HeatLineSegment>();

                segments.Add(segment);
            }

            // 计算并应用路径位置
            PositionSegments();
        }

        /// <summary>
        /// 构建完整路径点列表: Provider → waypoints → Acceptor
        /// </summary>
        private List<Vector3> BuildPathPoints()
        {
            var pts = new List<Vector3>();
            if (provider != null) pts.Add(provider.transform.position);
            if (waypoints != null)
                foreach (var wp in waypoints)
                    if (wp != null) pts.Add(wp.position);
            if (acceptor != null) pts.Add(acceptor.transform.position);

            // 至少需要两个点
            if (pts.Count < 2)
            {
                if (pts.Count == 0) pts.Add(transform.position - Vector3.forward);
                pts.Add(transform.position + Vector3.forward);
            }
            return pts;
        }

        /// <summary>
        /// 沿路径放置各线段的位置和朝向。
        /// </summary>
        private void PositionSegments()
        {
            if (segments.Count == 0) return;

            var path = BuildPathPoints();

            for (int i = 0; i < segments.Count; i++)
            {
                float t = segments.Count == 1 ? 0.5f : (float)i / (segments.Count - 1);
                Vector3 pos = GetPointOnPath(path, t);
                segments[i].transform.position = pos;

                float lookT = Mathf.Min(t + 0.01f, 1f);
                Vector3 lookTarget = GetPointOnPath(path, lookT);
                Vector3 dir = (lookTarget - pos).normalized;
                if (dir.magnitude > 0.001f)
                    segments[i].transform.forward = dir;
            }
        }

        /// <summary>
        /// 在多段折线上根据 t(0~1) 获取点。
        /// </summary>
        private Vector3 GetPointOnPath(List<Vector3> pts, float t)
        {
            if (pts.Count < 2) return pts.Count > 0 ? pts[0] : Vector3.zero;

            // 计算总长度和各段长度
            float totalLen = 0f;
            var segLens = new float[pts.Count - 1];
            for (int i = 0; i < pts.Count - 1; i++)
            {
                segLens[i] = Vector3.Distance(pts[i], pts[i + 1]);
                totalLen += segLens[i];
            }
            if (totalLen < 0.001f) return pts[0];

            float target = t * totalLen;
            float accum = 0f;
            for (int i = 0; i < segLens.Length; i++)
            {
                if (target <= accum + segLens[i])
                {
                    float segT = segLens[i] > 0.001f ? (target - accum) / segLens[i] : 0f;
                    return Vector3.Lerp(pts[i], pts[i + 1], segT);
                }
                accum += segLens[i];
            }
            return pts[pts.Count - 1];
        }

        /// <summary>
        /// 级联点亮动画：从 Provider 端到 Acceptor 端逐个点亮线段。
        /// 每段先闪烁橙色（爆炸传导），再稳定为红色（能量）。
        /// </summary>
        private IEnumerator CascadeLightUp()
        {
            if (segments.Count == 0) yield break;

            Debug.Log($"[热管: {name}] 🎆 开始级联点亮 {segments.Count} 段...");

            for (int i = 0; i < segments.Count; i++)
            {
                // 1. 橙色 — 爆炸传导到该段
                segments[i].SetEnergizing();
                yield return new WaitForSeconds(energizingDuration);

                // 2. 炸碎该段的碎石障碍物
                ExplodeDebrisAtSegment(i);

                // 3. 红色 — 稳定能量
                segments[i].SetEnergized();

                // 4. 等待下一段
                if (i < segments.Count - 1)
                    yield return new WaitForSeconds(cascadeDelay);
            }

            Debug.Log($"[热管: {name}] ✅ 级联点亮完成");

            // 级联完成后通知下游节点
            if (acceptor != null)
            {
                acceptor.ReceiveHeat(this, true);
            }
            else
            {
                Debug.LogWarning($"[热管: {name}] 未设置下游节点(acceptor)！");
            }

            cascadeCoroutine = null;
        }

        /// <summary>
        /// 设置所有线段为相同状态。
        /// </summary>
        private void SetAllSegmentsState(SegmentState state)
        {
            foreach (var seg in segments)
            {
                switch (state)
                {
                    case SegmentState.Off:         seg.SetOff(); break;
                    case SegmentState.Energizing:  seg.SetEnergizing(); break;
                    case SegmentState.Energized:   seg.SetEnergized(); break;
                    case SegmentState.Exploded:    seg.SetExploded(); break;
                }
            }
        }

        #endregion

        #region Debris (碎石)

        /// <summary>
        /// 自动将碎石关联到最近的管线段。
        /// </summary>
        private void AutoLinkDebris()
        {
            if (debrisList == null || debrisList.Length == 0) return;

            foreach (var debris in debrisList)
            {
                if (debris == null) continue;
                if (debris.LinkedSegmentIndex >= 0) continue; // 已手动指定

                // 找最近的管线段
                float bestDist = float.MaxValue;
                int bestIdx = 0;
                for (int i = 0; i < segments.Count; i++)
                {
                    float dist = Vector3.Distance(debris.transform.position, segments[i].transform.position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = i;
                    }
                }
                // 使用 reflection 或 SerializedObject 设置私有字段
                // 简单处理：在 debris 添加到子物体时已通过 Inspector 或代码设定
                // 这里只做日志
                if (bestDist < debris.TriggerRadius && segments.Count > 0)
                {
                    Debug.Log($"[热管: {name}] 碎石 [{debris.name}] 最近段=Seg[{bestIdx}] 距离={bestDist:F2}");
                }
            }
        }

        /// <summary>
        /// 引爆指定管线段附近的所有碎石。
        /// </summary>
        private void ExplodeDebrisAtSegment(int segmentIndex)
        {
            if (debrisList == null || debrisList.Length == 0) return;
            if (segmentIndex < 0 || segmentIndex >= segments.Count) return;

            Vector3 segPos = segments[segmentIndex].transform.position;

            foreach (var debris in debrisList)
            {
                if (debris == null || debris.HasExploded) continue;

                // 判断是否应该引爆
                bool shouldExplode = false;

                if (debris.LinkedSegmentIndex == segmentIndex)
                {
                    shouldExplode = true;
                }
                else if (debris.LinkedSegmentIndex < 0)
                {
                    // 自动模式：距离判断
                    float dist = Vector3.Distance(debris.transform.position, segPos);
                    if (dist < debris.TriggerRadius)
                        shouldExplode = true;
                }

                if (shouldExplode)
                    debris.Explode();
            }
        }

        /// <summary>
        /// 在编辑器中重建管线段（用于手动触发重新放置）。
        /// </summary>
        [ContextMenu("测试引爆全部碎石")]
        private void TestExplodeAllDebris()
        {
            if (debrisList == null) return;
            foreach (var d in debrisList)
                if (d != null && !d.HasExploded)
                    d.Explode();
        }

        [ContextMenu("➕ 添加拐点子物体")]
        private void AddWaypointChild()
        {
            int idx = waypoints != null ? waypoints.Length : 0;
            var go = new GameObject($"Waypoint_{idx}");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");
            // 添加到数组
            var so = new UnityEditor.SerializedObject(this);
            var wpProp = so.FindProperty("waypoints");
            wpProp.InsertArrayElementAtIndex(wpProp.arraySize);
            var elem = wpProp.GetArrayElementAtIndex(wpProp.arraySize - 1);
            elem.objectReferenceValue = go.transform;
            so.ApplyModifiedProperties();
            UnityEditor.Selection.activeGameObject = go;
#endif
            GenerateSegments();
        }

        [ContextMenu("重建管线段")]
        private void RebuildSegments()
        {
            GenerateSegments();
        }

        #endregion

        #region Editor

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 延迟执行以避免编辑器批量操作时的报错
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && Application.isPlaying)
                    Evaluate();
            };
        }

        private void OnDrawGizmosSelected()
        {
            var path = BuildPathPoints();
            if (path.Count < 2) return;

            // 根据状态选色
            Color lineColor;
            if (isFlowing) lineColor = Color.red;
            else if (hasExploded) lineColor = new Color(1f, 0.5f, 0f);
            else if (IsReadyToFlow) lineColor = new Color(0.2f, 0.5f, 1f);
            else lineColor = Color.gray;

            Gizmos.color = lineColor;
            for (int i = 0; i < path.Count - 1; i++)
                Gizmos.DrawLine(path[i], path[i + 1]);

            // 拐点小球
            for (int i = 0; i < path.Count; i++)
            {
                Gizmos.DrawSphere(path[i], 0.1f);
            }

            // 断路点标记
            if (breakPoints != null)
            {
                foreach (var bp in breakPoints)
                {
                    if (bp == null) continue;
                    Gizmos.color = bp.IsConnected ? Color.green : Color.red;
                    Gizmos.DrawWireCube(bp.transform.position, Vector3.one * 0.3f);
                }
            }

            // 标签在路径中点
            var mid = GetPointOnPath(path, 0.5f);
            UnityEditor.Handles.color = lineColor;
            string status = isFlowing ? "🔥 FLOWING" : (hasExploded ? "🟠 EXPLODED" : (IsReadyToFlow ? "🔵 READY" : "⬛ BLOCKED"));
            UnityEditor.Handles.Label(mid + Vector3.up * 0.3f, $"{name}\n{status}");
        }
#endif

        #endregion
    }
}
