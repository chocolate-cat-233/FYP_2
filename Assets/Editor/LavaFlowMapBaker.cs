using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Deterministic texture baker for Custom/LavaFlow.
///
/// Flow map packing:
///   R/G = signed flow direction encoded to 0..1
///   B   = per-rivulet speed value
///   A   = alpha-clip coverage
/// Pattern packing:
///   R = heat, G = cooling crust, B = height, A = hot filament
/// </summary>
public sealed class LavaFlowMapBaker : EditorWindow
{
    const string OutputDirectory = "Assets/Texture/Lava";
    const string PatternPath = OutputDirectory + "/LavaPattern.png";
    const string TopPath = OutputDirectory + "/LavaFlowMapTop.png";
    const string SidePath = OutputDirectory + "/LavaFlowMapSide.png";
    const string MaterialPath = "Assets/Shader/LavaFlow.mat";
    const int PreviewSize = 192;
    const int MetadataVersion = 2;

    enum BakeResolution
    {
        Size256 = 256,
        Size512 = 512,
        Size1024 = 1024,
    }

    [Serializable]
    struct BakeMetadata
    {
        public int version;
        public int seed;
        public int resolution;
        public int rivuletCount;
        public float rivuletWidth;
        public float meander;
        public float branching;
        public float breakup;
        public float speedVariation;
        public float turbulence;
        public float topSpread;
    }

    struct Parameters
    {
        public int seed;
        public int rivuletCount;
        public float rivuletWidth;
        public float meander;
        public float branching;
        public float breakup;
        public float speedVariation;
        public float turbulence;
        public float topSpread;
    }

    struct RivuletSample
    {
        public Vector2 flow;
        public float speed;
        public float coverage;
    }

    [SerializeField] int seed = 1749;
    [SerializeField] BakeResolution resolution = BakeResolution.Size512;
    [SerializeField] int rivuletCount = 12;
    [SerializeField] float rivuletWidth = 0.46f;
    [SerializeField] float meander = 0.38f;
    [SerializeField] float branching = 0.34f;
    [SerializeField] float breakup = 0.24f;
    [SerializeField] float speedVariation = 0.45f;
    [SerializeField] float turbulence = 0.31f;
    [SerializeField] float topSpread = 0.78f;

    Texture2D topPreview;
    Texture2D sidePreview;
    Texture2D patternPreview;
    Vector2 scrollPosition;

    [MenuItem("Tools/Lava/Waterfall Texture Baker")]
    static void OpenWindow()
    {
        var window = GetWindow<LavaFlowMapBaker>("Lava Waterfall Baker");
        window.minSize = new Vector2(720f, 620f);
        window.Show();
    }

    // Also serves CI and command-line asset regeneration:
    // Unity.exe -batchmode -projectPath <project> -executeMethod LavaFlowMapBaker.BakeDefaultAssets -quit
    [MenuItem("Tools/Lava/Bake Default Waterfall Assets")]
    public static void BakeDefaultAssets()
    {
        var baker = CreateInstance<LavaFlowMapBaker>();
        try
        {
            baker.ResetDefaults();
            baker.BakeAndAssign();
        }
        finally
        {
            DestroyImmediate(baker);
        }
    }

    void OnEnable()
    {
        minSize = new Vector2(720f, 620f);
        RebuildPreview();
    }

    void OnDisable()
    {
        DestroyPreview(ref topPreview);
        DestroyPreview(ref sidePreview);
        DestroyPreview(ref patternPreview);
    }

    void OnGUI()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("国王大道风格岩浆瀑布", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "生成可无缝平铺的顶面流图、侧面分股流图和打包岩浆纹理。" +
            "生成并赋值只会更新 LavaFlow.mat，不会修改 Prefab 或场景。",
            MessageType.Info);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawSettings();
        EditorGUILayout.Space(8f);
        DrawActions();
        EditorGUILayout.Space(10f);
        DrawPreviews();
        EditorGUILayout.EndScrollView();
    }

    void DrawSettings()
    {
        EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
        seed = EditorGUILayout.IntField("Seed", seed);
        resolution = (BakeResolution)EditorGUILayout.EnumPopup("分辨率", resolution);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("细流形态", EditorStyles.boldLabel);
        rivuletCount = EditorGUILayout.IntSlider("细流数量", rivuletCount, 4, 32);
        rivuletWidth = EditorGUILayout.Slider("总体覆盖宽度", rivuletWidth, 0.16f, 0.82f);
        meander = EditorGUILayout.Slider("横向摆动", meander, 0f, 1f);
        branching = EditorGUILayout.Slider("分叉率", branching, 0f, 1f);
        breakup = EditorGUILayout.Slider("断流量", breakup, 0f, 0.8f);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("流动", EditorStyles.boldLabel);
        speedVariation = EditorGUILayout.Slider("流速变化", speedVariation, 0f, 1f);
        turbulence = EditorGUILayout.Slider("湍流", turbulence, 0f, 1f);
        topSpread = EditorGUILayout.Slider("顶面向外扩散", topSpread, 0f, 1f);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("输出目录", OutputDirectory, EditorStyles.miniLabel);
    }

    void DrawActions()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("重新生成预览", GUILayout.Height(30f)))
            RebuildPreview();
        if (GUILayout.Button("生成并赋给 LavaFlow.mat", GUILayout.Height(30f)))
            BakeAndAssign();
        if (GUILayout.Button("恢复默认值", GUILayout.Height(30f)))
        {
            ResetDefaults();
            RebuildPreview();
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawPreviews()
    {
        EditorGUILayout.LabelField("预览（流图颜色表示方向，透明区域为镂空）", EditorStyles.boldLabel);
        float width = Mathf.Max(150f, (position.width - 54f) / 3f);
        EditorGUILayout.BeginHorizontal();
        DrawPreview("顶面流图", topPreview, width);
        DrawPreview("侧面细流", sidePreview, width);
        DrawPreview("打包岩浆纹理", patternPreview, width);
        EditorGUILayout.EndHorizontal();
    }

    static void DrawPreview(string label, Texture2D texture, float width)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(width));
        EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(width));
        Rect rect = GUILayoutUtility.GetRect(width, width, GUILayout.Width(width));
        if (texture != null)
            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
        else
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
        EditorGUILayout.EndVertical();
    }

    void ResetDefaults()
    {
        seed = 1749;
        resolution = BakeResolution.Size512;
        rivuletCount = 12;
        rivuletWidth = 0.46f;
        meander = 0.38f;
        branching = 0.34f;
        breakup = 0.24f;
        speedVariation = 0.45f;
        turbulence = 0.31f;
        topSpread = 0.78f;
    }

    Parameters CurrentParameters()
    {
        return new Parameters
        {
            seed = seed,
            rivuletCount = rivuletCount,
            rivuletWidth = rivuletWidth,
            meander = meander,
            branching = branching,
            breakup = breakup,
            speedVariation = speedVariation,
            turbulence = turbulence,
            topSpread = topSpread,
        };
    }

    void RebuildPreview()
    {
        Parameters parameters = CurrentParameters();
        GenerateMaps(PreviewSize, parameters,
            out Color32[] top, out Color32[] side, out Color32[] pattern);
        ReplacePreview(ref topPreview, top, "Lava Top Preview");
        ReplacePreview(ref sidePreview, side, "Lava Side Preview");
        ReplacePreview(ref patternPreview, pattern, "Lava Pattern Preview");
        Repaint();
    }

    static void ReplacePreview(ref Texture2D target, Color32[] pixels, string textureName)
    {
        DestroyPreview(ref target);
        target = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false, true)
        {
            name = textureName,
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        target.SetPixels32(pixels);
        target.Apply(false, false);
    }

    static void DestroyPreview(ref Texture2D texture)
    {
        if (texture != null)
            DestroyImmediate(texture);
        texture = null;
    }

    void BakeAndAssign()
    {
        int size = (int)resolution;
        Parameters parameters = CurrentParameters();

        try
        {
            EditorUtility.DisplayProgressBar("Lava Waterfall Baker", "生成程序化纹理...", 0.15f);
            GenerateMaps(size, parameters,
                out Color32[] top, out Color32[] side, out Color32[] pattern);

            EditorUtility.DisplayProgressBar("Lava Waterfall Baker", "验证纹理数据...", 0.45f);
            ValidateGeneratedData(size, parameters, top, side, pattern);

            EnsureOutputDirectory();
            EditorUtility.DisplayProgressBar("Lava Waterfall Baker", "写入 PNG...", 0.62f);
            WritePng(TopPath, size, top);
            WritePng(SidePath, size, side);
            WritePng(PatternPath, size, pattern);

            EditorUtility.DisplayProgressBar("Lava Waterfall Baker", "配置导入与材质...", 0.82f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string metadata = JsonUtility.ToJson(CreateMetadata(size));
            ConfigureImporter(TopPath, size, true, metadata);
            ConfigureImporter(SidePath, size, true, metadata);
            ConfigureImporter(PatternPath, size, false, metadata);
            AssignAndResetMaterial();

            AssetDatabase.SaveAssets();
            ValidateImportedAssets();
            RebuildPreview();
            Debug.Log($"[LavaFlow] 已生成 {size}x{size} 岩浆瀑布纹理并赋给 {MaterialPath}。" +
                      $" Seed={seed}, 数据校验通过。");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                throw;
            EditorUtility.DisplayDialog("岩浆瀑布生成失败", exception.Message, "确定");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    BakeMetadata CreateMetadata(int size)
    {
        return new BakeMetadata
        {
            version = MetadataVersion,
            seed = seed,
            resolution = size,
            rivuletCount = rivuletCount,
            rivuletWidth = rivuletWidth,
            meander = meander,
            branching = branching,
            breakup = breakup,
            speedVariation = speedVariation,
            turbulence = turbulence,
            topSpread = topSpread,
        };
    }

    static void EnsureOutputDirectory()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Texture"))
            AssetDatabase.CreateFolder("Assets", "Texture");
        if (!AssetDatabase.IsValidFolder(OutputDirectory))
            AssetDatabase.CreateFolder("Assets/Texture", "Lava");
    }

    static void WritePng(string path, int size, Color32[] pixels)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            DestroyImmediate(texture);
        }
    }

    static void ConfigureImporter(string path, int size, bool coverageInAlpha, string metadata)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport |
                                        ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("无法读取纹理导入器: " + path);

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = coverageInAlpha;
        importer.alphaTestReferenceValue = 0.5f;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 1;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = size;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.userData = metadata;
        importer.SaveAndReimport();
    }

    static void AssignAndResetMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Custom/LavaFlow");
        if (material == null)
            throw new InvalidOperationException("找不到材质: " + MaterialPath);
        if (shader == null)
            throw new InvalidOperationException("找不到 Shader: Custom/LavaFlow");

        material.shader = shader;
        PruneStaleProperties(material);
        material.SetTexture("_LavaPattern", AssetDatabase.LoadAssetAtPath<Texture2D>(PatternPath));
        material.SetTexture("_FlowMapTop", AssetDatabase.LoadAssetAtPath<Texture2D>(TopPath));
        material.SetTexture("_FlowMapSide", AssetDatabase.LoadAssetAtPath<Texture2D>(SidePath));

        material.SetColor("_CrustColor", new Color(0.035f, 0.012f, 0.006f, 1f));
        material.SetColor("_HotColor", new Color(1.25f, 0.16f, 0.012f, 1f));
        material.SetColor("_CoreColor", new Color(2.2f, 0.72f, 0.1f, 1f));
        material.SetFloat("_EmissionStrength", 1.25f);
        material.SetFloat("_WorldScale", 0.42f);
        material.SetFloat("_DetailScale", 3.1f);
        material.SetFloat("_TopThreshold", 0.55f);
        material.SetFloat("_TopBlend", 0.08f);
        material.SetFloat("_FlowSpeed", 0.16f);
        material.SetFloat("_DetailSpeed", 1.65f);
        material.SetFloat("_SpeedVariation", 0.45f);
        material.SetFloat("_FlowStrength", 0.34f);
        material.SetFloat("_BreakupAmount", 0.24f);
        material.SetFloat("_CrustAmount", 0.42f);
        material.SetFloat("_EdgeGlow", 1.35f);
        material.SetFloat("_NormalStrength", 0.32f);
        material.SetFloat("_Smoothness", 0.58f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.renderQueue = (int)RenderQueue.Geometry;
        material.enableInstancing = true;
        material.doubleSidedGI = true;
        material.SetOverrideTag("RenderType", "Opaque");
        EditorUtility.SetDirty(material);
    }

    static void PruneStaleProperties(Material material)
    {
        var serializedMaterial = new SerializedObject(material);
        RemoveMissingEntries(serializedMaterial.FindProperty("m_SavedProperties.m_TexEnvs"), material);
        RemoveMissingEntries(serializedMaterial.FindProperty("m_SavedProperties.m_Ints"), material);
        RemoveMissingEntries(serializedMaterial.FindProperty("m_SavedProperties.m_Floats"), material);
        RemoveMissingEntries(serializedMaterial.FindProperty("m_SavedProperties.m_Colors"), material);
        serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
    }

    static void RemoveMissingEntries(SerializedProperty array, Material material)
    {
        if (array == null || !array.isArray)
            return;
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            SerializedProperty name = entry.FindPropertyRelative("first");
            if (name != null && !material.HasProperty(name.stringValue))
                array.DeleteArrayElementAtIndex(i);
        }
    }

    static void ValidateImportedAssets()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || material.shader == null ||
            material.shader.name != "Custom/LavaFlow")
            throw new InvalidOperationException("LavaFlow.mat 没有正确连接 Custom/LavaFlow。");
        if (ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Custom/LavaFlow 存在 Shader 编译错误，请检查 Console。");

        foreach (string property in new[] { "_LavaPattern", "_FlowMapTop", "_FlowMapSide" })
        {
            if (!material.HasProperty(property) || material.GetTexture(property) == null)
                throw new InvalidOperationException("材质纹理未正确连接: " + property);
        }
    }

    static void GenerateMaps(int size, Parameters parameters,
                             out Color32[] top, out Color32[] side,
                             out Color32[] pattern)
    {
        int pixelCount = size * size;
        top = new Color32[pixelCount];
        side = new Color32[pixelCount];
        pattern = new Color32[pixelCount];
        float invSize = 1f / size;
        var boundaryCoverage = new float[size];
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) * invSize;
            boundaryCoverage[x] = EvaluateRivulets(u, 0f, invSize, parameters).coverage;
        }

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) * invSize;
            float v = (y + 0.5f) * invSize;
            int index = y * size + x;

            RivuletSample sideSample = EvaluateRivulets(u, v, invSize, parameters);
            side[index] = PackFlow(sideSample);
            top[index] = PackFlow(EvaluateTop(
                u, v, invSize, parameters, boundaryCoverage));
            pattern[index] = EvaluatePattern(u, v, parameters);
        }
    }

    static RivuletSample EvaluateRivulets(float u, float v, float texelSize,
                                          Parameters parameters)
    {
        float bestCoverage = 0f;
        float nearestDistance = float.MaxValue;
        int selectedStream = 0;
        float selectedCenter = 0f;

        for (int stream = 0; stream < parameters.rivuletCount; stream++)
        {
            float center = StreamCenter(stream, v, parameters);
            float distance = PeriodicDistance(u, center);
            float randomWidth = Mathf.Lerp(0.68f, 1.34f,
                Hash01(stream, parameters.seed + 911));
            float halfWidth = 0.5f * parameters.rivuletWidth /
                              parameters.rivuletCount * randomWidth;

            float edgeNoise = (PeriodicFbm(
                u + stream * 0.071f,
                v,
                5,
                3,
                parameters.seed + 1301 + stream * 17) - 0.5f) *
                parameters.turbulence / parameters.rivuletCount;
            float mainCoverage = 1f - SmoothThreshold(
                halfWidth * 0.52f,
                halfWidth * 1.18f,
                Mathf.Max(0f, distance + edgeNoise));

            float branchPhase = Hash01(stream, parameters.seed + 1709) * Mathf.PI * 2f;
            float branchOffset = Mathf.Sin(
                Mathf.PI * 2f * v * (1 + stream % 3) + branchPhase) *
                0.72f / parameters.rivuletCount;
            float branchCenter = Repeat01(center + branchOffset);
            float branchShape = 1f - SmoothThreshold(
                halfWidth * 0.34f,
                halfWidth * 0.72f,
                PeriodicDistance(u, branchCenter));
            float branchNoise = PeriodicFbm(
                v,
                Hash01(stream, parameters.seed + 2237),
                3,
                3,
                parameters.seed + 2309 + stream * 31);
            float branchGate = SmoothThreshold(
                0.76f - parameters.branching * 0.42f,
                0.9f - parameters.branching * 0.18f,
                branchNoise);

            float breakNoise = PeriodicFbm(
                u * 0.73f + stream * 0.113f,
                v,
                4,
                3,
                parameters.seed + 3011 + stream * 43);
            float breakMask = 1f - parameters.breakup *
                SmoothThreshold(0.62f, 0.92f, breakNoise);
            float coverage = Mathf.Clamp01(
                Mathf.Max(mainCoverage, branchShape * branchGate) * breakMask);

            if (coverage > bestCoverage ||
                (Mathf.Approximately(coverage, bestCoverage) && distance < nearestDistance))
            {
                bestCoverage = coverage;
                nearestDistance = distance;
                selectedStream = stream;
                selectedCenter = center;
            }
        }

        float derivativeStep = Mathf.Max(texelSize, 1f / 1024f);
        float centerBefore = StreamCenter(selectedStream, v - derivativeStep, parameters);
        float centerAfter = StreamCenter(selectedStream, v + derivativeStep, parameters);
        float centerDerivative = PeriodicDelta(centerAfter, centerBefore) /
                                 (2f * derivativeStep);
        float lateralTurbulence = PeriodicFbm(
            selectedCenter,
            v,
            4,
            2,
            parameters.seed + 4013) - 0.5f;
        var flow = new Vector2(
            -centerDerivative + lateralTurbulence * parameters.turbulence * 0.42f,
            -1f).normalized;
        float streamSpeed = Mathf.Lerp(
            0.5f - parameters.speedVariation * 0.5f,
            0.5f + parameters.speedVariation * 0.5f,
            Hash01(selectedStream, parameters.seed + 5011));
        streamSpeed += (PeriodicFbm(u, v, 3, 2, parameters.seed + 5101) - 0.5f) *
                       parameters.speedVariation * 0.24f;

        return new RivuletSample
        {
            flow = flow,
            speed = Mathf.Clamp01(streamSpeed),
            coverage = bestCoverage,
        };
    }

    static float StreamCenter(int stream, float v, Parameters parameters)
    {
        float count = parameters.rivuletCount;
        float baseCenter = (stream + Hash01(stream, parameters.seed + 101)) / count;
        float phaseA = Hash01(stream, parameters.seed + 211) * Mathf.PI * 2f;
        float phaseB = Hash01(stream, parameters.seed + 307) * Mathf.PI * 2f;
        float frequencyA = 1f + stream % 3;
        float frequencyB = 2f + stream % 2;
        float wave = Mathf.Sin(Mathf.PI * 2f * v * frequencyA + phaseA) * 0.62f +
                     Mathf.Sin(Mathf.PI * 2f * v * frequencyB + phaseB) * 0.38f;
        float noise = PeriodicFbm(
            v,
            Hash01(stream, parameters.seed + 419),
            3,
            3,
            parameters.seed + 503 + stream * 13) - 0.5f;
        return Repeat01(baseCenter +
                        wave * parameters.meander / count +
                        noise * parameters.turbulence * 0.8f / count);
    }

    static RivuletSample EvaluateTop(float u, float v, float texelSize,
                                     Parameters parameters, float[] boundaryCoverage)
    {
        float epsilon = Mathf.Max(texelSize, 1f / 1024f);
        float potentialLeft = PeriodicFbm(u - epsilon, v, 3, 3, parameters.seed + 6101);
        float potentialRight = PeriodicFbm(u + epsilon, v, 3, 3, parameters.seed + 6101);
        float potentialDown = PeriodicFbm(u, v - epsilon, 3, 3, parameters.seed + 6101);
        float potentialUp = PeriodicFbm(u, v + epsilon, 3, 3, parameters.seed + 6101);
        var curl = new Vector2(
            potentialUp - potentialDown,
            -(potentialRight - potentialLeft));
        if (curl.sqrMagnitude > 1e-6f)
            curl.Normalize();

        var outward = new Vector2(
            -Mathf.Sin(Mathf.PI * 2f * u),
            -Mathf.Sin(Mathf.PI * 2f * v));
        if (outward.sqrMagnitude > 1e-6f)
            outward.Normalize();
        Vector2 flow = Vector2.Lerp(curl, outward, parameters.topSpread);
        if (flow.sqrMagnitude < 1e-6f)
            flow = Vector2.up;
        else
            flow.Normalize();

        float distanceLeft = u;
        float distanceRight = 1f - u;
        float distanceBottom = v;
        float distanceTop = 1f - v;
        float edgeDistance = Mathf.Min(
            Mathf.Min(distanceLeft, distanceRight),
            Mathf.Min(distanceBottom, distanceTop));
        float edgeCoordinate = Mathf.Min(distanceLeft, distanceRight) <
                               Mathf.Min(distanceBottom, distanceTop) ? v : u;
        float boundary = SamplePeriodicProfile(boundaryCoverage, edgeCoordinate);

        float interiorNoise = PeriodicFbm(u, v, 4, 4, parameters.seed + 7001);
        float interiorCoverage = Mathf.Clamp01(0.48f + (interiorNoise - 0.5f) * 0.72f);
        float edgeBlend = 1f - SmoothThreshold(0.02f, 0.2f, edgeDistance);
        float coverage = Mathf.Lerp(interiorCoverage, boundary, edgeBlend);
        float speed = PeriodicFbm(u, v, 3, 3, parameters.seed + 7103);

        return new RivuletSample
        {
            flow = flow,
            speed = speed,
            coverage = coverage,
        };
    }

    static float SamplePeriodicProfile(float[] values, float coordinate)
    {
        float sample = Repeat01(coordinate) * values.Length - 0.5f;
        int index0 = Mathf.FloorToInt(sample);
        int index1 = index0 + 1;
        float blend = sample - index0;
        return Mathf.Lerp(
            values[Wrap(index0, values.Length)],
            values[Wrap(index1, values.Length)],
            blend);
    }

    static Color32 EvaluatePattern(float u, float v, Parameters parameters)
    {
        float warpU = PeriodicFbm(u, v, 3, 3, parameters.seed + 8009) - 0.5f;
        float warpV = PeriodicFbm(u + 0.37f, v + 0.19f, 3, 3,
                                  parameters.seed + 8101) - 0.5f;
        float warpedU = u + warpU * 0.12f;
        float warpedV = v + warpV * 0.12f;

        float macro = PeriodicFbm(warpedU, warpedV, 3, 4, parameters.seed + 8209);
        float detail = PeriodicFbm(warpedU + 0.21f, warpedV + 0.47f,
                                   7, 3, parameters.seed + 8311);
        float cellular = PeriodicCellular(warpedU, warpedV, 7, parameters.seed + 8423);
        float heat = Mathf.Clamp01(0.18f + macro * 0.62f + detail * 0.2f);
        heat = SmoothThreshold(0.08f, 0.94f, heat);

        float crustNoise = PeriodicFbm(warpedU - 0.31f, warpedV + 0.17f,
                                       5, 4, parameters.seed + 8537);
        float crust = Mathf.Clamp01(
            SmoothThreshold(0.43f, 0.86f, crustNoise) * 0.72f +
            (1f - heat) * 0.38f);

        float filamentNoise = PeriodicFbm(warpedU + 0.11f, warpedV - 0.29f,
                                          8, 3, parameters.seed + 8641);
        float filament = 1f - SmoothThreshold(
            0.035f,
            0.2f,
            Mathf.Abs(filamentNoise - 0.54f));
        filament = Mathf.Clamp01(Mathf.Max(filament, 1f - cellular * 1.8f));
        float height = Mathf.Clamp01(heat * 0.6f + (1f - crust) * 0.24f +
                                     filament * 0.28f);

        return new Color32(
            ToByte(heat),
            ToByte(crust),
            ToByte(height),
            ToByte(filament));
    }

    static Color32 PackFlow(RivuletSample sample)
    {
        return new Color32(
            ToByte(sample.flow.x * 0.5f + 0.5f),
            ToByte(sample.flow.y * 0.5f + 0.5f),
            ToByte(sample.speed),
            ToByte(sample.coverage));
    }

    static void ValidateGeneratedData(int size, Parameters parameters,
                                      Color32[] top, Color32[] side,
                                      Color32[] pattern)
    {
        int expected = size * size;
        if (top.Length != expected || side.Length != expected || pattern.Length != expected)
            throw new InvalidOperationException("生成的纹理尺寸不一致。");

        double coverageSum = 0.0;
        double downwardSum = 0.0;
        byte minCoverage = byte.MaxValue;
        byte maxCoverage = byte.MinValue;
        for (int i = 0; i < side.Length; i++)
        {
            byte coverage = side[i].a;
            coverageSum += coverage / 255.0;
            downwardSum += side[i].g / 255.0 * 2.0 - 1.0;
            minCoverage = Math.Min(minCoverage, coverage);
            maxCoverage = Math.Max(maxCoverage, coverage);
        }

        double averageCoverage = coverageSum / side.Length;
        double averageVerticalFlow = downwardSum / side.Length;
        if (averageCoverage < 0.08 || averageCoverage > 0.82 ||
            minCoverage > 80 || maxCoverage < 180)
            throw new InvalidOperationException(
                $"侧面覆盖率异常: avg={averageCoverage:F3}, min={minCoverage}, max={maxCoverage}");
        if (averageVerticalFlow > -0.35)
            throw new InvalidOperationException(
                $"侧面平均流向没有向下: y={averageVerticalFlow:F3}");

        float seamError = Mathf.Max(
            EdgeError(top, size),
            Mathf.Max(EdgeError(side, size), EdgeError(pattern, size)));
        if (seamError > 0.24f)
            Debug.LogWarning($"[LavaFlow] 纹理边缘变化较大 ({seamError:F3})，请检查当前参数下的平铺效果。");

        // A cheap repeat generation catches accidental non-deterministic APIs
        // without doubling the cost of the full-resolution bake.
        GenerateMaps(32, parameters,
            out Color32[] topCheckA, out Color32[] sideCheckA, out Color32[] patternCheckA);
        GenerateMaps(32, parameters,
            out Color32[] topCheckB, out Color32[] sideCheckB, out Color32[] patternCheckB);
        if (!PixelsEqual(topCheckA, topCheckB) ||
            !PixelsEqual(sideCheckA, sideCheckB) ||
            !PixelsEqual(patternCheckA, patternCheckB))
            throw new InvalidOperationException("相同 Seed 的确定性校验失败。");
    }

    static float EdgeError(Color32[] pixels, int size)
    {
        double error = 0.0;
        int samples = 0;
        for (int i = 0; i < size; i++)
        {
            error += ColorError(pixels[i * size], pixels[i * size + size - 1]);
            error += ColorError(pixels[i], pixels[(size - 1) * size + i]);
            samples += 2;
        }
        return (float)(error / samples);
    }

    static float ColorError(Color32 a, Color32 b)
    {
        return (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) +
                Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a)) / (4f * 255f);
    }

    static bool PixelsEqual(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
                return false;
        }
        return true;
    }

    static float PeriodicFbm(float u, float v, int baseCells, int octaves, int seed)
    {
        float sum = 0f;
        float weight = 0.5f;
        float normalization = 0f;
        int cells = Mathf.Max(1, baseCells);
        for (int octave = 0; octave < octaves; octave++)
        {
            sum += PeriodicValueNoise(u, v, cells, seed + octave * 1013) * weight;
            normalization += weight;
            weight *= 0.5f;
            cells *= 2;
        }
        return sum / Mathf.Max(normalization, 1e-5f);
    }

    static float PeriodicValueNoise(float u, float v, int cells, int seed)
    {
        float x = Repeat01(u) * cells;
        float y = Repeat01(v) * cells;
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float tx = Quintic(x - x0);
        float ty = Quintic(y - y0);
        int x1 = Wrap(x0 + 1, cells);
        int y1 = Wrap(y0 + 1, cells);
        x0 = Wrap(x0, cells);
        y0 = Wrap(y0, cells);

        float a = Mathf.Lerp(Hash01(x0, y0, seed), Hash01(x1, y0, seed), tx);
        float b = Mathf.Lerp(Hash01(x0, y1, seed), Hash01(x1, y1, seed), tx);
        return Mathf.Lerp(a, b, ty);
    }

    static float PeriodicCellular(float u, float v, int cells, int seed)
    {
        float x = Repeat01(u) * cells;
        float y = Repeat01(v) * cells;
        int cellX = Mathf.FloorToInt(x);
        int cellY = Mathf.FloorToInt(y);
        float nearest = float.MaxValue;

        for (int offsetY = -1; offsetY <= 1; offsetY++)
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            int wrappedX = Wrap(cellX + offsetX, cells);
            int wrappedY = Wrap(cellY + offsetY, cells);
            float featureX = cellX + offsetX + Hash01(wrappedX, wrappedY, seed);
            float featureY = cellY + offsetY + Hash01(wrappedX, wrappedY, seed + 173);
            float dx = x - featureX;
            float dy = y - featureY;
            nearest = Mathf.Min(nearest, dx * dx + dy * dy);
        }
        return Mathf.Clamp01(Mathf.Sqrt(nearest));
    }

    static int Wrap(int value, int period)
    {
        int wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }

    static float Repeat01(float value)
    {
        return value - Mathf.Floor(value);
    }

    static float PeriodicDistance(float a, float b)
    {
        float distance = Mathf.Abs(Repeat01(a) - Repeat01(b));
        return Mathf.Min(distance, 1f - distance);
    }

    static float PeriodicDelta(float to, float from)
    {
        float delta = Repeat01(to) - Repeat01(from);
        if (delta > 0.5f) delta -= 1f;
        if (delta < -0.5f) delta += 1f;
        return delta;
    }

    static float Quintic(float value)
    {
        return value * value * value * (value * (value * 6f - 15f) + 10f);
    }

    static float SmoothThreshold(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp01((value - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
        return t * t * (3f - 2f * t);
    }

    static float Hash01(int value, int seed)
    {
        return Hash01(value, value * 193 + 17, seed);
    }

    static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint hash = (uint)x * 0x9e3779b9u ^
                        (uint)y * 0x85ebca6bu ^
                        (uint)seed * 0xc2b2ae35u;
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            hash *= 0x846ca68bu;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777215f;
        }
    }

    static byte ToByte(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }
}
