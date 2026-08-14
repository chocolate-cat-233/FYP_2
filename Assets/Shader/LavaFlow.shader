Shader "Custom/LavaFlow"
{
    // 卡通岩浆推进前缘着色器 (URP)
    //
    // 一张 3D 噪声体纹理 (R/G/B/A = 频率 1/2/4/8 的无缝值噪声, 由
    // Tools > Lava > 生成岩浆 3D 噪声纹理 生成并自动接线) 驱动全部运动。
    //
    // 核心叙事: 岩浆像生命体一样缓慢吞噬表面 ——
    //   推进:   循环相位前 80% 时间, 前缘从对象顶部向底部推进 (顶面则
    //           从噪声到达场向外扩张晕开), 前缘带噪声"手指"蠕动伸出。
    //   前缘:   推进边界最热 (白热), 越靠后越冷却 (深红 → 暗结壳),
    //           岩浆体内有竖直绳流纹理与脉纹。
    //   滴落:   前缘下方有细条纹亮须伸出, 缓慢下坠 —— 滴落感。
    //   结壳:   冷却区留下发光裂纹网 + 板块色差, 未到达区域是深色岩面。
    //   循环:   相位后 20% 整体淡出, 重置后像新一轮喷发。
    //   顶/侧判定: 世界法线 Y 分量 (阈值 + 过渡带平滑混合)。

    Properties
    {
        [Header(Colors)]
        [HDR]_CrustColor ("结壳颜色", Color) = (0.07, 0.04, 0.03, 1)
        [HDR]_HotColor ("炽热颜色", Color) = (0.95, 0.25, 0.02, 1)
        [HDR]_CoreColor ("白热核心", Color) = (1.0, 0.55, 0.1, 1)
        _EmissionStrength ("自发光强度", Range(0, 6)) = 2.2

        [Header(Advancing Front)]
        _FrontCycleSpeed ("推进循环速度", Range(0.005, 1)) = 0.03
        _FrontSharpness ("前缘锐度", Range(0.5, 30)) = 6
        _FrontTop ("前缘起点 (对象Y)", Float) = 1
        _FrontBottom ("前缘终点 (对象Y)", Float) = 0
        _CoolRate ("冷却速度", Range(0, 5)) = 1.5
        _FingerScale ("前缘手指尺度", Range(0.1, 5)) = 1.2
        _FingerAmount ("前缘手指伸出量", Range(0, 3)) = 1.0
        _FingerEvolve ("手指蠕动速度", Range(0, 5)) = 1.0

        [Header(Drips)]
        _DripReach ("滴落长度", Range(0, 3)) = 0.8
        _DripSpeed ("滴落速度", Range(0, 3)) = 0.6
        _DripThreshold ("滴落阈值", Range(0, 1)) = 0.68

        [Header(Top Spread)]
        _TopNoiseScale ("晕开噪声尺度", Range(0.1, 10)) = 1.3
        _TopCoolRate ("顶面冷却速度", Range(0, 15)) = 5

        [Header(Flow Texture)]
        _StreakScale ("条纹拉伸度", Range(0.5, 8)) = 3.5
        _FlowSpeed ("流动速度", Range(-3, 3)) = 0.7
        _WarpAmount ("域扭曲强度", Range(0, 3)) = 1.0
        _VeinWidth ("脉纹宽度", Range(0, 0.3)) = 0.08

        [Header(Cracks and Detail)]
        _CrackScale ("裂纹尺度", Range(1, 30)) = 8
        _CrackThreshold ("裂纹阈值", Range(0, 1)) = 0.72
        _PlateScale ("结壳板块尺度", Range(0.1, 5)) = 0.7

        [Header(3D Noise)]
        _LavaNoise ("岩浆噪声 (3D)", 3D) = "white" {}

        [Header(Top Detection)]
        _TopThreshold ("顶面法线阈值", Range(0, 1)) = 0.55
        _TopBlend ("顶面过渡带", Range(0.01, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // ── Forward (colour) pass ─────────────────────────────────────
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CrustColor;
                float4 _HotColor;
                float4 _CoreColor;
                float  _EmissionStrength;
                float  _FrontCycleSpeed;
                float  _FrontSharpness;
                float  _FrontTop;
                float  _FrontBottom;
                float  _CoolRate;
                float  _FingerScale;
                float  _FingerAmount;
                float  _FingerEvolve;
                float  _DripReach;
                float  _DripSpeed;
                float  _DripThreshold;
                float  _TopNoiseScale;
                float  _TopCoolRate;
                float  _StreakScale;
                float  _FlowSpeed;
                float  _WarpAmount;
                float  _VeinWidth;
                float  _CrackScale;
                float  _CrackThreshold;
                float  _PlateScale;
                float  _TopThreshold;
                float  _TopBlend;
            CBUFFER_END

            // ── 3D 噪声体纹理: R/G/B/A = 频率 1/2/4/8 的无缝值噪声 ──
            // (由 Tools > Lava > 生成岩浆 3D 噪声纹理 程序化生成)
            TEXTURE3D(_LavaNoise);
            SAMPLER(sampler_LavaNoise);

            // 单次采样完成 4 阶 FBM
            float LavaFbm(float3 p)
            {
                float4 octaves = SAMPLE_TEXTURE3D(_LavaNoise, sampler_LavaNoise, p);
                return dot(octaves, float4(0.5, 0.25, 0.125, 0.0625));
            }

            // 最细频率层, 用作裂纹/滴须细节
            float LavaDetail(float3 p)
            {
                return SAMPLE_TEXTURE3D(_LavaNoise, sampler_LavaNoise, p).a;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS  = IN.positionOS.xyz;
                return OUT;
            }

            // ── Fragment ───────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                half3 normal = normalize(IN.normalWS);
                float time = _Time.y;
                float3 o = IN.positionOS;

                // 顶面判定: 顶面晕开推进 / 侧面自上而下推进
                float topMask = saturate(
                    (normal.y - _TopThreshold) / max(_TopBlend, 0.001));

                // ── 推进循环相位: 前 80% 推进, 后 20% 淡出重置 ──
                float phi = frac(time * _FrontCycleSpeed);
                float progress = smoothstep(0.0, 0.8, phi);
                float frontFade = 1.0 - smoothstep(0.8, 1.0, phi);

                // ── 侧面前缘: 从对象顶部向底部推进, 噪声手指蠕动 ──
                float frontY = lerp(_FrontTop, _FrontBottom, progress);
                float finger = LavaFbm(float3(
                    o.x * _FingerScale,
                    phi * _FingerEvolve,
                    o.z * _FingerScale));
                float sideFront = frontY + (finger - 0.5) * _FingerAmount;
                float lavaAbove = saturate((o.y - sideFront) * _FrontSharpness);
                float sideHeat = exp(-max(0.0, o.y - sideFront) * _CoolRate);

                // ── 顶面前缘: 噪声到达场随进度扩张 → 晕开推进 ──
                float arrive = 0.5 + 0.45 * LavaFbm(float3(
                    IN.positionWS.x * _TopNoiseScale,
                    phi * 0.7,
                    IN.positionWS.z * _TopNoiseScale));
                float topLava = 1.0 - smoothstep(
                    progress - 0.05, progress + 0.10, arrive);
                float topHeat = exp(-max(0.0, progress - arrive) * _TopCoolRate);

                // ── 顶/侧混合 ──
                float lava = lerp(lavaAbove, topLava, topMask);
                float heat = lerp(sideHeat, topHeat, topMask);
                heat *= 0.75 + 0.5 * LavaFbm(IN.positionWS * 0.8); // 冷却不均斑块
                heat *= frontFade;

                // ── 前缘下方: 滴落亮须 (细条纹下坠, 距前缘越远越淡) ──
                float dripN = LavaDetail(float3(
                    o.x * _StreakScale,
                    (o.y - time * _DripSpeed) / _StreakScale,
                    o.z * _StreakScale));
                float drip = smoothstep(
                    _DripThreshold, _DripThreshold + 0.05, dripN) *
                    exp(-max(0.0, sideFront - o.y) * _DripReach) *
                    (1.0 - lavaAbove) * (1.0 - topMask);

                // ── 岩浆体内的流动纹理: 竖直绳流 + 脉纹 ──
                float3 flowP = float3(
                    o.x * _StreakScale,
                    (o.y - time * _FlowSpeed) / _StreakScale,
                    o.z * _StreakScale);
                float flowWarp = LavaFbm(flowP * 1.2 + 9.7) - 0.5;
                float flowN = LavaFbm(flowP + float3(
                    flowWarp, 0.0, flowWarp) * _WarpAmount);
                float flowVein = smoothstep(_VeinWidth, 0.0, abs(flowN - 0.62));

                // ── 结壳裂纹网 + 板块色差 ──
                float crack = smoothstep(
                    _CrackThreshold, _CrackThreshold + 0.025,
                    LavaDetail(IN.positionWS * _CrackScale));
                float plate = LavaFbm(IN.positionWS * _PlateScale);

                // ── 颜色: 未到达=深色岩面 → 六段温度渐变 → 脉纹/裂纹/滴须 ──
                half3 rock = _CrustColor.rgb * 0.55;
                half3 crust = _CrustColor.rgb * (0.8 + 0.35 * plate);
                half3 deepRed = _HotColor.rgb * 0.4;
                half3 hotColor = _HotColor.rgb;
                half3 core = _CoreColor.rgb;
                half3 whiteHot = lerp(core, half3(1.0, 0.98, 0.85), 0.55);
                half3 col = lerp(rock, crust, lava);
                col = lerp(col, deepRed,  smoothstep(0.08, 0.30, heat));
                col = lerp(col, hotColor, smoothstep(0.28, 0.55, heat));
                col = lerp(col, core,     smoothstep(0.50, 0.78, heat));
                col = lerp(col, whiteHot, smoothstep(0.75, 0.95, heat));
                col = lerp(col, whiteHot, flowVein * lava * (0.3 + 0.7 * heat));
                col = lerp(col, whiteHot * 0.9,
                    crack * (0.15 + 0.85 * heat) * saturate(lava * 2.0));
                col = lerp(col, whiteHot * 0.9, drip * (0.5 + 0.5 * heat));

                // ── HDR 自发光: 温度色 × 热度 + 脉纹/裂纹/滴须辉光 ──
                half3 emission = (col * heat * 1.6 +
                    whiteHot * (flowVein * lava * 1.8 +
                    crack * heat * 1.2 + drip * 1.5)) * _EmissionStrength;

                // 主光 + SH 环境光
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normal, mainLight.direction));
                NdotL *= mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normal);
                half3 diffuse = col * (ambient + mainLight.color * NdotL);

                return half4(diffuse + emission, 1.0);
            }
            ENDHLSL
        }

        // ── Shadow caster pass ────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ── Depth-only pass (for _CameraDepthTexture) ─────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
