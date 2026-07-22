using UnityEngine;
using UnityEditor;

namespace HeatSystem.Editor
{
    /// <summary>
    /// HeatNode 自定义 Inspector。
    /// 运行中显示实时状态和交互按钮，方便调试。
    /// </summary>
    [CustomEditor(typeof(HeatNode))]
    public class HeatNodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var node = (HeatNode)target;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("━━━ 运行状态 ━━━", EditorStyles.boldLabel);

            // 状态指示器
            string stateText;
            Color stateColor;
            if (node.IsOutputting)
            {
                stateText = "💥 已激活（爆炸中）";
                stateColor = Color.red;
            }
            else if (node.IsPrimed)
            {
                stateText = "⚡ 蓄能中 — 等待交互";
                stateColor = new Color(1f, 0.85f, 0f);
            }
            else
            {
                stateText = "⬛ 待机";
                stateColor = Color.gray;
            }

            var bgColor = GUI.backgroundColor;
            GUI.backgroundColor = stateColor;
            EditorGUILayout.BeginHorizontal("box");
            GUI.backgroundColor = bgColor;
            EditorGUILayout.LabelField(stateText, new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = stateColor },
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 输入状态
            EditorGUILayout.LabelField($"📥 输入: {node.ActiveInputCount}", EditorStyles.label);

            // 下游线路
            var downstream = node.DownstreamLines;
            if (downstream != null)
            {
                EditorGUILayout.LabelField($"📤 下游线路: {downstream.Count} 条");
                EditorGUI.indentLevel++;
                foreach (var line in downstream)
                {
                    bool ready = line.IsReadyToFlow;
                    string mark = ready ? "✅" : "❌";
                    EditorGUILayout.LabelField($"{mark} {line.name}", EditorStyles.label);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);

            // 交互原因提示
            string hint = node.GetInteractHint();

            // 交互按钮
            bool canInteract = node.CanInteract();
            GUI.enabled = canInteract;
            GUI.backgroundColor = canInteract ? new Color(1f, 0.4f, 0f) : Color.gray;
            if (GUILayout.Button(canInteract ? "💥 交互激活！" : $"🔒 {hint}", GUILayout.Height(40)))
            {
                node.Interact();
                EditorUtility.SetDirty(node);
            }
            GUI.backgroundColor = bgColor;
            GUI.enabled = true;

            // 强制刷新
            Repaint();
        }
    }
}
