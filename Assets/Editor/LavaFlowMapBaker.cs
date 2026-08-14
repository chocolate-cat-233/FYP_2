using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the textures used by Custom/LavaFlow:
///   LavaNoise3D.asset   — periodic 3D FBM noise (object-space detail)
///   LavaFlowMapTop.png  — swirl + centre-bloom flow field for the top
///   LavaFlowMapSide.png — meandering downward flow field for the sides
/// and assigns them to Assets/Shader/LavaFlow.mat.
///
/// Flow map encoding: RG = dir * 0.5 + 0.5 (linear data, sRGB off, no
/// mipmaps, Repeat wrap). The top field is a periodic curl perturbation
/// mixed with a radial spread that fades out before the tile border, so
/// the field wraps seamlessly (a pure radial field would flip direction
/// at the tile edge and read as a seam).
/// </summary>
public static class LavaFlowMapBaker
{
    const string OutDir = "Assets/Texture/Lava";
    const string NoisePath = OutDir + "/LavaNoise3D.asset";
    const string TopPath = OutDir + "/LavaFlowMapTop.png";
    const string SidePath = OutDir + "/LavaFlowMapSide.png";
    const string MaterialPath = "Assets/Shader/LavaFlow.mat";
    const string PrefabPath = "Assets/Prefab/Objects2/Lava.prefab";

    const int NoiseDim = 64;    // 64^3 RGBA32 ≈ 1.3 MB with mips
    const int CellSpacing = 4;  // texels per Voronoi cell → 16 cells per
                                // axis; raise for bigger lava cells
    const int FlowSize = 256;

    [MenuItem("Tools/Lava/Bake Flow Assets")]
    public static void BakeAll()
    {
        if (!AssetDatabase.IsValidFolder(OutDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Texture"))
                AssetDatabase.CreateFolder("Assets", "Texture");
            AssetDatabase.CreateFolder("Assets/Texture", "Lava");
        }

        BakeNoise3D();
        BakeFlowMapTop();
        BakeFlowMapSide();
        AssignToMaterial();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("LavaFlow: baked LavaNoise3D + flow maps and assigned them to LavaFlow.mat.");
    }

    [MenuItem("Tools/Lava/Assign Material To Prefab")]
    public static void AssignToPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var lavaMat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (prefab == null || lavaMat == null)
        {
            Debug.LogError("LavaFlow: prefab or material not found for assignment.");
            return;
        }
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterial = lavaMat;
        PrefabUtility.SavePrefabAsset(prefab);
        Debug.Log("LavaFlow: Lava.prefab now uses LavaFlow.mat.");
    }

    // ── 3D noise ──────────────────────────────────────────────────────

    static void BakeNoise3D()
    {
        // Reuse the asset in place when it exists — keeps the guid and
        // the material reference intact across re-bakes.
        var tex = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
        if (tex == null)
        {
            tex = new Texture3D(NoiseDim, NoiseDim, NoiseDim,
                                TextureFormat.RGBA32, true);
            AssetDatabase.CreateAsset(tex, NoisePath);
        }

        // Pass 1: raw fields + their min/max for normalization.
        //   R — periodic 3D Voronoi F1 (distance to the nearest cell
        //       feature point): cell interiors bright, seams dark.
        //   G — periodic Voronoi border field: inverted F2−F1, 1 on the
        //       cell seams → a connected crack network for the shader's
        //       crack layer.
        int voxels = NoiseDim * NoiseDim * NoiseDim;
        var rawR = new float[voxels];
        var rawG = new float[voxels];
        float minR = float.MaxValue, maxR = float.MinValue;
        float minG = float.MaxValue, maxG = float.MinValue;
        for (int z = 0; z < NoiseDim; z++)
        for (int y = 0; y < NoiseDim; y++)
        for (int x = 0; x < NoiseDim; x++)
        {
            int i = z * NoiseDim * NoiseDim + y * NoiseDim + x;
            Voronoi(x, y, z, out float f1, out float edge);
            rawR[i] = f1;
            rawG[i] = edge;
            minR = Mathf.Min(minR, rawR[i]); maxR = Mathf.Max(maxR, rawR[i]);
            minG = Mathf.Min(minG, rawG[i]); maxG = Mathf.Max(maxG, rawG[i]);
        }

        // Pass 2: normalize to [0,1] and shape the distributions.
        var pixels = new Color32[voxels];
        for (int z = 0; z < NoiseDim; z++)
        for (int y = 0; y < NoiseDim; y++)
        for (int x = 0; x < NoiseDim; x++)
        {
            int i = z * NoiseDim * NoiseDim + y * NoiseDim + x;
            // Steep S-curve around the middle of the distance field:
            // cell interiors burn, seams drop to dark crust.
            float r = Mathf.SmoothStep(0.30f, 0.70f,
                Mathf.InverseLerp(minR, maxR, rawR[i]));
            // Cell seams = 1, cell centres = 0 (shader thresholds high).
            float g = 1f - Mathf.InverseLerp(minG, maxG, rawG[i]);
            pixels[i] = new Color32(
                (byte)Mathf.RoundToInt(255f * r),
                (byte)Mathf.RoundToInt(255f * g),
                0, 255);
        }
        tex.SetPixels32(pixels);
        tex.Apply(true, false);           // generate the mip chain

        // RGBA32 is a gamma format → sampled raw, no sRGB conversion,
        // matching the linear flow maps.
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 0;
        EditorUtility.SetDirty(tex);
    }

    // Periodic Perlin (gradient) noise FBM, 3 octaves, period NoiseDim
    // along every axis.
    static float PerlinFbm(float x, float y, float z)
    {
        const int Octaves = 3;
        float sum = 0f, amp = 0.5f;
        int period = NoiseDim;
        for (int o = 0; o < Octaves; o++)
        {
            float f = 1 << o;
            sum += amp * Perlin(x * f, y * f, z * f, period);
            amp *= 0.5f;
            period >>= 1;
        }
        return sum;
    }

    // 12 edge gradients (classic Perlin set).
    static readonly Vector3[] Grads =
    {
        new Vector3(1, 1, 0), new Vector3(-1, 1, 0), new Vector3(1, -1, 0),
        new Vector3(-1, -1, 0), new Vector3(1, 0, 1), new Vector3(-1, 0, 1),
        new Vector3(1, 0, -1), new Vector3(-1, 0, -1), new Vector3(0, 1, 1),
        new Vector3(0, -1, 1), new Vector3(0, 1, -1), new Vector3(0, -1, -1),
    };

    static float Perlin(float x, float y, float z, int period)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        // Lattice indices wrap at the period → seamless on all axes.
        xi = ((xi % period) + period) % period;
        yi = ((yi % period) + period) % period;
        zi = ((zi % period) + period) % period;
        int xi1 = (xi + 1) % period, yi1 = (yi + 1) % period, zi1 = (zi + 1) % period;

        float n000 = GradDot(xi, yi, zi, xf, yf, zf);
        float n100 = GradDot(xi1, yi, zi, xf - 1f, yf, zf);
        float n010 = GradDot(xi, yi1, zi, xf, yf - 1f, zf);
        float n110 = GradDot(xi1, yi1, zi, xf - 1f, yf - 1f, zf);
        float n001 = GradDot(xi, yi, zi1, xf, yf, zf - 1f);
        float n101 = GradDot(xi1, yi, zi1, xf - 1f, yf, zf - 1f);
        float n011 = GradDot(xi, yi1, zi1, xf, yf - 1f, zf - 1f);
        float n111 = GradDot(xi1, yi1, zi1, xf - 1f, yf - 1f, zf - 1f);

        // Quintic fade — noticeably smoother than the Hermite curve.
        float u = xf * xf * xf * (xf * (xf * 6f - 15f) + 10f);
        float v = yf * yf * yf * (yf * (yf * 6f - 15f) + 10f);
        float w = zf * zf * zf * (zf * (zf * 6f - 15f) + 10f);

        return Mathf.Lerp(
            Mathf.Lerp(Mathf.Lerp(n000, n100, u), Mathf.Lerp(n010, n110, u), v),
            Mathf.Lerp(Mathf.Lerp(n001, n101, u), Mathf.Lerp(n011, n111, u), v),
            w);
    }

    static float GradDot(int x, int y, int z, float dx, float dy, float dz)
    {
        var g = Grads[(int)(Hash(x, y, z) * Grads.Length) % Grads.Length];
        return g.x * dx + g.y * dy + g.z * dz;
    }

    // Periodic 3D Voronoi with one feature point per CellSpacing-texel
    // cell — the coarser the lattice, the bigger the lava cells. One
    // neighbour pass yields both fields:
    //   f1   — distance to the nearest feature (0 at the feature points,
    //          largest at the cell corners): the classic cellular field.
    //   edge — F2 − F1 (0 on the cell seams, largest at cell centres):
    //          the classic crack metric.
    static void Voronoi(float x, float y, float z, out float f1, out float edge)
    {
        int cells = NoiseDim / CellSpacing;
        int xi = Mathf.FloorToInt(x / CellSpacing);
        int yi = Mathf.FloorToInt(y / CellSpacing);
        int zi = Mathf.FloorToInt(z / CellSpacing);
        float d1 = float.MaxValue, d2 = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ((xi + dx) % cells + cells) % cells;
            int cy = ((yi + dy) % cells + cells) % cells;
            int cz = ((zi + dz) % cells + cells) % cells;
            // Feature point inside the cell, in texel units.
            float px = (cx + Hash(cx, cy, cz)) * CellSpacing;
            float py = (cy + Hash(cx + 13, cy + 29, cz + 7)) * CellSpacing;
            float pz = (cz + Hash(cx + 71, cy + 43, cz + 31)) * CellSpacing;
            float dx2 = x - px, dy2 = y - py, dz2 = z - pz;
            float d = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;
            if (d < d1) { d2 = d1; d1 = d; }
            else if (d < d2) { d2 = d; }
        }
        f1 = Mathf.Sqrt(d1);
        edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
    }

    static float Hash(int x, int y, int z)
    {
        uint n = (uint)x * 0x1b873593u ^ (uint)y * 0x85ebca6bu ^ (uint)z * 0xc2b2ae35u;
        n ^= n >> 15;
        n *= 0x2c1b3c6du;
        n ^= n >> 12;
        return (n & 0xffffffu) / (float)0xffffffu;
    }

    // ── Top flow map ──────────────────────────────────────────────────

    static void BakeFlowMapTop()
    {
        // Periodic scalar potential; its curl is divergence-free and
        // wraps. Two periods across the map.
        var potential = new float[FlowSize * FlowSize];
        for (int j = 0; j < FlowSize; j++)
        for (int i = 0; i < FlowSize; i++)
            potential[j * FlowSize + i] = PerlinFbm(i * 0.5f, j * 0.5f, 0f);

        var pixels = new Color32[FlowSize * FlowSize];
        for (int j = 0; j < FlowSize; j++)
        for (int i = 0; i < FlowSize; i++)
        {
            // Curl of the potential (wrap-aware central differences).
            float dPdx = potential[j * FlowSize + (i + 1) % FlowSize] -
                         potential[j * FlowSize + (i - 1 + FlowSize) % FlowSize];
            float dPdy = potential[((j + 1) % FlowSize) * FlowSize + i] -
                         potential[((j - 1 + FlowSize) % FlowSize) * FlowSize + i];
            var curl = new Vector2(dPdy, -dPdx);

            // Radial spread from the tile centre, fading to zero before
            // the border so the field stays seamless across tiles. In
            // the shader the tile centre sits at the pool centre
            // (object space), giving the outward "晕开" drift.
            float cx = (i + 0.5f) / FlowSize - 0.5f;
            float cy = (j + 0.5f) / FlowSize - 0.5f;
            float dist = Mathf.Sqrt(cx * cx + cy * cy);
            float bloom = 1f - Mathf.SmoothStep(0.08f, 0.40f, dist);
            var radial = dist > 1e-4f
                ? new Vector2(cx / dist, cy / dist)
                : Vector2.zero;

            var dir = curl * 0.8f + radial * (bloom * 1.3f);
            float len = dir.magnitude;
            if (len > 1e-4f) dir /= len; else dir = Vector2.zero;

            pixels[j * FlowSize + i] = Encode(dir.x, dir.y);
        }
        WritePng(pixels, TopPath);
    }

    // ── Side flow map ─────────────────────────────────────────────────

    static void BakeFlowMapSide()
    {
        var pixels = new Color32[FlowSize * FlowSize];
        for (int j = 0; j < FlowSize; j++)
        for (int i = 0; i < FlowSize; i++)
        {
            // u = along-wall coordinate, v = height (0 bottom → 1 top).
            float u = (i + 0.5f) / FlowSize;
            float v = (j + 0.5f) / FlowSize;

            // Mostly straight down with a gentle sinusoidal meander.
            float meander = Mathf.Sin(2f * Mathf.PI *
                (u * 3f + 0.6f * Mathf.Sin(2f * Mathf.PI * v * 1.7f))) * 0.30f;
            float down = -1f + 0.22f * Mathf.Sin(2f * Mathf.PI * u * 2.5f);
            var dir = new Vector2(meander, down).normalized;

            pixels[j * FlowSize + i] = Encode(dir.x, dir.y);
        }
        WritePng(pixels, SidePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    static Color32 Encode(float dirX, float dirY)
    {
        return new Color32(
            (byte)Mathf.RoundToInt(255f * (0.5f + 0.5f * dirX)),
            (byte)Mathf.RoundToInt(255f * (0.5f + 0.5f * dirY)),
            0, 255);
    }

    static void WritePng(Color32[] pixels, string path)
    {
        var tex = new Texture2D(FlowSize, FlowSize, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);       // temporary object only

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;       // linear direction data
        importer.mipmapEnabled = false;     // mips would blur directions
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = false;
        importer.SaveAndReimport();
    }

    static void AssignToMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Debug.LogError("LavaFlow: material not found at " + MaterialPath);
            return;
        }
        mat.SetTexture("_LavaNoise",
            AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath));
        mat.SetTexture("_FlowMapTop",
            AssetDatabase.LoadAssetAtPath<Texture2D>(TopPath));
        mat.SetTexture("_FlowMapSide",
            AssetDatabase.LoadAssetAtPath<Texture2D>(SidePath));
        EditorUtility.SetDirty(mat);
    }
}
