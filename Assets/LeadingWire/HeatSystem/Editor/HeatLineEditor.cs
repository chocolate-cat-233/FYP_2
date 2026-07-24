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
            EditorGUILayout.PropertyField(providerProp, new GUIContent("上游"));
            EditorGUILayout.PropertyField(acceptorProp, new GUIContent("下游"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔗 自动连线", GUILayout.Height(24))) AutoWire(line);
            if (GUILayout.Button("🔄 重建管线段", GUILayout.Height(24))) line.GenerateSegments();
            EditorGUILayout.EndHorizontal();

            // ── 拐点 ──
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"拐点 ({waypointsProp.arraySize})", EditorStyles.boldLabel);

            for (int i = 0; i < waypointsProp.arraySize; i++)
            {
                var elem = waypointsProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(elem, GUIContent.none);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
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
            if (GUILayout.Button("➕ 中间添加"))
            {
                AddWaypointAt(line, 0.5f);
                line.GenerateSegments();
            }
            if (waypointsProp.arraySize > 0 && GUILayout.Button("清空"))
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

            // ── 管线段 ──
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("管线段", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(segmentCountProp, new GUIContent("数量"));
            EditorGUILayout.PropertyField(cascadeDelayProp, new GUIContent("级联延迟(s)"));

            EditorGUILayout.Space(5);
            // 其余字段
            var skip = new HashSet<string> { "provider", "acceptor", "waypoints", "segmentCount",
                "cascadeDelay", "segmentPrefab", "energizingDuration" };
            var prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do { if (!skip.Contains(prop.name)) EditorGUILayout.PropertyField(prop, true); }
                while (prop.NextVisible(false));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var line = (HeatLine)target;
            var path = BuildEditorPath(line);
            if (path.Count < 2) return;

            // 绘制路径
            Handles.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            for (int i = 0; i < path.Count - 1; i++)
                Handles.DrawLine(path[i], path[i + 1], 3f);

            // 拖拽拐点：path 中跳过 provider 和 acceptor 的点
            int wpStart = line.Provider != null ? 1 : 0;
            int wpEnd = line.Acceptor != null ? path.Count - 2 : path.Count - 1;

            for (int i = wpStart; i <= wpEnd && i < path.Count; i++)
            {
                int wpIdx = i - wpStart;
                if (wpIdx >= waypointsProp.arraySize) continue;

                var wp = waypointsProp.GetArrayElementAtIndex(wpIdx).objectReferenceValue as Transform;
                if (wp == null) continue;

                EditorGUI.BeginChangeCheck();
                float size = HandleUtility.GetHandleSize(path[i]) * 0.08f;
                Vector3 newPos = Handles.FreeMoveHandle(path[i], size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wp, "Move Waypoint");
                    wp.position = newPos;
                    line.GenerateSegments();
                }
            }

            // 段中点：点击添加拐点
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 mid = (path[i] + path[i + 1]) * 0.5f;
                float size = HandleUtility.GetHandleSize(mid) * 0.05f;
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

        private void AddWaypointAt(HeatLine line, float t)
        {
            var path = BuildEditorPath(line);
            var pt = GetPointOnPath(path, t);
            var go = new GameObject($"WP_{waypointsProp.arraySize}");
            go.transform.SetParent(line.transform);
            go.transform.position = pt;
            Undo.RegisterCreatedObjectUndo(go, "Add Waypoint");
            waypointsProp.InsertArrayElementAtIndex(waypointsProp.arraySize);
            waypointsProp.GetArrayElementAtIndex(waypointsProp.arraySize - 1).objectReferenceValue = go.transform;
            serializedObject.ApplyModifiedProperties();
            Selection.activeGameObject = go;
        }

        private void AutoWire(HeatLine line)
        {
            serializedObject.Update();
            if (providerProp.objectReferenceValue == null)
            {
                var all = Object.FindObjectsByType<HeatProvider>(FindObjectsSortMode.None);
                HeatProvider best = null;
                float bestD = float.MaxValue;
                foreach (var p in all)
                {
                    if ((Object)p == (Object)line) continue;
                    float d = Vector3.Distance(p.transform.position, line.transform.position);
                    if (d < bestD) { bestD = d; best = p; }
                }
                if (best != null) providerProp.objectReferenceValue = best;
            }
            if (acceptorProp.objectReferenceValue == null)
            {
                var all = Object.FindObjectsByType<HeatNode>(FindObjectsSortMode.None);
                HeatNode best = null;
                float bestD = float.MaxValue;
                foreach (var a in all)
                {
                    if ((Object)a == (Object)line) continue;
                    float d = Vector3.Distance(a.transform.position, line.transform.position);
                    if (d < bestD) { bestD = d; best = a; }
                }
                if (best != null) acceptorProp.objectReferenceValue = best;
            }
            serializedObject.ApplyModifiedProperties();
            line.GenerateSegments();
        }

        private List<Vector3> BuildEditorPath(HeatLine line)
        {
            var pts = new List<Vector3>();
            if (providerProp.objectReferenceValue != null)
                pts.Add(((HeatProvider)providerProp.objectReferenceValue).transform.position);
            for (int i = 0; i < waypointsProp.arraySize; i++)
            {
                var t = waypointsProp.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (t != null) pts.Add(t.position);
            }
            if (acceptorProp.objectReferenceValue != null)
                pts.Add(((HeatNode)acceptorProp.objectReferenceValue).transform.position);
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
            float total = 0f;
            var lens = new float[pts.Count - 1];
            for (int i = 0; i < pts.Count - 1; i++)
                total += (lens[i] = Vector3.Distance(pts[i], pts[i + 1]));
            if (total < 0.001f) return pts[0];
            float target = t * total, accum = 0f;
            for (int i = 0; i < lens.Length; i++)
            {
                if (target <= accum + lens[i])
                {
                    float st = lens[i] > 0.001f ? (target - accum) / lens[i] : 0f;
                    return Vector3.Lerp(pts[i], pts[i + 1], st);
                }
                accum += lens[i];
            }
            return pts[pts.Count - 1];
        }
    }
}
