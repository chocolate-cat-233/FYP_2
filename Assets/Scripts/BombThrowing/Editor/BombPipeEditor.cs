#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using SuperFinalFuse.BombThrowing.Mechanisms;

namespace SuperFinalFuse.BombThrowing.Editor
{
    [CustomEditor(typeof(BombPipe))]
    public sealed class BombPipeEditor : UnityEditor.Editor
    {
        private BombPipe pipe;
        private SerializedProperty pathPointsRootProperty;

        private void OnEnable()
        {
            pipe = (BombPipe)target;
            pathPointsRootProperty =
                serializedObject.FindProperty("pathPointsRoot");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Ordered Path Editor",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Route order is Portal A -> PathPoints children in hierarchy " +
                "order -> Portal B. Reverse travel uses the same path length.",
                MessageType.Info);

            EditorGUILayout.LabelField(
                "Route Length",
                $"{pipe.PipeLength:0.###} units");
            EditorGUILayout.LabelField(
                "Travel Duration",
                $"{pipe.CurrentTravelDuration:0.###} seconds");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create/Find Path Root"))
                {
                    EnsurePathRoot();
                }
                if (GUILayout.Button("Add Path Point"))
                {
                    AddPathPoint();
                }
            }

            DrawPathPointList();

            if (GUILayout.Button("Refresh From Hierarchy"))
            {
                RefreshPipe();
            }
        }

        private void DrawPathPointList()
        {
            Transform root = pipe.PathPointsRoot;
            if (root == null || root.childCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No intermediate points. Length is currently the direct " +
                    "distance between both portals.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.Space(4f);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform point = root.GetChild(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{i + 1}. {point.name}",
                        GUILayout.MinWidth(120f));
                    if (GUILayout.Button("Select", GUILayout.Width(52f)))
                    {
                        Selection.activeTransform = point;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button("▲", GUILayout.Width(28f)))
                        {
                            MovePoint(point, i - 1);
                            GUIUtility.ExitGUI();
                        }
                    }
                    using (new EditorGUI.DisabledScope(
                        i == root.childCount - 1))
                    {
                        if (GUILayout.Button("▼", GUILayout.Width(28f)))
                        {
                            MovePoint(point, i + 1);
                            GUIUtility.ExitGUI();
                        }
                    }
                    if (GUILayout.Button("X", GUILayout.Width(28f)))
                    {
                        Undo.DestroyObjectImmediate(point.gameObject);
                        RefreshPipe();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private Transform EnsurePathRoot()
        {
            if (pipe.PathPointsRoot != null)
            {
                return pipe.PathPointsRoot;
            }

            Transform existing = pipe.transform.Find("PathPoints");
            if (existing == null)
            {
                GameObject rootObject = new GameObject("PathPoints");
                Undo.RegisterCreatedObjectUndo(
                    rootObject,
                    "Create Bomb Pipe Path Root");
                existing = rootObject.transform;
                existing.SetParent(pipe.transform, false);
            }

            serializedObject.Update();
            pathPointsRootProperty.objectReferenceValue = existing;
            serializedObject.ApplyModifiedProperties();
            RefreshPipe();
            return existing;
        }

        private void AddPathPoint()
        {
            Transform root = EnsurePathRoot();
            int index = root.childCount;
            GameObject pointObject = new GameObject($"Point_{index + 1:00}");
            Undo.RegisterCreatedObjectUndo(
                pointObject,
                "Add Bomb Pipe Path Point");
            Transform point = pointObject.transform;
            point.SetParent(root, true);

            Vector3 previous = index > 0
                ? root.GetChild(index - 1).position
                : pipe.PortalA != null
                    ? pipe.PortalA.position
                    : pipe.transform.position;
            Vector3 destination = pipe.PortalB != null
                ? pipe.PortalB.position
                : previous + pipe.transform.forward;
            point.position = Vector3.Lerp(previous, destination, 0.5f);
            Selection.activeTransform = point;
            RefreshPipe();
        }

        private void MovePoint(Transform point, int siblingIndex)
        {
            Undo.RecordObject(point, "Reorder Bomb Pipe Path Point");
            point.SetSiblingIndex(siblingIndex);
            RefreshPipe();
        }

        private void RefreshPipe()
        {
            pipe.RefreshPathPointsFromHierarchy();
            EditorUtility.SetDirty(pipe);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pipe);
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnSceneGUI()
        {
            if (pipe == null)
            {
                return;
            }

            Handles.color = Color.cyan;
            if (pipe.PortalA != null)
            {
                Handles.Label(pipe.PortalA.position, "Portal A");
                Handles.ArrowHandleCap(
                    0,
                    pipe.PortalA.position,
                    pipe.PortalA.rotation,
                    1.2f,
                    EventType.Repaint);
            }
            if (pipe.PortalB != null)
            {
                Handles.Label(pipe.PortalB.position, "Portal B");
                Handles.ArrowHandleCap(
                    0,
                    pipe.PortalB.position,
                    pipe.PortalB.rotation,
                    1.2f,
                    EventType.Repaint);
            }

            Transform root = pipe.PathPointsRoot;
            if (root == null)
            {
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform point = root.GetChild(i);
                Handles.Label(point.position, $"Point {i + 1}");
                EditorGUI.BeginChangeCheck();
                Vector3 position = Handles.PositionHandle(
                    point.position,
                    Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(point, "Move Bomb Pipe Path Point");
                    point.position = position;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        point);
                    RefreshPipe();
                }
            }
        }
    }
}
#endif
