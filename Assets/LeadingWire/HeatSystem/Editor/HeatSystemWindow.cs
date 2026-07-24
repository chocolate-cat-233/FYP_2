using System.Linq;
using UnityEngine;
using UnityEditor;

namespace HeatSystem.Editor
{
    /// <summary>
    /// 热管系统编辑面板。一站式创建、连线、测试热网。
    /// 打开: 菜单栏 Window → Heat System Editor
    /// </summary>
    public class HeatSystemWindow : EditorWindow
    {
        private Vector2 scrollPos;
        private bool showSources = true, showNodes = true, showLines = true;

        [MenuItem("Window/Heat System Editor")]
        public static void ShowWindow()
        {
            var win = GetWindow<HeatSystemWindow>("🔥 Heat System");
            win.minSize = new Vector2(320, 400);
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            DrawToolbar();
            EditorGUILayout.Space(8);
            DrawQuickActions();
            EditorGUILayout.Space(8);
            DrawNetworkOverview();

            EditorGUILayout.EndScrollView();
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= Repaint;
        }

        #region Toolbar

        private void DrawToolbar()
        {
            EditorGUILayout.LabelField("创建组件", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔥 热源", GUILayout.Height(30)))
                HeatSystemMenuItems.CreateHeatSourceStatic();
            if (GUILayout.Button("🔶 节点", GUILayout.Height(30)))
                HeatSystemMenuItems.CreateHeatNodeStatic();
            if (GUILayout.Button("═ 热管", GUILayout.Height(30)))
                HeatSystemMenuItems.CreateHeatLineStatic();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("✂ 断路点", GUILayout.Height(28)))
                HeatSystemMenuItems.CreateBreakPointStatic();
            if (GUILayout.Button("📦 箱子", GUILayout.Height(28)))
                HeatSystemMenuItems.CreatePushableBoxStatic();
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
            if (GUILayout.Button("⚡ 一键创建完整 Demo 链", GUILayout.Height(34)))
                CreateCompleteDemo();
            GUI.backgroundColor = Color.white;
        }

        #endregion

        #region Quick Actions

        private void DrawQuickActions()
        {
            EditorGUILayout.LabelField("快捷操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("验证全网络"))
                ValidateAllNetworks();

            if (GUILayout.Button("修复全部断路点"))
                FixAllBreakPoints();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重置全部断路点"))
                ResetAllBreakPoints();

            GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
            if (GUILayout.Button("重建全部管线段"))
                RebuildAllSegments();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void ValidateAllNetworks()
        {
            Debug.Log("=== 🔍 热网验证 ===");
            int issues = 0;

            foreach (var line in FindAll<HeatLine>())
            {
                string lineName = line.name;
                var provider = line.Provider;
                var acceptor = line.Acceptor;

                if (provider == null)
                {
                    Debug.LogWarning($"⚠ {lineName}: 未设置上游 Provider", line);
                    issues++;
                }
                if (acceptor == null)
                {
                    Debug.LogWarning($"⚠ {lineName}: 未设置下游 Acceptor", line);
                    issues++;
                }
                // Check breakpoints
                var bps = line.BreakPoints;
                if (bps != null)
                {
                    int disconnected = bps.Count(bp => !bp.IsConnected);
                    if (disconnected > 0)
                        Debug.Log($"  {lineName}: {disconnected}/{bps.Length} 个断路点未连接", line);
                }
            }

            // Check nodes with no input
            foreach (var node in FindAll<HeatNode>())
            {
                bool hasInput = false;
                foreach (var line in FindAll<HeatLine>())
                {
                    if (line.Acceptor == node) { hasInput = true; break; }
                }
                if (!hasInput)
                {
                    Debug.LogWarning($"⚠ {node.name}: 没有任何热管连接到该节点", node);
                    issues++;
                }
            }

            if (issues == 0)
                Debug.Log("✅ 热网验证通过，无误！");
            else
                Debug.Log($"⚠ 发现 {issues} 个问题，详见上方日志。");
        }

        private void FixAllBreakPoints()
        {
            foreach (var bp in FindAll<BreakPoint>())
                bp.Connect();
            Debug.Log("✅ 全部断路点已连接");
        }

        private void ResetAllBreakPoints()
        {
            foreach (var bp in FindAll<BreakPoint>())
                bp.Disconnect();
            Debug.Log("🔄 全部断路点已断开");
        }

        private void RebuildAllSegments()
        {
            foreach (var line in FindAll<HeatLine>())
                line.GenerateSegments();
            Debug.Log("🔄 全部热管线段已重建");
        }

        #endregion

        #region Network Overview

        private void DrawNetworkOverview()
        {
            EditorGUILayout.LabelField("场景热网概览", EditorStyles.boldLabel);

            var sources = FindAll<HeatProvider>();
            var nodes = FindAll<HeatNode>();
            var lines = FindAll<HeatLine>();

            // Heat Sources
            showSources = EditorGUILayout.Foldout(showSources, $"🔥 热源 ({sources.Length})", true);
            if (showSources)
            {
                EditorGUI.indentLevel++;
                foreach (var src in sources)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button($"📍", GUILayout.Width(24)))
                    {
                        Selection.activeGameObject = src.gameObject;
                        EditorGUIUtility.PingObject(src.gameObject);
                    }
                    EditorGUILayout.LabelField(src.name, EditorStyles.boldLabel);

                    // Show downstream lines
                    var downstream = lines.Where(l => l.Provider == src).ToArray();
                    if (downstream.Length > 0)
                    {
                        foreach (var dl in downstream)
                            EditorGUILayout.LabelField($"→ [{dl.name}] → {(dl.Acceptor != null ? dl.Acceptor.name : "???")}",
                                GetLineStyle(dl));
                    }
                    else
                    {
                        EditorGUILayout.LabelField("(无下游)", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            // Heat Nodes
            showNodes = EditorGUILayout.Foldout(showNodes, $"🔶 节点 ({nodes.Length})", true);
            if (showNodes)
            {
                EditorGUI.indentLevel++;
                foreach (var node in nodes)
                {
                    // Find input lines
                    var inputs = lines.Where(l => l.Acceptor == node).ToArray();
                    var outputs = lines.Where(l => l.Provider == node).ToArray();

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button($"📍", GUILayout.Width(24)))
                    {
                        Selection.activeGameObject = node.gameObject;
                        EditorGUIUtility.PingObject(node.gameObject);
                    }
                    string typeLabel = node.NodeType == NodeType.AndGate ? "&" : "⏺";
                    EditorGUILayout.LabelField($"{typeLabel} {node.name}", EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel++;
                    if (inputs.Length > 0)
                        EditorGUILayout.LabelField($"输入: {string.Join(", ", inputs.Select(l => l.name))}", EditorStyles.miniLabel);
                    else
                        EditorGUILayout.LabelField("输入: (无 — ⚠ 孤立节点)", GetWarningStyle());

                    if (outputs.Length > 0)
                        EditorGUILayout.LabelField($"输出: {string.Join(", ", outputs.Select(l => $"[{l.name}] → {(l.Acceptor != null ? l.Acceptor.name : "???")}"))}", EditorStyles.miniLabel);
                    else
                        EditorGUILayout.LabelField("输出: (无)", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            // Heat Lines
            showLines = EditorGUILayout.Foldout(showLines, $"═ 热管 ({lines.Length})", true);
            if (showLines)
            {
                EditorGUI.indentLevel++;
                foreach (var line in lines)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button($"📍", GUILayout.Width(24)))
                    {
                        Selection.activeGameObject = line.gameObject;
                        EditorGUIUtility.PingObject(line.gameObject);
                    }

                    string status;
                    if (Application.isPlaying && line.IsFlowing) status = "🔥";
                    else if (Application.isPlaying && line.Segments.Count > 0 &&
                             line.Segments[0].CurrentState == SegmentState.Exploded) status = "🟠";
                    else status = "⬛";

                    string provName = line.Provider != null ? line.Provider.name : "❌未设";
                    string accName = line.Acceptor != null ? line.Acceptor.name : "❌未设";
                    EditorGUILayout.LabelField($"{status} {line.name}: {provName} → {accName}",
                        GetLineStyle(line));

                    // Quick wire button
                    if (GUILayout.Button("🔗", GUILayout.Width(28)))
                        AutoWireLine(line);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            if (sources.Length == 0 && nodes.Length == 0 && lines.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "场景中还没有热网组件。\n\n" +
                    "快速开始:\n" +
                    "• 点击上方 ⚡ 一键创建完整 Demo\n" +
                    "• 或手动点击创建按钮后在 Inspector 中连线",
                    MessageType.Info);
            }
        }

        private void AutoWireLine(HeatLine line)
        {
            if (line.Provider == null)
            {
                // Try find nearest provider
                var providers = FindAll<HeatProvider>()
                    .Where(p => p != line)
                    .OrderBy(p => Vector3.Distance(p.transform.position, line.transform.position))
                    .ToArray();

                if (providers.Length > 0)
                {
                    var so = new SerializedObject(line);
                    so.FindProperty("provider").objectReferenceValue = providers[0];
                    so.ApplyModifiedProperties();
                    Debug.Log($"🔗 {line.name}: 自动设置上游 → {providers[0].name}");
                }
            }

            if (line.Acceptor == null)
            {
                var acceptors = FindAll<HeatNode>()
                    .Where(n => n != line && !IsUpstreamOf(line, n))
                    .OrderBy(n => Vector3.Distance(n.transform.position, line.transform.position))
                    .ToArray();

                if (acceptors.Length > 0)
                {
                    var so = new SerializedObject(line);
                    so.FindProperty("acceptor").objectReferenceValue = acceptors[0];
                    so.ApplyModifiedProperties();
                    Debug.Log($"🔗 {line.name}: 自动设置下游 → {acceptors[0].name}");
                }
            }

            line.GenerateSegments();
            EditorUtility.SetDirty(line);
        }

        private bool IsUpstreamOf(HeatLine line, HeatNode node)
        {
            // Simple check: is node upstream of this line's provider chain?
            if (line.Provider == node) return true;
            return false;
        }

        #endregion

        #region Create Complete Demo

        private void CreateCompleteDemo()
        {
            Debug.Log("⚡ 创建完整 Demo 链路...");

            // ─── 辅助函数：创建带 Visual 的自定义物体 ───
            GameObject MakeVisualChild(GameObject parent, string name, Vector3 scale, Color? color = null)
            {
                var v = GameObject.CreatePrimitive(PrimitiveType.Cube);
                v.name = name;
                v.transform.SetParent(parent.transform);
                v.transform.localPosition = Vector3.zero;
                v.transform.localScale = scale;
                var r = v.GetComponent<MeshRenderer>();
                if (r != null && color.HasValue) r.material.color = color.Value;
                DestroyImmediate(v.GetComponent<Rigidbody>());
                DestroyImmediate(v.GetComponent<Collider>());
                return v;
            }

            void BindRenderer(Object comp, GameObject visual)
            {
                var so = new SerializedObject(comp);
                var p = so.FindProperty("targetRenderer");
                if (p != null) { p.objectReferenceValue = visual.GetComponent<MeshRenderer>(); so.ApplyModifiedProperties(); }
            }

            // ═══════════════════════════════════════════
            //  布局 (X轴):
            //  [热源] ── [BP-A] ── [BP-B] ── [中继器] ── [碎石×3] ── [终端]
            //  (0,0,0)  (1.5,0,0) (3,0,0)   (4.5,0,0)  (6,0,0)     (7.5,0,0)
            //
            //  箱子居中在 (2.25,0,-0.6)，两端沿 X 伸入两 BP:
            //     EndB(-X) → BP-A,  EndA(+X) → BP-B
            // ═══════════════════════════════════════════

            // 1. 热源
            var srcGo = HeatSystemMenuItems.CreateHeatSourceStatic();
            srcGo.name = "Demo_热源";
            srcGo.transform.position = new Vector3(0, 0, 0);

            // 2. 中继器
            var nodeGo = HeatSystemMenuItems.CreateHeatNodeStatic();
            nodeGo.name = "Demo_中继器";
            nodeGo.transform.position = new Vector3(4.5f, 0, 0);
            var nodeComp = nodeGo.GetComponent<HeatNode>();

            // 3. 热管A：热源 → 中继器 (在中间有双断路点+箱子)
            var lineAGo = HeatSystemMenuItems.CreateHeatLineStatic();
            lineAGo.name = "Demo_热管A";
            lineAGo.transform.position = new Vector3(2.25f, 0, 0);
            var lineA = lineAGo.GetComponent<HeatLine>();
            {
                var so = new SerializedObject(lineA);
                so.FindProperty("provider").objectReferenceValue = srcGo.GetComponent<HeatNode>();
                so.FindProperty("acceptor").objectReferenceValue = nodeComp;
                so.ApplyModifiedProperties();
            }

            // 4. 两个断路点 (在热管A上，相隔 1.5 单位)
            BreakPoint CreateBP(string name, float localX)
            {
                var go = new GameObject(name, typeof(BreakPoint));
                go.transform.SetParent(lineAGo.transform);
                go.transform.localPosition = new Vector3(localX - 2.25f, 0, 0);
                var bp = go.GetComponent<BreakPoint>();
                var vis = MakeVisualChild(go, "Visual", Vector3.one * 0.25f);
                BindRenderer(bp, vis);
                Undo.RegisterCreatedObjectUndo(go, "Create BP");
                return bp;
            }
            CreateBP("Demo_BP-A", 1.5f);
            CreateBP("Demo_BP-B", 3.0f);

            // 5. 箱子 (两端沿 X, 居中在 BP-A/B 之间)
            var boxGo = HeatSystemMenuItems.CreatePushableBoxStatic();
            boxGo.name = "Demo_箱子";
            boxGo.transform.position = new Vector3(2.25f, 0, -0.6f);
            // 默认触点已是 I 形(X轴)，直接保留

            // 6. 终端节点
            var termGo = HeatSystemMenuItems.CreateHeatNodeStatic();
            termGo.name = "Demo_终端";
            termGo.transform.position = new Vector3(7.5f, 0, 0);
            var termComp = termGo.GetComponent<HeatNode>();

            // 7. 热管B：中继器 → 终端 (含碎石)
            var lineBGo = HeatSystemMenuItems.CreateHeatLineStatic();
            lineBGo.name = "Demo_热管B";
            lineBGo.transform.position = new Vector3(6.0f, 0, 0);
            var lineB = lineBGo.GetComponent<HeatLine>();
            {
                var so = new SerializedObject(lineB);
                so.FindProperty("provider").objectReferenceValue = nodeComp;
                so.FindProperty("acceptor").objectReferenceValue = termComp;
                so.ApplyModifiedProperties();
            }

            // 8. 碎石 (在热管B上)
            for (int i = 0; i < 3; i++)
            {
                var dGo = new GameObject($"Demo_碎石{i}", typeof(BreakableDebris));
                dGo.transform.SetParent(lineBGo.transform);
                dGo.transform.localPosition = new Vector3((i - 1) * 0.4f, 0.2f, 0);
                dGo.transform.localScale = Vector3.one * 0.3f;
                MakeVisualChild(dGo, "Visual", Vector3.one, new Color(0.45f, 0.35f, 0.25f));
                Undo.RegisterCreatedObjectUndo(dGo, "Create Debris");
            }

            // 9. 生成管线段
            lineA.GenerateSegments();
            lineB.GenerateSegments();

            // 10. 选中箱子
            Selection.activeGameObject = boxGo;
            EditorGUIUtility.PingObject(boxGo);

            Debug.Log("✅ Demo 链路创建完成！\n" +
                      "   热源(0) → [BP-A|BP-B] → 中继器(4.5) → [碎石] → 终端(7.5)\n" +
                      "   箱子(2.25,0,-0.6)，两端沿X伸入BP-A和BP-B");
            Debug.Log("💡 测试步骤:\n" +
                      "   1. Play → 全部灰色\n" +
                      "   2. 右键箱子 → 向北推 1 单位 → 箱子滑入两BP之间\n" +
                      "      BP-A🟢 BP-B🟢 → 热管A 级联点亮\n" +
                      "   3. 选中热源 → 点「💥交互激活」→ 热源变红\n" +
                      "   4. 选中中继器 → 蓄能(黄) →「💥交互激活」→ 热管B 级联+碎石炸飞\n" +
                      "   5. 右键箱子 → 向南推 1 单位 → 全线掉电");
        }

        #endregion

        #region Helpers

        private static T[] FindAll<T>() where T : UnityEngine.Object
        {
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
        }

        private static GUIStyle GetLineStyle(HeatLine line)
        {
            var style = new GUIStyle(EditorStyles.label);
            if (Application.isPlaying)
            {
                if (line.IsFlowing)
                    style.normal.textColor = Color.red;
                else if (line.Segments.Count > 0 &&
                         line.Segments[0].CurrentState == SegmentState.Exploded)
                    style.normal.textColor = new Color(1f, 0.5f, 0f);
                else
                    style.normal.textColor = Color.gray;
            }
            else
            {
                bool wired = line.Provider != null && line.Acceptor != null;
                style.normal.textColor = wired ? Color.white : Color.yellow;
            }
            return style;
        }

        private static GUIStyle GetWarningStyle()
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = Color.yellow;
            return style;
        }

        #endregion
    }
}
