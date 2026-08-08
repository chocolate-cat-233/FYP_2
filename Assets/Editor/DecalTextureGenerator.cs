// ============================================================================
// 程序化贴花生成器 DecalTextureGenerator
// ----------------------------------------------------------------------------
// 用噪声算法批量生成裂纹/水渍/污渍/苔藓/磨损/砖缝贴花，导出 PNG 贴图 +
// URP Decal 材质，供场景中的 Decal Projector 使用。
//
// 使用方式:
//   1. Unity 菜单 Tools > Decal Textures > Generator 打开窗口
//   2. 设置种子/尺寸/强度，点"生成全部"（或单项生成）
//   3. 生成完毕后在场景里创建 Decal Projector (GameObject > 3D Object >
//      Decal Projector)，把材质拖上去，摆到墙面/地面即可
//   4. "在场景中摆放样板"可一键把 9 种贴花铺到当前场景地板上预览
//
// 特点:
//   - 固定种子 → 结果可复现，适合 git 工作流
//   - 纯 C# 哈希噪声，不依赖外部贴图资源，无版权问题
//   - 裂纹/磨损同时生成配套法线贴图，投影后有凹凸立体感
//   - 材质从 URP 内置 Decal shadergraph 编译产物创建，管线无关
// ============================================================================

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DecalTextureGenerator : EditorWindow
{
    const string OutDir = "Assets/Texture/Decals";

    int seed = 20260808;
    int size = 1024;
    float intensity = 1f;

    struct DecalData
    {
        public Color32[] albedo;
        public float[] height;   // 高度场，用于生成法线贴图
    }

    [MenuItem("Tools/Decal Textures/Generator")]
    static void Open() { GetWindow<DecalTextureGenerator>("程序化贴花生成器"); }

    // ============================================================================
    // 窗口
    // ============================================================================
    void OnGUI()
    {
        GUILayout.Space(4);
        seed = EditorGUILayout.IntField("种子 (固定可复现)", seed);
        size = EditorGUILayout.IntPopup("贴图尺寸", size, new[] { "512", "1024", "2048" }, new[] { 512, 1024, 2048 });
        intensity = EditorGUILayout.Slider("全局强度", intensity, 0.2f, 2f);
        EditorGUILayout.HelpBox(
            "输出到 Assets/Texture/Decals/ (贴图 + URP Decal 材质)\n" +
            "场景中使用: GameObject > 3D Object > Decal Projector，把材质拖上去即可。",
            MessageType.Info);

        GUILayout.Space(6);
        if (GUILayout.Button("生成全部 (9 种贴花)")) GenerateAll();

        GUILayout.Space(6);
        EditorGUILayout.LabelField("单项生成", EditorStyles.boldLabel);
        if (GUILayout.Button("长裂纹 (+法线)")) Generate("decal_crack", () => GenCrack(size, seed, intensity), true);
        if (GUILayout.Button("蛛网裂纹 (+法线)")) Generate("decal_spiderweb", () => GenSpiderweb(size, seed, intensity), true);
        if (GUILayout.Button("垂直水渍")) Generate("decal_waterstain", () => GenWaterStain(size, seed, intensity), false);
        if (GUILayout.Button("大面积污渍")) Generate("decal_stain_large", () => GenStainLarge(size, seed, intensity), false);
        if (GUILayout.Button("小片污渍")) Generate("decal_stain_small", () => GenStainSmall(size, seed, intensity), false);
        if (GUILayout.Button("成片苔藓")) Generate("decal_moss_patch", () => GenMossPatch(size, seed, intensity), false);
        if (GUILayout.Button("苔藓团")) Generate("decal_moss_cluster", () => GenMossCluster(size, seed, intensity), false);
        if (GUILayout.Button("边缘磨损 (+法线)")) Generate("decal_wear", () => GenWear(size, seed, intensity), true);
        if (GUILayout.Button("砖缝脏线")) Generate("decal_seam", () => GenSeam(size, seed, intensity), false);

        GUILayout.Space(6);
        if (GUILayout.Button("在场景中摆放样板 (DecalSamples)")) PlaceSamples();
    }

    // ============================================================================
    // 生成入口: 算像素 → 存 PNG → 设导入参数 → 建材质
    // ============================================================================
    void GenerateAll()
    {
        if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
        Generate("decal_crack", () => GenCrack(size, seed, intensity), true);
        Generate("decal_spiderweb", () => GenSpiderweb(size, seed, intensity), true);
        Generate("decal_waterstain", () => GenWaterStain(size, seed, intensity), false);
        Generate("decal_stain_large", () => GenStainLarge(size, seed, intensity), false);
        Generate("decal_stain_small", () => GenStainSmall(size, seed, intensity), false);
        Generate("decal_moss_patch", () => GenMossPatch(size, seed, intensity), false);
        Generate("decal_moss_cluster", () => GenMossCluster(size, seed, intensity), false);
        Generate("decal_wear", () => GenWear(size, seed, intensity), true);
        Generate("decal_seam", () => GenSeam(size, seed, intensity), false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DecalGenerator] 全部生成完毕 → {OutDir} (种子 {seed})");
    }

    void Generate(string name, Func<DecalData> gen, bool withNormal)
    {
        try
        {
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
            var data = gen();

            string albPath = OutDir + "/" + name + ".png";
            SaveAndImport(data.albedo, size, albPath, false, true);

            string normalPath = null;
            if (withNormal && data.height != null)
            {
                normalPath = OutDir + "/" + name + "_normal.png";
                SaveAndImport(HeightToNormal(data.height, size, 1.8f), size, normalPath, true, false);
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albPath);
            var normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;
            CreateDecalMaterial(name, albedo, normal);
            Debug.Log($"[DecalGenerator] 生成完成: {name}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DecalGenerator] 生成失败: {name}\n{e}");
        }
    }

    static void SaveAndImport(Color32[] px, int size, string path, bool isNormal, bool alphaTransparent)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !isNormal;              // 法线贴图必须线性
        imp.alphaIsTransparency = alphaTransparent;
        imp.wrapMode = TextureWrapMode.Clamp;     // 贴花禁止 Repeat
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
    }

    static void CreateDecalMaterial(string name, Texture2D albedo, Texture2D normal)
    {
        var shader = GetDecalShader();
        if (shader == null)
        {
            Debug.LogWarning("[DecalGenerator] 找不到 URP Decal shader，跳过材质创建");
            return;
        }
        string matPath = OutDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(shader);
        mat.name = name;
        if (albedo != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
        if (albedo != null && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (normal != null && mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", normal);
        if (normal != null && mat.HasProperty("_NormalBlend")) mat.SetFloat("_NormalBlend", 1f);
        if (isNew) AssetDatabase.CreateAsset(mat, matPath);
        else EditorUtility.SetDirty(mat);
    }

    static Shader GetDecalShader()
    {
        // 官方路径: URP 渲染管线的内置 Decal 材质 (Decal.shadergraph 编译产物)
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp
            && rp.decalMaterial != null
            && rp.decalMaterial.shader != null)
            return rp.decalMaterial.shader;
        return Shader.Find("Shader Graphs/Decal"); // 兜底
    }

    // ============================================================================
    // 噪声库 (全部确定性，依赖固定 seed)
    // ============================================================================
    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h * (1f / 4294967295f);
        }
    }

    static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        // 五次平滑插值，避免网格痕迹
        fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        float a = Mathf.Lerp(Hash(xi, yi, seed), Hash(xi + 1, yi, seed), fx);
        float b = Mathf.Lerp(Hash(xi, yi + 1, seed), Hash(xi + 1, yi + 1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    /// 分形叠加噪声 (fBm)，输出约 [0,1]
    static float Fbm(float x, float y, int seed, int octaves, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + i * 131);
            freq *= lacunarity;
            amp *= gain;
        }
        return sum;
    }

    /// 细胞噪声: 输出最近/次近距离（欧氏距离）
    static void Voronoi(float x, float y, int seed, float jitter, out float d1, out float d2)
    {
        int gx = Mathf.FloorToInt(x), gy = Mathf.FloorToInt(y);
        float fx = x - gx, fy = y - gy;
        d1 = 1e9f;
        d2 = 1e9f;
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            float px = ox + (Hash(gx + ox, gy + oy, seed) * 2f - 1f) * jitter;
            float py = oy + (Hash(gx + ox, gy + oy, seed + 77) * 2f - 1f) * jitter;
            float dx = px - fx, dy = py - fy;
            float d = dx * dx + dy * dy;
            if (d < d1) { d2 = d1; d1 = d; }
            else if (d < d2) d2 = d;
        }
        d1 = Mathf.Sqrt(d1);
        d2 = Mathf.Sqrt(d2);
    }

    /// Voronoi 边缘强度: 两距离差越小（越靠近边缘）越亮
    static float EdgeStrength(float d1, float d2, float band)
    {
        return Mathf.Clamp01(1f - (d2 - d1) / band);
    }

    /// 中心径向衰减，让贴花四周淡出 (inner~outer 归一化半径)
    static float RadialFalloff(float u, float v, float inner, float outer)
    {
        float dx = u - 0.5f, dy = v - 0.5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy) * 1.4142f;
        return 1f - Mathf.SmoothStep(inner, outer, r);
    }

    /// 通用像素循环: fn(u,v) → (r, g, b, alpha)
    static DecalData Compute(int size, Func<float, float, Vector4> fn)
    {
        var px = new Color32[size * size];
        var h = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            var c = fn(u, v);
            float a = Mathf.Clamp01(c.w);
            px[x + y * size] = new Color32(
                (byte)(Mathf.Clamp01(c.x) * 255f),
                (byte)(Mathf.Clamp01(c.y) * 255f),
                (byte)(Mathf.Clamp01(c.z) * 255f),
                (byte)(a * 255f));
            h[x + y * size] = a;
        }
        return new DecalData { albedo = px, height = h };
    }

    /// 高度场 → 切线空间法线贴图
    static Color32[] HeightToNormal(float[] h, int size, float strength)
    {
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(size - 1, x + 1);
            int y0 = Mathf.Max(0, y - 1), y1 = Mathf.Min(size - 1, y + 1);
            float dx = (h[x1 + y * size] - h[x0 + y * size]) * strength;
            float dy = (h[x + y1 * size] - h[x + y0 * size]) * strength;
            var n = new Vector3(-dx, -dy, 1f).normalized;
            px[x + y * size] = new Color32(
                (byte)((n.x * 0.5f + 0.5f) * 255f),
                (byte)((n.y * 0.5f + 0.5f) * 255f),
                (byte)((n.z * 0.5f + 0.5f) * 255f),
                255);
        }
        return px;
    }

    // ============================================================================
    // 贴花: 长裂纹
    // ============================================================================
    static DecalData GenCrack(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            Voronoi(u * 7f, v * 7f, seed, 0.65f, out float d1, out float d2);
            float edge = Mathf.Pow(EdgeStrength(d1, d2, 0.13f), 2f);
            Voronoi(u * 19f, v * 19f, seed + 1, 0.8f, out float e1, out float e2);
            float fine = Mathf.Pow(EdgeStrength(e1, e2, 0.055f), 3f) * 0.5f;
            float crack = Mathf.Max(edge, fine);
            // 断裂: 噪声阈值切断部分裂纹，避免整条贯穿
            float brk = Mathf.SmoothStep(0.45f, 0.78f, Fbm(u * 8f + 7f, v * 8f, seed + 2, 3));
            // 粗细变化
            float thick = 0.3f + 0.7f * Fbm(u * 15f, v * 15f + 3f, seed + 3, 2);
            float a = crack * brk * thick * RadialFalloff(u, v, 0.35f, 0.8f) * intensity;
            float n = Fbm(u * 22f, v * 22f, seed + 4, 2) * 0.35f;
            float c = 0.07f + n;
            return new Vector4(c, c, c * 0.92f, a);
        });
    }

    // ============================================================================
    // 贴花: 蛛网裂纹 (中心密集，向四周扩散)
    // ============================================================================
    static DecalData GenSpiderweb(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            Voronoi(u * 5f, v * 5f, seed, 0.6f, out float d1, out float d2);
            float edge = Mathf.Pow(EdgeStrength(d1, d2, 0.11f), 2.2f);
            Voronoi(u * 14f, v * 14f, seed + 1, 0.75f, out float e1, out float e2);
            float fine = Mathf.Pow(EdgeStrength(e1, e2, 0.045f), 3f) * 0.6f;
            float crack = Mathf.Max(edge, fine);
            // 中心权重: 越靠中心裂纹越密
            float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 1.4142f;
            float centerW = 1f - Mathf.SmoothStep(0.3f, 0.75f, r);
            float brk = Mathf.SmoothStep(0.42f, 0.72f, Fbm(u * 9f, v * 9f, seed + 2, 3));
            float a = crack * brk * (0.45f + 0.55f * centerW) * RadialFalloff(u, v, 0.4f, 0.85f) * intensity;
            float n = Fbm(u * 22f, v * 22f, seed + 3, 2) * 0.35f;
            float c = 0.07f + n;
            return new Vector4(c, c, c * 0.92f, a);
        });
    }

    // ============================================================================
    // 贴花: 垂直水渍 (墙面雨水痕)
    // ============================================================================
    static DecalData GenWaterStain(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            // 水平扰动 + 垂直蜿蜒，采样频率 y 方向低 → 竖直长条
            float u2 = u * 1.1f + (Fbm(v * 3f, u * 3f, seed, 2) - 0.5f) * 0.12f;
            float v2 = v + (Fbm(u * 4f, v * 4f, seed + 1, 2) - 0.5f) * 0.16f;
            float s1 = Fbm(u2 * 13f, v2 * 2.8f, seed + 2, 4);
            float s2 = Fbm(u2 * 20f + 9f, v2 * 4.2f + 2f, seed + 3, 4);
            float a = (Mathf.SmoothStep(0.52f, 0.7f, s1) * 0.72f + Mathf.SmoothStep(0.55f, 0.74f, s2) * 0.5f)
                    * (0.55f + 0.45f * Fbm(u * 6f, v * 6f, seed + 4, 2))
                    * RadialFalloff(u, v, 0.3f, 0.75f) * intensity;
            a = Mathf.Min(a, 0.5f);
            float n = Fbm(u * 18f, v * 18f, seed + 5, 2) * 0.3f;
            return new Vector4(0.16f + n * 0.4f, 0.145f + n * 0.35f, 0.13f + n * 0.3f, a);
        });
    }

    // ============================================================================
    // 贴花: 大面积污渍
    // ============================================================================
    static DecalData GenStainLarge(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            float m = Fbm(u * 2.5f, v * 2.5f, seed, 3);
            float mask = Mathf.SmoothStep(0.5f, 0.62f, m);
            float det = Fbm(u * 12f, v * 12f, seed + 1, 2);
            float a = mask * (0.5f + 0.5f * det) * RadialFalloff(u, v, 0.25f, 0.7f) * intensity;
            a = Mathf.Min(a, 0.55f);
            float n = Fbm(u * 9f, v * 9f, seed + 2, 2) * 0.06f;
            return new Vector4(0.12f + n, 0.10f + n, 0.085f + n, a);
        });
    }

    // ============================================================================
    // 贴花: 小片污渍 (细碎斑点)
    // ============================================================================
    static DecalData GenStainSmall(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            float m = Fbm(u * 5f, v * 5f, seed, 3) * 0.72f + Fbm(u * 20f, v * 20f, seed + 1, 2) * 0.28f;
            float mask = Mathf.SmoothStep(0.52f, 0.62f, m);
            float a = mask * (0.35f + 0.65f * Fbm(u * 14f, v * 14f, seed + 2, 2))
                    * RadialFalloff(u, v, 0.3f, 0.8f) * intensity * 0.85f;
            a = Mathf.Min(a, 0.5f);
            float n = Fbm(u * 9f, v * 9f, seed + 3, 2) * 0.06f;
            return new Vector4(0.12f + n, 0.10f + n, 0.085f + n, a);
        });
    }

    // ============================================================================
    // 贴花: 成片苔藓
    // ============================================================================
    static DecalData GenMossPatch(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            float m = Fbm(u * 2.2f, v * 2.2f, seed, 3);
            float mask = Mathf.SmoothStep(0.47f, 0.6f, m);
            float sp = Mathf.SmoothStep(0.55f, 0.8f, Fbm(u * 14f, v * 14f, seed + 1, 3));
            float g = 0.4f + 0.6f * Fbm(u * 8f, v * 8f, seed + 2, 2);
            float a = mask * sp * (0.35f + 0.65f * g) * RadialFalloff(u, v, 0.25f, 0.7f) * intensity;
            a = Mathf.Min(a, 0.85f);
            float r = 0.10f * g + 0.03f, gc = 0.26f * g + 0.05f, b = 0.11f * g + 0.03f;
            return new Vector4(r, gc, b, a);
        });
    }

    // ============================================================================
    // 贴花: 苔藓团 (Voronoi 细胞团块)
    // ============================================================================
    static DecalData GenMossCluster(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            Voronoi(u * 3f, v * 3f, seed, 0.5f, out float d1, out float d2);
            float rr = d1 * 3f;
            float blob = 1f - Mathf.SmoothStep(0.3f, 0.85f, rr);
            float ring = Mathf.SmoothStep(0.42f, 0.7f, Fbm(u * 5f, v * 5f, seed + 1, 3));
            float sp = Mathf.SmoothStep(0.5f, 0.78f, Fbm(u * 17f, v * 17f, seed + 2, 3));
            float g = 0.5f + 0.5f * Fbm(u * 9f, v * 9f, seed + 3, 2);
            float a = blob * ring * sp * (0.3f + 0.7f * g) * RadialFalloff(u, v, 0.3f, 0.8f) * intensity;
            a = Mathf.Min(a, 0.9f);
            float r = 0.10f * g + 0.03f, gc = 0.26f * g + 0.05f, b = 0.11f * g + 0.03f;
            return new Vector4(r, gc, b, a);
        });
    }

    // ============================================================================
    // 贴花: 边缘磨损 (沿贴花四周，磨亮的浅色带)
    // ============================================================================
    static DecalData GenWear(int size, int seed, float intensity)
    {
        return Compute(size, (u, v) =>
        {
            float dx = Mathf.Min(u, 1f - u), dy = Mathf.Min(v, 1f - v);
            float d = Mathf.Min(dx, dy);
            float band = 1f - Mathf.SmoothStep(0f, 0.3f, d);
            float patch = Mathf.SmoothStep(0.4f, 0.7f, Fbm(u * 10f, v * 10f, seed, 3));
            float rough = 0.45f + 0.55f * Fbm(u * 21f, v * 21f, seed + 1, 2);
            float a = band * patch * rough * RadialFalloff(u, v, 0.2f, 0.65f) * intensity * 0.6f;
            a = Mathf.Min(a, 0.5f);
            float n = Fbm(u * 26f, v * 26f, seed + 2, 2);
            float c = 0.66f + 0.18f * n;
            return new Vector4(c, c * 0.97f, c * 0.92f, a);
        });
    }

    // ============================================================================
    // 贴花: 砖缝脏线 (4×5 板格 + 宽幅脏污)
    // ============================================================================
    static DecalData GenSeam(int size, int seed, float intensity)
    {
        const int rows = 4, cols = 5;
        return Compute(size, (u, v) =>
        {
            float fy = v * rows - Mathf.Floor(v * rows);
            float fx = u * cols - Mathf.Floor(u * cols);
            // 缝线抖动
            float wobY = (Fbm(u * 5f, v * 5f, seed, 2) - 0.5f) * 0.06f;
            float wobX = (Fbm(u * 6f + 3f, v * 6f + 1f, seed + 1, 2) - 0.5f) * 0.06f;
            float distY = Mathf.Min(fy, 1f - fy) + wobY;
            float distX = Mathf.Min(fx, 1f - fx) + wobX;
            // 缝线粗细随噪声变化
            float w = 0.014f + 0.012f * Fbm(u * 16f, v * 16f, seed + 2, 2);
            float line = Mathf.Max(1f - Mathf.SmoothStep(0f, w, distY), 1f - Mathf.SmoothStep(0f, w, distX));
            // 缝边宽幅脏污
            float grime = (1f - Mathf.SmoothStep(0f, w * 5f, distY)) * 0.25f;
            float a = Mathf.Clamp01(line * 0.8f + grime) * RadialFalloff(u, v, 0.3f, 0.8f) * intensity;
            a = Mathf.Min(a, 0.75f);
            float n = Fbm(u * 24f, v * 24f, seed + 3, 2) * 0.2f;
            float c = 0.09f + n;
            return new Vector4(c, c, c * 0.95f, a);
        });
    }

    // ============================================================================
    // 样板摆放: 在当前场景铺 3×3 网格的 Decal Projector (投影方向为本地 +Z)
    // ============================================================================
    void PlaceSamples()
    {
        string[] names =
        {
            "decal_crack", "decal_spiderweb", "decal_waterstain",
            "decal_stain_large", "decal_stain_small", "decal_moss_patch",
            "decal_moss_cluster", "decal_wear", "decal_seam",
        };
        var root = new GameObject("DecalSamples");
        for (int i = 0; i < names.Length; i++)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutDir + "/" + names[i] + ".mat");
            if (mat == null) continue;
            var go = new GameObject(names[i]);
            go.transform.SetParent(root.transform);
            // X=90° 使本地 +Z 指向下方，投影到地面
            go.transform.position = new Vector3((i % 3) * 3.4f - 3.4f, 0.75f, (i / 3) * 3.0f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(3f, 2.6f, 1f); // 投影盒 3×2.6×1
            var dp = go.AddComponent<DecalProjector>();
            dp.material = mat;
        }
        Selection.activeGameObject = root;
        Debug.Log("[DecalGenerator] 样板已摆放 (投影方向为本地 +Z, 面朝下)。\n" +
                  "提示: 若贴花出现在天花板/墙上，把 DecalSamples 下对象的旋转 X 由 90 改为 -90。");
    }
}
