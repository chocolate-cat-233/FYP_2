using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace HeatSystem.Editor
{
    [CustomEditor(typeof(HeatLine))]
    public class HeatLineEditor : UnityEditor.Editor
    {
        private SerializedProperty providerProp;
        private SerializedProperty acceptorProp;
        private SerializedProperty waypointsProp;
        private SerializedProperty segmentCountProp;
        private SerializedProperty cascadeDelayProp;

        private void OnEnable()
        {
            providerProp = serializedObject.FindProperty("provider");
            acceptorProp = serializedObject.FindProperty("acceptor");
            waypointsProp = serializedObject.FindProperty("waypoints");
            segmentCountProp = serializedObject.FindProperty("segmentCount");
            cascadeDelayProp = serializedObject.FindProperty("cascadeDelay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var line = (HeatLine)target;

            // ── 连接 ──
            EditorGUILayout.LabelField("连接", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(providerProp, new GUIContent("上游 Provider"));
            EditorGUILayout.PropertyField(acceptorProp, new GUIContent("下游 Acceptor"));

            // 快速连线按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔗 自动连线", GUILayout.Height(24)))
                AutoWire(line);
            if (GUILayout.Button("🔄 重建管线段", GUILayout.Height(24)))
                line.GenerateSegments();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // ── 拐点 ──
            EditorGUILayout.LabelField($"路径拐点 ({waypointsProp.arraySize})", EditorStyles.boldLabel);

            for (int i = 0; i < waypointsProp.arraySize; i++)
            {
                var elem = waypointsProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(elem, GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    // 删除拐点物体
                    var t = elem.objectReferenceValue as Transform;
                    if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
                    waypointsProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    line.GenerateSegments();
                    return;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("➕ 添加拐点"))
            {
                var go = new GameObject($"WP_{waypointsProp.arraySize}");
                go.transform.SetParent(line.transform);
                // 放在路径中间
                var path = BuildPathPoints(line);
                if (path.Count >= 2)
                    go.transform.position = GetPointOnPath(path, 0.5f);
                else
                    go.transform.localPosition = Vector3.zero;
                Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");

                waypointsProp.InsertArrayElementAtIndex(waypointsProp.arraySize);
                waypointsProp.GetArrayElementAtIndex(waypointsProp.arraySize - 1).objectReferenceValue = go.transform;
                serializedObject.ApplyModifiedProperties();
                line.GenerateSegments();
                Selection.activeGameObject = go;
            }
            if (waypointsProp.arraySize > 0 && GUILayout.Button("清空全部"))
            {
                for (int i = waypointsProp.arraySize - 1; i >= 0; i--)
                {
                    var t = waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
                    waypointsProp.DeleteArrayElementAtIndex(i);
                }
                serializedObject.ApplyModifiedProperties();
                line.GenerateSegments();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // ── 管线段 ──
            EditorGUILayout.LabelField("管线段", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(segmentCountProp, new GUIContent("线段数量"));
            EditorGUILayout.PropertyField(cascadeDelayProp, new GUIContent("级联延迟(s)"));

            EditorGUILayout.Space(5);
            DrawDefaultInspectorExcept("provider", "acceptor", "waypoints", "segmentCount",
                "cascadeDelay", "segmentPrefab", "energizingDuration", "energizedColor",
                "explodedColor", "readyColor", "defaultColor", "targetRenderer",
                "debugIsFlowing", "debugHasExploded", "onHeatStartFlowing", "onHeatStopFlowing");

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var line = (HeatLine)target;
            var path = BuildPathPoints(line);
            if (path.Count < 2) return;

            // 绘制路径线
            Handles.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            for (int i = 0; i < path.Count - 1; i++)
                Handles.DrawLine(path[i], path[i + 1], 2f);

            // 可拖拽的拐点（waypoints 对应的点）
            var wps = line.GetComponentsInChildren<Transform>();
            // 获取 waypoints 数组中的 Transform
            int wpStart = 0; // skip provider
            if (line.Provider != null) wpStart++;

            // 遍历路径点：provider → waypoints → acceptor
            // waypoints 对应 path[1..path.Count-2]（如果两端是 provider 和 acceptor）
            int wpBaseIdx = line.Provider != null ? 1 : 0;
            int wpEndIdx = line.Acceptor != null ? path.Count - 2 : path.Count - 1;

            for (int i = wpBaseIdx; i <= wpEndIdx && i < path.Count; i++)
            {
                EditorGUI.BeginChangeCheck();
                float handleSize = HandleUtility.GetHandleSize(path[i]) * 0.1f;
                Vector3 newPos = Handles.FreeMoveHandle(path[i], handleSize, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(line.transform, "Move Waypoint");

                    // 找到对应的 waypoint Transform 并移动
                    if (i - wpBaseIdx < waypointsProp.arraySize)
                    {
                        var wp = waypointsProp.GetArrayElementAtIndex(i - wpBaseIdx).objectReferenceValue as Transform;
                        if (wp != null)
                        {
                            Undo.RecordObject(wp, "Move Waypoint");
                            wp.position = newPos;
                        }
                    }
                    line.GenerateSegments();
                }
            }

            // 路径中点：点击添加拐点
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 mid = (path[i] + path[i + 1]) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid) * 0.06f;
                Handles.color = new Color(1f, 1f, 1f, 0.3f);
                if (Handles.Button(mid, Quaternion.identity, size, size, Handles.DotHandleCap))
                {
                    var go = new GameObject($"WP_{waypointsProp.arraySize}");
                    go.transform.SetParent(line.transform);
                    go.transform.position = mid;
                    Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");

                    waypointsProp.InsertArrayElementAtIndex(waypointsProp.arraySize);
                    waypointsProp.GetArrayElementAtIndex(waypointsProp.arraySize - 1).objectReferenceValue = go.transform;
                    serializedObject.ApplyModifiedProperties();
                    line.GenerateSegments();
                    Selection.activeGameObject = go;
                    Repaint();
                }
            }
        }

        // ── helpers ──

        private void AutoWire(HeatLine line)
        {
            var providers = Object.FindObjectsByType<HeatProvider>(FindObjectsSortMode.None);
            var acceptors = Object.FindObjectsByType<HeatNode>(FindObjectsSortMode.None);

            if (line.Provider == null)
            {
                HeatProvider best = null;
                float bestDist = float.MaxValue;
                foreach (var p in providers)
                {
                    if (p == line) continue;
                    float d = Vector3.Distance(p.transform.position, line.transform.position);
                    if (d < bestDist) { bestDist = d; best = p; }
                }
                if (best != null)
                {
                    providerProp.objectReferenceValue = best;
                    serializedObject.ApplyModifiedProperties();
                }
            }

            if (line.Acceptor == null)
            {
                HeatNode best = null;
                float bestDist = float.MaxValue;
                foreach (var a in acceptors)
                {
                    if (a == line) continue;
                    float d = Vector3.Distance(a.transform.position, line.transform.position);
                    if (d < bestDist) { bestDist = d; best = a; }
                }
                if (best != null)
                {
                    acceptorProp.objectReferenceValue = best;
                    serializedObject.ApplyModifiedProperties();
                }
            }

            serializedObject.ApplyModifiedProperties();
            line.GenerateSegments();
        }

        private List<Vector3> BuildPathPoints(HeatLine line)
        {
            var pts = new List<Vector3>();
            if (line.Provider != null) pts.Add(line.Provider.transform.position);
            // 通过 SerializedObject 读取 waypoints
            for (int i = 0; i < waypointsProp.arraySize; i++)
            {
                var t = waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (t != null) pts.Add(t.position);
            }
            if (line.Acceptor != null) pts.Add(line.Acceptor.transform.position);
            if (pts.Count < 2)
            {
                if (pts.Count == 0) pts.Add(line.transform.position + Vector3.left);
                pts.Add(line.transform.position + Vector3.right);
            }
            return pts;
        }

        private Vector3 GetPointOnPath(List<Vector3> pts, float t)
        {
            if (pts.Count < 2) return pts.Count > 0 ? pts[0] : Vector3.zero;
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

        private void DrawDefaultInspectorExcept(params string[] exclude)
        {
            var ex = new HashSet<string>(exclude);
            var prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (!ex.Contains(prop.name))
                        EditorGUILayout.PropertyField(prop, true);
                } while (prop.NextVisible(false));
            }
        }
    }
}
