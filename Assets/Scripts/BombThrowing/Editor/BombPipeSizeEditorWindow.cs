using UnityEditor;
using UnityEngine;

namespace SuperFinalFuse.BombThrowing.Editor
{
    public sealed class BombPipeSizeEditorWindow : EditorWindow
    {
        private const string WindowTitle = "Bomb Pipe Size";
        private GameObject pipeRootObject;
        private float desiredLocalLength = 1f;
        private string validationMessage;

        [MenuItem("Tools/Bomb Throwing/Bomb Pipe Size Editor")]
        private static void OpenWindow()
        {
            BombPipeSizeEditorWindow window =
                GetWindow<BombPipeSizeEditorWindow>(WindowTitle);
            window.minSize = new Vector2(360f, 230f);
            window.TryUseSelection();
            window.Show();
        }

        private void OnSelectionChange()
        {
            TryUseSelection();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                "Bomb Pipe X Size",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Only Visual/Model Local Scale X is changed. Entrance and Exit " +
                "are moved to the two model surfaces; their Transform scale and " +
                "BoxCollider size remain unchanged.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            GameObject selectedRoot = (GameObject)EditorGUILayout.ObjectField(
                "Pipe Root",
                pipeRootObject,
                typeof(GameObject),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                SetPipeRoot(selectedRoot);
            }

            using (new EditorGUI.DisabledScope(pipeRootObject == null))
            {
                desiredLocalLength = EditorGUILayout.FloatField(
                    "Model X Length",
                    desiredLocalLength);
                desiredLocalLength = Mathf.Max(0.01f, desiredLocalLength);

                EditorGUILayout.Space(6f);
                if (GUILayout.Button("Read Current Model Length"))
                {
                    ReadCurrentLength();
                }

                if (GUILayout.Button("Apply Pipe Size", GUILayout.Height(30f)))
                {
                    ApplyPipeSize();
                }
            }

            if (!string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(
                    validationMessage,
                    pipeRootObject != null
                        ? MessageType.Warning
                        : MessageType.Info);
            }
        }

        private void TryUseSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            Transform root = FindPipeRoot(selected.transform);
            if (root != null)
            {
                SetPipeRoot(root.gameObject);
            }
        }

        private void SetPipeRoot(GameObject candidate)
        {
            if (candidate == null)
            {
                pipeRootObject = null;
                validationMessage = "Select BombPipe or one of its children.";
                return;
            }

            Transform root = FindPipeRoot(candidate.transform);
            if (root == null)
            {
                pipeRootObject = null;
                validationMessage =
                    "The selected hierarchy must contain Entrance, Exit, " +
                    "CapturePoint, and Visual/Model.";
                return;
            }

            pipeRootObject = root.gameObject;
            validationMessage = null;
            ReadCurrentLength();
        }

        private void ReadCurrentLength()
        {
            if (!TryGetParts(
                    out Transform root,
                    out _,
                    out _,
                    out _,
                    out Transform model,
                    out MeshFilter meshFilter))
            {
                return;
            }

            desiredLocalLength = GetModelLengthInRootSpace(
                root,
                model,
                meshFilter.sharedMesh.bounds);
        }

        private void ApplyPipeSize()
        {
            if (!TryGetParts(
                    out Transform root,
                    out Transform entrance,
                    out Transform exit,
                    out Transform capturePoint,
                    out Transform model,
                    out MeshFilter meshFilter))
            {
                return;
            }

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            float currentLength = GetModelLengthInRootSpace(
                root,
                model,
                meshBounds);
            if (currentLength <= 0.0001f)
            {
                validationMessage =
                    "Model has no measurable X length in BombPipe root space.";
                return;
            }

            Undo.RecordObjects(
                new Object[] { model, entrance, exit, capturePoint },
                "Resize Bomb Pipe");

            Vector3 modelScale = model.localScale;
            modelScale.x *= desiredLocalLength / currentLength;
            model.localScale = modelScale;

            GetModelXRangeInRootSpace(
                root,
                model,
                meshBounds,
                out float minimumX,
                out float maximumX);
            float centerX = (minimumX + maximumX) * 0.5f;

            Vector3 entrancePosition = entrance.localPosition;
            entrancePosition.x = maximumX;
            entrance.localPosition = entrancePosition;

            Vector3 exitPosition = exit.localPosition;
            exitPosition.x = minimumX;
            exit.localPosition = exitPosition;

            Vector3 capturePosition = capturePoint.localPosition;
            capturePosition.x = centerX;
            capturePoint.localPosition = capturePosition;

            RecordPrefabModification(model);
            RecordPrefabModification(entrance);
            RecordPrefabModification(exit);
            RecordPrefabModification(capturePoint);
            EditorUtility.SetDirty(pipeRootObject);
            if (EditorUtility.IsPersistent(pipeRootObject))
            {
                AssetDatabase.SaveAssets();
            }

            validationMessage =
                $"Applied X length {desiredLocalLength:0.###}. Entrance/Exit " +
                "collider sizes and Transform scales were preserved.";
            SceneView.RepaintAll();
        }

        private bool TryGetParts(
            out Transform root,
            out Transform entrance,
            out Transform exit,
            out Transform capturePoint,
            out Transform model,
            out MeshFilter meshFilter)
        {
            root = pipeRootObject != null
                ? FindPipeRoot(pipeRootObject.transform)
                : null;
            entrance = root != null ? root.Find("Entrance") : null;
            exit = root != null ? root.Find("Exit") : null;
            capturePoint = root != null ? root.Find("CapturePoint") : null;
            model = root != null ? root.Find("Visual/Model") : null;
            meshFilter = model != null
                ? model.GetComponent<MeshFilter>()
                : null;

            if (root != null && entrance != null && exit != null &&
                capturePoint != null && model != null &&
                meshFilter != null && meshFilter.sharedMesh != null)
            {
                validationMessage = null;
                return true;
            }

            validationMessage =
                "Missing Entrance, Exit, CapturePoint, Visual/Model, or the " +
                "Model MeshFilter/shared mesh.";
            return false;
        }

        private static Transform FindPipeRoot(Transform candidate)
        {
            Transform current = candidate;
            while (current != null)
            {
                if (current.Find("Entrance") != null &&
                    current.Find("Exit") != null &&
                    current.Find("CapturePoint") != null &&
                    current.Find("Visual/Model") != null)
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private static float GetModelLengthInRootSpace(
            Transform root,
            Transform model,
            Bounds meshBounds)
        {
            GetModelXRangeInRootSpace(
                root,
                model,
                meshBounds,
                out float minimumX,
                out float maximumX);
            return Mathf.Abs(maximumX - minimumX);
        }

        private static void GetModelXRangeInRootSpace(
            Transform root,
            Transform model,
            Bounds meshBounds,
            out float minimumX,
            out float maximumX)
        {
            Vector3 localMinimum = meshBounds.center -
                Vector3.right * meshBounds.extents.x;
            Vector3 localMaximum = meshBounds.center +
                Vector3.right * meshBounds.extents.x;
            float firstX = root.InverseTransformPoint(
                model.TransformPoint(localMinimum)).x;
            float secondX = root.InverseTransformPoint(
                model.TransformPoint(localMaximum)).x;
            minimumX = Mathf.Min(firstX, secondX);
            maximumX = Mathf.Max(firstX, secondX);
        }

        private static void RecordPrefabModification(Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    target);
            }
        }
    }
}
