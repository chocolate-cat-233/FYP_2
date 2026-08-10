// ============================================================================
// 爆炸粒子效果工厂 ExplosionParticleFactory
// ----------------------------------------------------------------------------
// 程序化生成卡通冲击风爆炸粒子：软粒子贴图 + URP 粒子材质 + 分层粒子预制体，
// 并接线到 Bomb.prefab 的 BombProjectile.explosionEffectPrefab。
//
// 使用方式:
//   1. Unity 菜单 Tools > Explosion FX > 生成爆炸效果 (贴图+材质+预制体)
//   2. Tools > Explosion FX > 接线到 Bomb.prefab
//   3. 打开关卡投掷炸弹验证；改种子/参数后重新执行第 1 步即可覆盖
//
// 特点:
//   - 固定种子，可复现，适合 git 工作流
//   - 零美术资源：全部贴图由哈希噪声程序化生成
//   - 材质用 URP 官方 Particles/Unlit，混合由 _SrcBlend/_DstBlend 驱动
//     (URP 17 无 _ALPHABLEND_ON/_ADDITIVE_ON 关键字)
//   - 只改 Bomb.prefab，不碰 ExplosiveBarrel 和任何运行时脚本
// ============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SuperFinalFuse.BombThrowing;

public class ExplosionParticleFactory
{
    const string ParticleTexDir = "Assets/Texture/Particles";
    const string FxMaterialDir = "Assets/Materials";
    const string EffectPrefabPath = "Assets/Prefab/Effects/BombExplosion.prefab";
    const string QuadMeshPath = "Assets/Prefab/Effects/FX_QuadXZ.asset";
    const string BombPrefabPath = "Assets/Prefab/Objects2/Bomb.prefab";
    const int Seed = 20260809;

    static readonly Color FlashWhite = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    static readonly Color FlashYellow = new Color32(0xFF, 0xE3, 0x4D, 0xFF);
    static readonly Color FireYellow = new Color32(0xFF, 0xD0, 0x00, 0xFF);
    static readonly Color FireOrange = new Color32(0xFF, 0x7A, 0x00, 0xFF);
    static readonly Color FireDark = new Color32(0xC8, 0x4A, 0x12, 0xFF);
    static readonly Color SparkLight = new Color32(0xFF, 0xF3, 0xA0, 0xFF);
    static readonly Color SparkHot = new Color32(0xFF, 0x9A, 0x00, 0xFF);
    static readonly Color SparkRed = new Color32(0xFF, 0x55, 0x00, 0xFF);
    static readonly Color RingOrange = new Color32(0xFF, 0xB3, 0x00, 0xFF);
    static readonly Color SmokeDark = new Color32(0x4A, 0x4F, 0x58, 0xFF);
    static readonly Color SmokeMid = new Color32(0x5A, 0x60, 0x6B, 0xFF);
    static readonly Color SmokeLight = new Color32(0x6A, 0x70, 0x78, 0xFF);

    // ============================================================================
    // 菜单入口
    // ============================================================================
    [MenuItem("Tools/Explosion FX/生成爆炸效果 (贴图+材质+预制体)")]
    static void GenerateAll()
    {
        // 预制体是调参主场：已存在时不覆盖，保护手动调整的参数。
        // 需要恢复代码里的出厂参数时，用下面的「重置爆炸预制体」。
        if (File.Exists(EffectPrefabPath))
        {
            Debug.Log("[ExplosionFX] BombExplosion.prefab 已存在，跳过重建（手动调整的参数不会被覆盖）。\n" +
                      "需要恢复出厂参数: Tools > Explosion FX > 重置爆炸预制体 (覆盖手动调整)");
            return;
        }
        GenerateEverything();
    }

    [MenuItem("Tools/Explosion FX/重置爆炸预制体 (覆盖手动调整)")]
    static void ResetPrefab()
    {
        bool confirm = EditorUtility.DisplayDialog(
            "重置爆炸预制体",
            "将用代码中的参数重新生成 BombExplosion.prefab，\n你在预制体上手动调整的参数会被覆盖。确定继续？",
            "重置", "取消");
        if (!confirm)
        {
            return;
        }
        GenerateEverything();
    }

    static void GenerateEverything()
    {
        try
        {
            Directory.CreateDirectory(ParticleTexDir);
            Directory.CreateDirectory(FxMaterialDir);

            // 1. 软粒子贴图
            Texture2D texCircle = SaveAndImport(GenSoftCircle(128), 128,
                ParticleTexDir + "/soft_circle.png");
            Texture2D texRing = SaveAndImport(GenSoftRing(256), 256,
                ParticleTexDir + "/soft_ring.png");
            Texture2D texBurst = SaveAndImport(GenBurstLines(256), 256,
                ParticleTexDir + "/burst_lines.png");
            Texture2D texSmoke = SaveAndImport(GenSmokePuff(256), 256,
                ParticleTexDir + "/smoke_puff.png");

            // 2. 粒子材质 (URP Particles/Unlit，混合由 Blend 属性驱动)
            Material matCircle = CreateParticleMaterial(FxMaterialDir + "/FX_Additive.mat", texCircle, true);
            Material matBurst = CreateParticleMaterial(FxMaterialDir + "/FX_Additive_Burst.mat", texBurst, true);
            Material matRing = CreateParticleMaterial(FxMaterialDir + "/FX_Additive_Ring.mat", texRing, true);
            Material matSmoke = CreateParticleMaterial(FxMaterialDir + "/FX_Alpha_Smoke.mat", texSmoke, false);

            // 3. 冲击波环用网格
            Mesh quad = CreateQuadMesh();

            // 4. 构建分层粒子预制体
            BuildExplosionPrefab(matCircle, matBurst, matRing, matSmoke, quad);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ExplosionFX] 生成完毕 → " + EffectPrefabPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ExplosionFX] 生成失败\n" + e);
        }
    }

    [MenuItem("Tools/Explosion FX/接线到 Bomb.prefab")]
    static void WireBombPrefab()
    {
        GameObject bomb = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        GameObject effect = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPrefabPath);
        if (bomb == null)
        {
            EditorUtility.DisplayDialog("接线失败", "找不到 " + BombPrefabPath, "确定");
            return;
        }
        if (effect == null)
        {
            EditorUtility.DisplayDialog("接线失败", "先执行「生成爆炸效果」再接线。", "确定");
            return;
        }

        BombProjectile projectile = bomb.GetComponent<BombProjectile>();
        if (projectile == null)
        {
            EditorUtility.DisplayDialog("接线失败", "Bomb.prefab 上找不到 BombProjectile。", "确定");
            return;
        }

        SerializedObject so = new SerializedObject(projectile);
        so.FindProperty("explosionEffectPrefab").objectReferenceValue = effect;
        so.FindProperty("effectLifetime").floatValue = 3f; // 兜底销毁
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("[ExplosionFX] 已接线: Bomb.prefab → BombExplosion.prefab (effectLifetime=3)");
    }

    // ============================================================================
    // 分层粒子预制体 (6 个系统, 全部 StopAction=Destroy 自清理)
    // ============================================================================
    static void BuildExplosionPrefab(
        Material matCircle, Material matBurst, Material matRing, Material matSmoke, Mesh quad)
    {
        GameObject root = new GameObject("BombExplosion");

        // --- 1. 星芒闪光: 主"嘭" ---
        ParticleSystem starFlash = CreateSystem(root.transform, "StarFlash", matBurst,
            ParticleSystemRenderMode.Billboard);
        ConfigureMain(starFlash, 0.3f, 0.12f, 0f, 0.6f, 0f, 5);
        SetBurst(starFlash, 1);
        SetSphereShape(starFlash, 0.01f);
        SetColorOverLifetime(starFlash, MakeGradient(
            (0f, FlashWhite), (0.5f, FlashYellow), (1f, new Color(1f, 0.5f, 0f, 0f))));
        SetSizeOverLifetime(starFlash, Curve((0f, 1f), (0.6f, 3f)));

        // --- 2. 二次冲击: 橙红放大层 ---
        ParticleSystem secondPop = CreateSystem(root.transform, "SecondPop", matBurst,
            ParticleSystemRenderMode.Billboard);
        ConfigureMain(secondPop, 0.3f, 0.22f, 0f, 0.9f, 0f, 5);
        SetBurst(secondPop, 1, 0.04f);
        SetSphereShape(secondPop, 0.01f);
        SetColorOverLifetime(secondPop, MakeGradient(
            (0f, new Color(1f, 0.55f, 0f, 0.85f)), (1f, new Color(1f, 0.3f, 0f, 0f))));
        SetSizeOverLifetime(secondPop, Curve((0f, 1f), (0.6f, 2.4f)));

        // --- 3. 火团: 向外扩散 ---
        ParticleSystem fireball = CreateSystem(root.transform, "Fireball", matCircle,
            ParticleSystemRenderMode.Billboard);
        ConfigureMain(fireball, 0.7f, new ParticleSystem.MinMaxCurve(0.4f, 0.6f),
            new ParticleSystem.MinMaxCurve(1.5f, 3.5f),
            new ParticleSystem.MinMaxCurve(0.28f, 0.5f), 0.5f, 50);
        SetBurst(fireball, 12);
        SetSphereShape(fireball, 0.06f);
        SetColorOverLifetime(fireball, MakeGradient(
            (0f, FireYellow), (0.55f, FireOrange), (1f, new Color(FireDark.r, FireDark.g, FireDark.b, 0f))));
        SetSizeOverLifetime(fireball, Curve((0f, 0.7f), (0.4f, 1.3f), (1f, 1.05f)));

        // --- 4. 冲击波环: 水平扩张 (quad 网格 + ring 贴图) ---
        ParticleSystem ring = CreateSystem(root.transform, "ShockwaveRing", matRing,
            ParticleSystemRenderMode.Mesh);
        ring.GetComponent<ParticleSystemRenderer>().mesh = quad;
        ConfigureMain(ring, 0.35f, 0.3f, 0f, 0.5f, 0f, 5);
        SetBurst(ring, 1);
        SetSphereShape(ring, 0.01f);
        SetColorOverLifetime(ring, MakeGradient(
            (0f, RingOrange), (1f, new Color(1f, 0.6f, 0f, 0f))));
        SetSizeOverLifetime(ring, Curve((0f, 1f), (0.6f, 8f))); // 0.5 → 4u，对应 blastRadius=4

        // --- 5. 火星: 拉丝四溅 (卡通爆炸的灵魂) ---
        ParticleSystem sparks = CreateSystem(root.transform, "Sparks", matCircle,
            ParticleSystemRenderMode.Stretch);
        ParticleSystemRenderer sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.velocityScale = 0.12f;
        sparkRenderer.lengthScale = 0.9f;
        ConfigureMain(sparks, 0.8f, new ParticleSystem.MinMaxCurve(0.35f, 0.7f),
            new ParticleSystem.MinMaxCurve(5f, 12f),
            new ParticleSystem.MinMaxCurve(0.04f, 0.09f), 4f, 150);
        SetBurst(sparks, 60);
        SetSphereShape(sparks, 0.08f);
        SetColorOverLifetime(sparks, MakeGradient(
            (0f, SparkLight), (0.6f, SparkHot), (1f, new Color(SparkRed.r, SparkRed.g, SparkRed.b, 0f))));

        // --- 6. 卡通烟: 上升飘散 ---
        ParticleSystem smoke = CreateSystem(root.transform, "Smoke", matSmoke,
            ParticleSystemRenderMode.Billboard);
        ConfigureMain(smoke, 1.5f, new ParticleSystem.MinMaxCurve(0.8f, 1.4f),
            new ParticleSystem.MinMaxCurve(0.6f, 1.6f),
            new ParticleSystem.MinMaxCurve(0.35f, 0.55f), -0.35f, 50);
        SetBurst(smoke, 10, 0.05f);
        SetSphereShape(smoke, 0.25f);
        var vel = smoke.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(1.3f);
        SetColorOverLifetime(smoke, MakeGradient(
            (0f, new Color(SmokeDark.r, SmokeDark.g, SmokeDark.b, 0.85f)),
            (0.5f, new Color(SmokeMid.r, SmokeMid.g, SmokeMid.b, 0.55f)),
            (1f, new Color(SmokeLight.r, SmokeLight.g, SmokeLight.b, 0f))));
        SetSizeOverLifetime(smoke, Curve((0f, 0.8f), (1f, 2.6f)));

        PrefabUtility.SaveAsPrefabAsset(root, EffectPrefabPath);
        Object.DestroyImmediate(root);
    }

    // ============================================================================
    // 粒子系统配置辅助
    // ============================================================================
    static ParticleSystem CreateSystem(Transform parent, string name, Material mat,
        ParticleSystemRenderMode renderMode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = mat;
        renderer.renderMode = renderMode;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static void ConfigureMain(ParticleSystem ps, float duration,
        ParticleSystem.MinMaxCurve lifetime, ParticleSystem.MinMaxCurve speed,
        ParticleSystem.MinMaxCurve size, float gravity, int maxParticles)
    {
        var main = ps.main;
        main.loop = false;
        main.duration = duration;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.maxParticles = maxParticles;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.stopAction = ParticleSystemStopAction.Destroy;
    }

    static void SetBurst(ParticleSystem ps, int count, float delay = 0f)
    {
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)count) });
    }

    static void SetSphereShape(ParticleSystem ps, float radius)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.randomDirectionAmount = 0.9f;
    }

    static void SetColorOverLifetime(ParticleSystem ps, Gradient gradient)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    static void SetSizeOverLifetime(ParticleSystem ps, AnimationCurve curve)
    {
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    /// 构造渐变色阶: (时间, 颜色)
    static Gradient MakeGradient(params (float t, Color c)[] stops)
    {
        var gradient = new Gradient();
        var colorKeys = new GradientColorKey[stops.Length];
        var alphaKeys = new GradientAlphaKey[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            colorKeys[i] = new GradientColorKey(stops[i].c, stops[i].t);
            alphaKeys[i] = new GradientAlphaKey(stops[i].c.a, stops[i].t);
        }
        gradient.SetKeys(colorKeys, alphaKeys);
        return gradient;
    }

    /// 构造曲线: (时间, 值) 键
    static AnimationCurve Curve(params (float t, float v)[] keys)
    {
        var curve = new AnimationCurve();
        foreach ((float t, float v) in keys)
        {
            curve.AddKey(t, v);
        }
        return curve;
    }

    // ============================================================================
    // 软粒子贴图生成 (确定性哈希噪声)
    // ============================================================================
    static Texture2D SaveAndImport(Color32[] px, int size, string path)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Color32[] GenSoftCircle(int size)
    {
        return Compute(size, (u, v) =>
        {
            float r = DistToCenter(u, v) * 2f;
            float a = 1f - Mathf.SmoothStep(0.2f, 0.55f, r);
            return new Color32(255, 255, 255, (byte)(a * 255f));
        });
    }

    static Color32[] GenSoftRing(int size)
    {
        return Compute(size, (u, v) =>
        {
            float r = DistToCenter(u, v);
            float a = 1f - Mathf.SmoothStep(0.03f, 0.09f, Mathf.Abs(r - 0.5f));
            a *= Mathf.SmoothStep(0.3f, 0.42f, r);          // 内缘渐变
            a *= 1f - Mathf.SmoothStep(0.6f, 0.72f, r);     // 外缘渐变
            return new Color32(255, 255, 255, (byte)(a * 255f));
        });
    }

    static Color32[] GenBurstLines(int size)
    {
        const int lineCount = 12;
        return Compute(size, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;          // 归一化 [0,1]
            float angle = (Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI * 2f); // [0,1)
            float d = Mathf.Abs(Mathf.Repeat(angle * lineCount + 0.5f, 1f) - 0.5f) * 2f;
            float w = Mathf.Lerp(0.05f, 0.22f, r)
                    + (Fbm(u * 9f, v * 9f, 7, 2) - 0.5f) * 0.08f;        // 抖动线宽
            float line = 1f - Mathf.SmoothStep(0f, w, d);
            float disc = 1f - Mathf.SmoothStep(0f, 0.18f, r);           // 中心亮点
            float fade = 1f - Mathf.SmoothStep(0.55f, 0.95f, r);        // 边缘淡出
            float a = Mathf.Pow(Mathf.Max(line * fade, disc * 0.9f), 1.2f);
            return new Color32(255, 255, 255, (byte)(a * 255f));
        });
    }

    static Color32[] GenSmokePuff(int size)
    {
        return Compute(size, (u, v) =>
        {
            float n = Fbm(u * 3.5f, v * 3.5f, 11, 4);
            float mask = Mathf.SmoothStep(0.45f, 0.6f, n);
            float r = DistToCenter(u, v);
            float fade = 1f - Mathf.SmoothStep(0.55f, 0.95f, r);
            float a = Mathf.Pow(mask * fade, 1.3f);
            return new Color32(255, 255, 255, (byte)(a * 255f));
        });
    }

    /// 通用像素循环: fn(u,v) → Color32
    static Color32[] Compute(int size, System.Func<float, float, Color32> fn)
    {
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            px[x + y * size] = fn((x + 0.5f) / size, (y + 0.5f) / size);
        }
        return px;
    }

    static float DistToCenter(float u, float v)
    {
        float dx = u - 0.5f, dy = v - 0.5f;
        return Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;
    }

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
        fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        float a = Mathf.Lerp(Hash(xi, yi, seed), Hash(xi + 1, yi, seed), fx);
        float b = Mathf.Lerp(Hash(xi, yi + 1, seed), Hash(xi + 1, yi + 1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static float Fbm(float x, float y, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + i * 131);
            freq *= 2f;
            amp *= 0.5f;
        }
        return sum;
    }

    // ============================================================================
    // 材质与网格
    // ============================================================================
    static Material CreateParticleMaterial(string path, Texture2D map, bool additive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogError("[ExplosionFX] 找不到 Universal Render Pipeline/Particles/Unlit shader");
            return null;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = mat == null;
        if (isNew)
        {
            mat = new Material(shader);
        }

        mat.name = Path.GetFileNameWithoutExtension(path);
        if (map != null)
        {
            mat.SetTexture("_BaseMap", map);
        }
        ApplyParticleBlend(mat, additive);

        if (isNew)
        {
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    // URP 17 ParticlesUnlit: 混合状态由 _SrcBlend/_DstBlend 等 Blend 属性直接驱动，
    // 没有 _ALPHABLEND_ON/_ADDITIVE_ON 关键字。_Surface/_Blend 仅为面板一致性。
    static void ApplyParticleBlend(Material mat, bool additive)
    {
        mat.SetInt("_Surface", 1);
        mat.SetInt("_Blend", additive ? 1 : 0);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
        mat.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_DstBlendAlpha", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_BaseColor", Color.white);
    }

    // 修复已有材质的混合设置。只动材质文件，贴图与预制体保持原样
    // （用于材质混合被意外重置成不透明、导致粒子显示为方块的情况）。
    [MenuItem("Tools/Explosion FX/修复粒子材质 (只改混合, 不动贴图)")]
    static void FixMaterialBlends()
    {
        string[] additivePaths =
        {
            FxMaterialDir + "/FX_Additive.mat",
            FxMaterialDir + "/FX_Additive_Burst.mat",
            FxMaterialDir + "/FX_Additive_Ring.mat",
        };
        string alphaPath = FxMaterialDir + "/FX_Alpha_Smoke.mat";

        int fixedCount = 0;
        foreach (string path in additivePaths)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogWarning("[ExplosionFX] 找不到材质: " + path);
                continue;
            }
            ApplyParticleBlend(mat, true);
            EditorUtility.SetDirty(mat);
            fixedCount++;
        }

        Material smoke = AssetDatabase.LoadAssetAtPath<Material>(alphaPath);
        if (smoke == null)
        {
            Debug.LogWarning("[ExplosionFX] 找不到材质: " + alphaPath);
        }
        else
        {
            ApplyParticleBlend(smoke, false);
            EditorUtility.SetDirty(smoke);
            fixedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ExplosionFX] 已修复 {fixedCount} 个粒子材质: SrcAlpha/One 加法混合 (烟雾 OneMinusSrcAlpha) + " +
                  "关闭深度写入 + 透明队列。贴图与预制体未改动。");
    }

    static Mesh CreateQuadMesh()
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(QuadMeshPath);
        if (existing != null)
        {
            return existing;
        }

        var mesh = new Mesh { name = "FX_QuadXZ" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, QuadMeshPath);
        return mesh;
    }
}
