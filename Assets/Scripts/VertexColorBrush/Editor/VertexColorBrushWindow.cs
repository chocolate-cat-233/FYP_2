using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperFinalFuse.VertexColorBrush.Editor
{
    /// <summary>
    /// Scene-view brush that paints vertex colors on MeshFilter meshes.
    /// Designed to work with Custom/GrassVertexColor: paint Color.red to
    /// set the grass mask (R = 1), erase (Shift+LMB or Erase Mode) to
    /// carve ground (R = 0).
    /// </summary>
    public enum DebugChannel
    {
        Rgb = 0,
        R = 1,
        G = 2,
        B = 3,
        A = 4
    }

    public sealed class VertexColorBrushWindow : EditorWindow
    {
        private const string WindowTitle = "Vertex Color Brush";
        private const float RayDistance = 500f;
        private static readonly Color EraseColor = Color.black;

        [SerializeField] private bool brushEnabled = true;
        [SerializeField] private float brushRadius = 1f;
        [SerializeField] private float brushStrength = 1f;
        [SerializeField, Range(0f, 1f)] private float brushSoftness = 0.35f;
        [SerializeField] private Color paintColor = Color.red;
        [SerializeField] private bool eraseMode;
        [SerializeField] private bool previewVertexColors;
        [SerializeField] private bool previewOnGizmo = true;
        // RGB default: painting any color produces immediate visual
        // feedback; switch to R/G to inspect the grass mask channels.
        [SerializeField] private DebugChannel debugChannel = DebugChannel.Rgb;

        private bool isPainting;
        private bool strokeErase;
        private bool strokeChanged;
        private Mesh strokeMesh;
        private Transform strokeTransform;
        private Vector3[] strokeVertices;
        private Vector3[] strokeWorldVertices;
        private int[] strokeTriangles;
        private Color32[] strokeColors;

        private bool hasHoverHit;
        private RaycastHit hoverHit;
        private Vector3 lastHoverPoint;
        private Vector3 lastPaintPoint;
        private int[] strokeTriangle;
        private int[] lastPaintTriangle;

        private Material debugMaterial;
        private MeshRenderer previewRenderer;
        private Material[] previewOriginalMaterials;

        private static Material overlayMaterial;
        private MeshRenderer gizmoPreviewRenderer;
        private Vector3[] gizmoWorldVertices;
        private Vector3[] gizmoWorldNormals;
        private int[] gizmoTriangles;
        private Color32[] gizmoColors;
        private bool gizmoColorsDirty;

        private Mesh pendingMesh;
        private string statusMessage;
        private bool statusIsError;

        [MenuItem("Tools/Vertex Color Brush")]
        private static void OpenWindow()
        {
            VertexColorBrushWindow window =
                GetWindow<VertexColorBrushWindow>(WindowTitle);
            window.minSize = new Vector2(320f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneView;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += OnUndoPerformed;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneView;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoPerformed;
            RestorePreview();
            if (debugMaterial != null)
            {
                DestroyImmediate(debugMaterial);
                debugMaterial = null;
            }
            EndStroke();
        }

        private void OnSceneSaving(Scene scene, string path)
        {
            RestorePreview();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                RestorePreview();
            }
        }

        private void OnUndoPerformed()
        {
            gizmoColorsDirty = true;
            SceneView.RepaintAll();
        }

        // ── Scene view ────────────────────────────────────────────────

        private void OnSceneView(SceneView sceneView)
        {
            Event e = Event.current;
            hasHoverHit = TryGetHoverTarget(out hoverHit);
            UpdatePreview();
            UpdateGizmoPreview();

            if (!brushEnabled)
            {
                return;
            }

            // Handles drawing is only valid during Repaint.
            if (e.type == EventType.Repaint)
            {
                if (hasHoverHit)
                {
                    DrawVertexColorOverlay(sceneView);
                    DrawBrushGizmo(hoverHit.point, hoverHit.normal);
                    if (Vector3.Distance(hoverHit.point, lastHoverPoint) > 0.01f)
                    {
                        lastHoverPoint = hoverHit.point;
                        sceneView.Repaint();
                    }
                }
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 &&
                hasHoverHit && !e.alt && !e.control && !e.command)
            {
                if (isPainting)
                {
                    // Safety: end a stroke whose MouseUp was missed (e.g.
                    // released over another window).
                    EndStroke();
                }
                lastPaintPoint = hoverHit.point;
                BeginStroke(hoverHit, eraseMode || e.shift);
                if (isPainting)
                {
                    e.Use();
                    sceneView.Repaint();
                }
            }
            else if (e.type == EventType.MouseDrag && isPainting)
            {
                // Drag events do not reliably report a button; isPainting
                // is the real gate. When the ray briefly misses the
                // collider, keep painting from the last known point so the
                // stroke stays continuous.
                Vector3 paintPoint = hasHoverHit
                    ? hoverHit.point
                    : lastPaintPoint;
                int[] paintTriangle = hasHoverHit
                    ? GetHitTriangle(hoverHit, strokeMesh)
                    : lastPaintTriangle;
                PaintStroke(paintPoint, paintTriangle);
                lastPaintPoint = paintPoint;
                lastPaintTriangle = paintTriangle;
                e.Use();
                sceneView.Repaint();
            }
            else if (e.type == EventType.MouseUp && isPainting)
            {
                EndStroke();
                e.Use();
                sceneView.Repaint();
            }
            else if (e.type == EventType.MouseLeaveWindow && isPainting)
            {
                EndStroke();
            }
        }

        private bool TryGetHoverTarget(out RaycastHit hit)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            return Physics.Raycast(ray, out hit, RayDistance);
        }

        private void DrawBrushGizmo(Vector3 center, Vector3 normal)
        {
            Handles.color = new Color(1f, 0.55f, 0.15f, 0.9f);
            Handles.DrawWireDisc(center, normal, brushRadius);

            float innerRadius = Mathf.Max(
                0.01f,
                brushRadius * (1f - Mathf.Clamp01(brushSoftness)));
            Handles.color = new Color(1f, 0.55f, 0.15f, 0.35f);
            Handles.DrawWireDisc(center, normal, innerRadius);
            Handles.DrawSolidDisc(center, normal, 0.03f);
        }

        // ── Vertex color preview ───────────────────────────────────────

        private void EnsureDebugMaterial()
        {
            if (debugMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Custom/DebugVertexColor");
            if (shader == null)
            {
                SetStatus(
                    "Debug shader 'Custom/DebugVertexColor' not found.",
                    true);
                return;
            }

            debugMaterial = new Material(shader)
            {
                name = "VertexColorPreview (Editor)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void UpdatePreview()
        {
            MeshRenderer target = null;
            if (previewVertexColors && hasHoverHit)
            {
                MeshFilter meshFilter =
                    hoverHit.collider.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    target = hoverHit.collider.GetComponent<MeshRenderer>();
                }
            }

            if (target == previewRenderer)
            {
                return;
            }

            RestorePreview();
            if (target == null)
            {
                return;
            }

            EnsureDebugMaterial();
            if (debugMaterial == null)
            {
                return;
            }

            previewRenderer = target;
            previewOriginalMaterials = target.sharedMaterials;
            target.sharedMaterials = new[] { debugMaterial };
            debugMaterial.SetFloat("_DebugChannel", (float)debugChannel);
        }

        private void RestorePreview()
        {
            if (previewRenderer == null)
            {
                previewOriginalMaterials = null;
                return;
            }

            if (previewOriginalMaterials != null)
            {
                previewRenderer.sharedMaterials = previewOriginalMaterials;
            }

            previewRenderer = null;
            previewOriginalMaterials = null;
        }

        // ── Vertex colors on the brush gizmo ───────────────────────────

        private void UpdateGizmoPreview()
        {
            MeshRenderer target = null;
            MeshFilter targetFilter = null;
            if (previewOnGizmo && hasHoverHit)
            {
                targetFilter = hoverHit.collider.GetComponent<MeshFilter>();
                if (targetFilter != null &&
                    targetFilter.sharedMesh != null &&
                    targetFilter.sharedMesh.isReadable)
                {
                    target = hoverHit.collider.GetComponent<MeshRenderer>();
                }
            }

            if (target == gizmoPreviewRenderer)
            {
                return;
            }

            gizmoPreviewRenderer = target;
            gizmoWorldVertices = null;
            gizmoWorldNormals = null;
            gizmoTriangles = null;
            gizmoColors = null;
            if (target == null)
            {
                return;
            }

            Mesh mesh = targetFilter.sharedMesh;
            BuildGizmoCache(mesh, target.transform);
        }

        private void BuildGizmoCache(Mesh mesh, Transform transform)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            gizmoWorldVertices = new Vector3[vertices.Length];
            gizmoWorldNormals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                gizmoWorldVertices[i] = transform.TransformPoint(vertices[i]);
                gizmoWorldNormals[i] =
                    normals != null && i < normals.Length
                        ? transform.TransformDirection(normals[i])
                        : Vector3.zero;
            }

            gizmoTriangles = mesh.triangles;
            RefreshGizmoColors();
        }

        private void RefreshGizmoColors()
        {
            gizmoColorsDirty = false;
            if (gizmoPreviewRenderer == null)
            {
                gizmoColors = null;
                return;
            }

            Mesh mesh =
                gizmoPreviewRenderer.GetComponent<MeshFilter>().sharedMesh;
            Color32[] colors = mesh.colors32;
            if (colors == null || colors.Length != gizmoWorldVertices.Length)
            {
                // No color channel: default to white (all grass).
                colors = new Color32[gizmoWorldVertices.Length];
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = new Color32(255, 255, 255, 255);
                }
            }

            gizmoColors = colors;
        }

        private void DrawVertexColorOverlay(SceneView sceneView)
        {
            if (!previewOnGizmo || gizmoPreviewRenderer == null ||
                gizmoWorldVertices == null || gizmoTriangles == null ||
                gizmoColors == null ||
                gizmoColors.Length != gizmoWorldVertices.Length)
            {
                return;
            }

            if (gizmoColorsDirty)
            {
                RefreshGizmoColors();
            }

            Material overlay = GetOverlayMaterial();
            if (overlay == null)
            {
                return;
            }

            Handles.SetCamera(sceneView.camera);
            overlay.SetPass(0);
            GL.PushMatrix();
            if (Handles.matrix != Matrix4x4.identity)
            {
                GL.MultMatrix(Handles.matrix);
            }

            GL.Begin(GL.TRIANGLES);
            for (int tri = 0; tri + 2 < gizmoTriangles.Length; tri += 3)
            {
                int i0 = gizmoTriangles[tri];
                int i1 = gizmoTriangles[tri + 1];
                int i2 = gizmoTriangles[tri + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= gizmoWorldVertices.Length ||
                    i1 >= gizmoWorldVertices.Length ||
                    i2 >= gizmoWorldVertices.Length)
                {
                    continue;
                }

                EmitVertex(i0);
                EmitVertex(i1);
                EmitVertex(i2);
            }
            GL.End();
            GL.PopMatrix();
        }

        private void EmitVertex(int index)
        {
            Color display = ChannelColor(gizmoColors[index], debugChannel);
            display.a = 0.55f;
            GL.Color(display);
            // Offset slightly along the normal so the overlay does not
            // z-fight with the mesh surface.
            GL.Vertex(
                gizmoWorldVertices[index] +
                gizmoWorldNormals[index] * 0.02f);
        }

        private static Color ChannelColor(Color32 c, DebugChannel channel)
        {
            switch (channel)
            {
                case DebugChannel.R:
                    return new Color(c.r / 255f, 0f, 0f, 1f);
                case DebugChannel.G:
                    return new Color(0f, c.g / 255f, 0f, 1f);
                case DebugChannel.B:
                    return new Color(0f, 0f, c.b / 255f, 1f);
                case DebugChannel.A:
                    float a = c.a / 255f;
                    return new Color(a, a, a, 1f);
                default:
                    return new Color(
                        c.r / 255f,
                        c.g / 255f,
                        c.b / 255f,
                        1f);
            }
        }

        private static Material GetOverlayMaterial()
        {
            if (overlayMaterial != null)
            {
                return overlayMaterial;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return null;
            }

            overlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return overlayMaterial;
        }

        // ── Stroke lifecycle ───────────────────────────────────────────

        private void BeginStroke(RaycastHit hit, bool erase)
        {
            MeshFilter meshFilter = hit.collider.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                SetStatus("Target has no MeshFilter to paint.", true);
                return;
            }

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                SetStatus("Target mesh is null.", true);
                return;
            }

            if (!mesh.isReadable)
            {
                ShowNotReadable(mesh);
                return;
            }

            strokeMesh = mesh;
            strokeTransform = meshFilter.transform;
            strokeVertices = mesh.vertices;
            strokeWorldVertices = new Vector3[strokeVertices.Length];
            for (int i = 0; i < strokeVertices.Length; i++)
            {
                strokeWorldVertices[i] =
                    strokeTransform.TransformPoint(strokeVertices[i]);
            }
            strokeTriangles = mesh.triangles;
            strokeColors = mesh.colors32;
            if (strokeColors == null ||
                strokeColors.Length != strokeVertices.Length)
            {
                // A mesh without a color channel defaults to white:
                // fully grass, full wind.
                strokeColors = new Color32[strokeVertices.Length];
                for (int i = 0; i < strokeColors.Length; i++)
                {
                    strokeColors[i] = new Color32(255, 255, 255, 255);
                }
            }

            strokeErase = erase;
            strokeChanged = false;
            isPainting = true;
            strokeTriangle = GetHitTriangle(hit, mesh);
            lastPaintTriangle = strokeTriangle;
            Undo.RecordObject(mesh, "Paint Vertex Colors");
            PaintStroke(hit.point, strokeTriangle);
        }

        private void PaintStroke(Vector3 center, int[] hitTriangle)
        {
            if (strokeMesh == null)
            {
                return;
            }

            float radius = Mathf.Max(0.001f, brushRadius);
            float falloffStart = radius * (1f - Mathf.Clamp01(brushSoftness));
            Color32 targetColor = strokeErase ? EraseColor : paintColor;
            bool changed = false;

            // Paint per triangle, with the falloff based on the distance
            // from the stroke point to the triangle centroid. On dense
            // meshes this matches per-vertex falloff closely; on sparse
            // meshes (few, large triangles) radius, strength and softness
            // still visibly affect the painted area.
            for (int tri = 0; tri + 2 < strokeTriangles.Length; tri += 3)
            {
                int i0 = strokeTriangles[tri];
                int i1 = strokeTriangles[tri + 1];
                int i2 = strokeTriangles[tri + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 ||
                    i0 >= strokeWorldVertices.Length ||
                    i1 >= strokeWorldVertices.Length ||
                    i2 >= strokeWorldVertices.Length)
                {
                    continue;
                }

                Vector3 centroid =
                    (strokeWorldVertices[i0] +
                     strokeWorldVertices[i1] +
                     strokeWorldVertices[i2]) * (1f / 3f);
                float distance = GetBrushDistance(center, centroid);
                if (distance > radius)
                {
                    continue;
                }

                float falloff = 1f - Mathf.SmoothStep(
                    falloffStart,
                    radius,
                    distance);
                float t = Mathf.Clamp01(brushStrength * falloff);
                if (t <= 0f)
                {
                    continue;
                }

                changed |= PaintVertex(i0, targetColor, t);
                changed |= PaintVertex(i1, targetColor, t);
                changed |= PaintVertex(i2, targetColor, t);
            }

            // Guaranteed minimum coverage: always paint the hit triangle,
            // so a click registers even on sparse meshes (e.g. low-poly
            // ground) whose triangles are larger than the brush radius.
            if (hitTriangle != null)
            {
                float t = Mathf.Clamp01(brushStrength);
                for (int k = 0; k < hitTriangle.Length; k++)
                {
                    changed |= PaintVertex(
                        hitTriangle[k],
                        targetColor,
                        t);
                }
            }

            if (!changed)
            {
                return;
            }

            strokeMesh.colors32 = strokeColors;
            strokeMesh.UploadMeshData(false);
            EditorUtility.SetDirty(strokeMesh);
            strokeChanged = true;
            gizmoColorsDirty = true;
        }

        private bool PaintVertex(int index, Color32 targetColor, float t)
        {
            if (index < 0 || index >= strokeColors.Length)
            {
                return false;
            }

            Color32 next = Color32.LerpUnclamped(
                strokeColors[index],
                targetColor,
                t);
            if (next.r != strokeColors[index].r ||
                next.g != strokeColors[index].g ||
                next.b != strokeColors[index].b ||
                next.a != strokeColors[index].a)
            {
                strokeColors[index] = next;
                return true;
            }

            return false;
        }

        private void EndStroke()
        {
            if (!isPainting)
            {
                return;
            }

            isPainting = false;
            if (strokeChanged && strokeMesh != null)
            {
                AssetDatabase.SaveAssetIfDirty(strokeMesh);
                SetStatus(
                    $"Saved vertex colors on {strokeMesh.name}.",
                    false);
            }

            strokeMesh = null;
            strokeTransform = null;
            strokeVertices = null;
            strokeWorldVertices = null;
            strokeTriangles = null;
            strokeColors = null;
            strokeTriangle = null;
            lastPaintTriangle = null;
        }

        /// <summary>
        /// Distance projected onto the xz plane so painting on sloped
        /// ground keeps a constant footprint (terrain-style).
        /// </summary>
        private static float GetBrushDistance(Vector3 center, Vector3 vertex)
        {
            Vector3 delta = vertex - center;
            delta.y = 0f;
            return delta.magnitude;
        }

        /// <summary>
        /// Vertex indices of the mesh triangle the ray hit. Only valid when
        /// the hit collider is a MeshCollider referencing the painted mesh;
        /// primitive colliders report -1 and return null.
        /// </summary>
        private static int[] GetHitTriangle(RaycastHit hit, Mesh strokeMesh)
        {
            if (hit.triangleIndex < 0 || strokeMesh == null)
            {
                return null;
            }

            if (!(hit.collider is MeshCollider meshCollider) ||
                meshCollider.sharedMesh != strokeMesh)
            {
                return null;
            }

            int[] triangles = strokeMesh.triangles;
            int baseIndex = hit.triangleIndex * 3;
            if (baseIndex < 0 || baseIndex + 2 >= triangles.Length)
            {
                return null;
            }

            return new[]
            {
                triangles[baseIndex],
                triangles[baseIndex + 1],
                triangles[baseIndex + 2]
            };
        }

        // ── Window UI ──────────────────────────────────────────────────

        private void OnGUI()
        {
            brushEnabled = EditorGUILayout.ToggleLeft(
                "Brush Enabled (Scene View)",
                brushEnabled);
            EditorGUILayout.Space(4);

            brushRadius = EditorGUILayout.Slider(
                "Radius",
                brushRadius,
                0.05f,
                10f);
            brushStrength = EditorGUILayout.Slider(
                "Strength",
                brushStrength,
                0f,
                1f);
            brushSoftness = EditorGUILayout.Slider(
                "Softness",
                brushSoftness,
                0f,
                1f);
            paintColor = EditorGUILayout.ColorField(
                "Paint Color",
                paintColor);
            eraseMode = EditorGUILayout.ToggleLeft(
                "Erase Mode (Shift = quick erase)",
                eraseMode);

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            previewOnGizmo = EditorGUILayout.ToggleLeft(
                "Vertex Color Overlay (hover)",
                previewOnGizmo);
            previewVertexColors = EditorGUILayout.ToggleLeft(
                "Swap Material Preview (hover)",
                previewVertexColors);
            debugChannel = (DebugChannel)EditorGUILayout.EnumPopup(
                "Preview Channel",
                debugChannel);
            if (EditorGUI.EndChangeCheck())
            {
                UpdatePreview();
                UpdateGizmoPreview();
                if (debugMaterial != null)
                {
                    debugMaterial.SetFloat(
                        "_DebugChannel",
                        (float)debugChannel);
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill All"))
            {
                FillOrClearAll(paintColor, "Fill");
            }
            if (GUILayout.Button("Clear All"))
            {
                FillOrClearAll(Color.black, "Clear");
            }
            EditorGUILayout.EndHorizontal();

            if (pendingMesh != null)
            {
                string path = AssetDatabase.GetAssetPath(pendingMesh);
                if (!string.IsNullOrEmpty(path) &&
                    AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("Enable Read/Write & Reimport"))
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                        SetStatus(
                            "Read/Write enabled. Reimport resets previously " +
                            "painted colors — paint again.",
                            false);
                        pendingMesh = null;
                    }
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "LMB paints, Shift+LMB erases. Target needs a collider and " +
                "a readable mesh. Grass shader: R = grass mask (1 grass, " +
                "0 ground), G = wind amount.\n\n" +
                "Hovering overlays the whole mesh's vertex colors as a " +
                "semi-transparent GL overlay (no material change). The " +
                "optional Swap Material preview tints the mesh instead and " +
                "is restored on scene save, play mode, or window close.",
                MessageType.Info);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    statusMessage,
                    statusIsError ? MessageType.Error : MessageType.None);
            }
        }

        private void FillOrClearAll(Color color, string action)
        {
            int count = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (TryApplyToAllVertices(go, color))
                {
                    count++;
                }
            }

            SetStatus(
                count > 0
                    ? $"{action} applied to {count} mesh(es)."
                    : "No fillable mesh in selection.",
                count == 0);
        }

        private bool TryApplyToAllVertices(GameObject go, Color color)
        {
            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = meshFilter.sharedMesh;
            if (!mesh.isReadable)
            {
                SetStatus(
                    $"Mesh '{mesh.name}' is not readable.",
                    true);
                return false;
            }

            Undo.RecordObject(mesh, "Fill Vertex Colors");
            Color32[] colors = new Color32[mesh.vertexCount];
            Color32 color32 = color;
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color32;
            }

            mesh.colors32 = colors;
            mesh.UploadMeshData(false);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return true;
        }

        // ── Status ─────────────────────────────────────────────────────

        private void ShowNotReadable(Mesh mesh)
        {
            pendingMesh = mesh;
            string path = AssetDatabase.GetAssetPath(mesh);
            SetStatus(
                string.IsNullOrEmpty(path)
                    ? $"Mesh '{mesh.name}' is not readable — enable " +
                      "Read/Write on the mesh asset."
                    : $"Mesh '{mesh.name}' is not readable — enable " +
                      "Read/Write in the ModelImporter.",
                true);
        }

        private void SetStatus(string message, bool isError)
        {
            statusMessage = message;
            statusIsError = isError;
        }
    }
}
