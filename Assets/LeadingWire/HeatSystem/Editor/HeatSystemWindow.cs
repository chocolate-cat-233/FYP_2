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

            var sources = FindAll<HeatSource>();
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
                    .Where(p => p != line && !(p is HeatNode))
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

            // 1. Create Heat Source
            var source = HeatSystemMenuItems.CreateHeatSourceStatic();
            source.name = "Demo_热源";
            source.transform.position = new Vector3(0, 0, 0);

            // 2. Create Repeater Node
            var node = HeatSystemMenuItems.CreateHeatNodeStatic();
            node.name = "Demo_中继器";
            node.transform.position = new Vector3(3, 0, 0);

            // 3. Create HeatLine from source to node
            var lineGo = HeatSystemMenuItems.CreateHeatLineStatic();
            lineGo.name = "Demo_热管";
            lineGo.transform.position = new Vector3(1.5f, 0, 0);
            var lineComp = lineGo.GetComponent<HeatLine>();

            // Wire
            var so = new SerializedObject(lineComp);
            so.FindProperty("provider").objectReferenceValue = source.GetComponent<HeatSource>();
            so.FindProperty("acceptor").objectReferenceValue = node.GetComponent<HeatNode>();
            so.ApplyModifiedProperties();

            // 4. Create BreakPoint as child of HeatLine
            var bpGo = new GameObject("Demo_断路点", typeof(BreakPoint));
            bpGo.transform.SetParent(lineGo.transform);
            bpGo.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(bpGo, "Create BreakPoint");

            // Add visual cube to breakpoint
            var bpVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bpVisual.name = "Visual";
            bpVisual.transform.SetParent(bpGo.transform);
            bpVisual.transform.localPosition = Vector3.zero;
            bpVisual.transform.localScale = Vector3.one * 0.3f;
            DestroyImmediate(bpVisual.GetComponent<Rigidbody>());
            DestroyImmediate(bpVisual.GetComponent<Collider>());

            // 5. Create output node (terminal)
            var node2Go = HeatSystemMenuItems.CreateHeatNodeStatic();
            node2Go.name = "Demo_输出节点";
            node2Go.transform.position = new Vector3(6, 0, 0);
            var node2Comp = node2Go.GetComponent<HeatNode>();

            // 6. Create output line
            var line2Go = HeatSystemMenuItems.CreateHeatLineStatic();
            line2Go.name = "Demo_输出热管";
            line2Go.transform.position = new Vector3(4.5f, 0, 0);
            var line2Comp = line2Go.GetComponent<HeatLine>();

            var so2 = new SerializedObject(line2Comp);
            so2.FindProperty("provider").objectReferenceValue = node.GetComponent<HeatNode>();
            so2.FindProperty("acceptor").objectReferenceValue = node2Comp;
            so2.ApplyModifiedProperties();

            // Generate segments for both lines
            lineComp.GenerateSegments();
            line2Comp.GenerateSegments();

            // Select the breakpoint for easy testing
            Selection.activeGameObject = bpGo;
            EditorGUIUtility.PingObject(bpGo);

            Debug.Log("✅ Demo 链路创建完成！\n" +
                      $"   热源 → [{lineGo.name}] → 中继器 → [{line2Go.name}] → 输出节点\n" +
                      $"   选中了断路点 [{bpGo.name}]，运行后在 Inspector 右键切换修复状态。");
            Debug.Log("💡 使用步骤:\n" +
                      "   1. 点击 Play 运行\n" +
                      "   2. 右键断路点 → Toggle Fixed\n" +
                      "   3. 观察 Console 和管线段颜色变化");
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
