// ============================================================================
// 岩浆 3D 噪声纹理生成器 LavaFlowTextureGenerator
// ----------------------------------------------------------------------------
// 为 Custom/LavaFlow 着色器生成无缝平铺的 3D 噪声体纹理 (64³ RGBA32):
//   R/G/B/A = 频率 1/2/4/8 的四层值噪声 (着色器加权求和即 4 阶 FBM)
//   - 着色器用它定义静态流场 (顶面水平辐散晕开 / 侧面竖直向下),
//     源头温度场沿 -Y 滚动 → 热斑沿流线从顶向下传播 (无缝平铺保证无限循环)
//   - A 通道 (最细频率) 兼作表面斑驳/裂纹细节
// 生成后自动接线到 Assets/Shader/LavaFlow.mat 的 _LavaNoise 槽,
// 并清理材质里引用 shader 已不存在属性的历史保存值。
// 菜单: Tools > Lava > 生成岩浆 3D 噪声纹理
// ============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

public class LavaFlowTextureGenerator
{
    const string TexPath = "Assets/Texture/Lava/LavaNoise3D.asset";
    const string MaterialPath = "Assets/Shader/LavaFlow.mat";
    const int Size = 64;

    [MenuItem("Tools/Lava/生成岩浆 3D 噪声纹理")]
    static void Generate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TexPath));

        Texture3D tex = AssetDatabase.LoadAssetAtPath<Texture3D>(TexPath);
        bool isNew = tex == null;
        if (isNew)
        {
            tex = new Texture3D(Size, Size, Size, TextureFormat.RGBA32, false);
        }
        tex.name = "LavaNoise3D";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        // 每体素烘焙 4 个频率层 (着色器一次采样完成 FBM)
        Color32[] px = new Color32[Size * Size * Size];
        float inv = 1f / Size;
        for (int z = 0; z < Size; z++)
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = (x + 0.5f) * inv;
            float v = (y + 0.5f) * inv;
            float w = (z + 0.5f) * inv;
            px[x + y * Size + z * Size * Size] = new Color32(
                ToByte(ValueNoise3(u, v, w, 1f, Size, 11)),
                ToByte(ValueNoise3(u, v, w, 2f, Size, 47)),
                ToByte(ValueNoise3(u, v, w, 4f, Size, 89)),
                ToByte(ValueNoise3(u, v, w, 8f, Size, 131)));
        }
        tex.SetPixels32(px);
        tex.Apply(false);

        if (isNew)
        {
            AssetDatabase.CreateAsset(tex, TexPath);
        }
        else
        {
            EditorUtility.SetDirty(tex);
        }

        // 接线到材质
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null)
        {
            mat.SetTexture("_LavaNoise", tex);
            PruneStaleProperties(mat); // 移除 shader 已不存在属性的历史保存值
            EditorUtility.SetDirty(mat);
        }
        else
        {
            Debug.LogWarning("[LavaFlow] 未找到 " + MaterialPath +
                             ", 请手动把 LavaNoise3D 拖到材质的 _LavaNoise 槽");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[LavaFlow] 3D 噪声纹理已生成 → " + TexPath);
    }

    // 清空材质保存的所有参数值, 恢复为着色器默认值 (纹理接线保留)。
    // 用于着色器大改后摆脱旧调参对新默认值的覆盖。
    [MenuItem("Tools/Lava/重置材质参数 (恢复着色器默认值)")]
    static void ResetMaterialParameters()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            EditorUtility.DisplayDialog("重置失败", "找不到 " + MaterialPath, "确定");
            return;
        }

        var so = new SerializedObject(mat);
        ClearArray(so.FindProperty("m_SavedProperties.m_Floats"));
        ClearArray(so.FindProperty("m_SavedProperties.m_Colors"));
        so.ApplyModifiedPropertiesWithoutUndo();

        Texture3D tex = AssetDatabase.LoadAssetAtPath<Texture3D>(TexPath);
        if (tex != null)
        {
            mat.SetTexture("_LavaNoise", tex);
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[LavaFlow] 材质参数已恢复为着色器默认值 (3D 噪声纹理接线保留)");
    }

    static void ClearArray(SerializedProperty array)
    {
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            array.DeleteArrayElementAtIndex(i);
        }
    }

    // 清理材质里引用了 shader 已不存在属性的保存值 (避免 Console 报
    // "doesn't have a property" 警告), 保留仍然有效的用户调参。
    static void PruneStaleProperties(Material mat)
    {
        var so = new SerializedObject(mat);
        RemoveMissing(so.FindProperty("m_SavedProperties.m_Floats"), mat);
        RemoveMissing(so.FindProperty("m_SavedProperties.m_Colors"), mat);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void RemoveMissing(SerializedProperty array, Material mat)
    {
        for (int i = array.arraySize - 1; i >= 0; i--)
        {
            string name = array.GetArrayElementAtIndex(i)
                .FindPropertyRelative("first").stringValue;
            if (!mat.HasProperty(name))
            {
                array.DeleteArrayElementAtIndex(i);
            }
        }
    }

    static byte ToByte(float v)
    {
        return (byte)(Mathf.Clamp01(v) * 255f);
    }

    // 无缝平铺的 3D 值噪声: 整数频率 + 晶格索引按 Size 取模
    static float ValueNoise3(float x, float y, float z, float freq, int size, int seed)
    {
        float fx = x * freq, fy = y * freq, fz = z * freq;
        int xi = (int)Mathf.Floor(fx);
        int yi = (int)Mathf.Floor(fy);
        int zi = (int)Mathf.Floor(fz);
        float tx = Quintic(fx - xi);
        float ty = Quintic(fy - yi);
        float tz = Quintic(fz - zi);
        return Lerp3(
            Hash3(W(xi, size), W(yi, size), W(zi, size), seed),
            Hash3(W(xi + 1, size), W(yi, size), W(zi, size), seed),
            Hash3(W(xi, size), W(yi + 1, size), W(zi, size), seed),
            Hash3(W(xi + 1, size), W(yi + 1, size), W(zi, size), seed),
            Hash3(W(xi, size), W(yi, size), W(zi + 1, size), seed),
            Hash3(W(xi + 1, size), W(yi, size), W(zi + 1, size), seed),
            Hash3(W(xi, size), W(yi + 1, size), W(zi + 1, size), seed),
            Hash3(W(xi + 1, size), W(yi + 1, size), W(zi + 1, size), seed),
            tx, ty, tz);
    }

    // Size 为 2 的幂, 用位与取模
    static int W(int i, int size)
    {
        return i & (size - 1);
    }

    static float Quintic(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    static float Lerp3(
        float c000, float c100, float c010, float c110,
        float c001, float c101, float c011, float c111,
        float tx, float ty, float tz)
    {
        return Mathf.Lerp(
            Mathf.Lerp(
                Mathf.Lerp(c000, c100, tx),
                Mathf.Lerp(c010, c110, tx), ty),
            Mathf.Lerp(
                Mathf.Lerp(c001, c101, tx),
                Mathf.Lerp(c011, c111, tx), ty),
            tz);
    }

    static float Hash3(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 2246822519 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h * (1f / 4294967295f);
        }
    }
}
