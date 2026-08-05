Shader "Custom/GrassVertexColor"
{
    // Cartoon-style ground shader driven by vertex colors — colors only,
    // no textures.
    //
    // Vertex color channels:
    //   R — grass mask: 1 = grass, 0 = ground (soft band around 0.5)
    //   G — wind scale: 1 = full wind, 0 = pinned down
    //   B/A — unused.
    // Meshes without a color channel read as white = all grass. Paint with
    // the Vertex Color Brush (Tools > Vertex Color Brush).

    Properties
    {
        _GrassColor ("Grass Color", Color) = (0.35, 0.6, 0.25, 1)
        _GroundColor ("Ground Color", Color) = (0.45, 0.34, 0.22, 1)

        [Header(Blending)]
        _BlendSoftness ("Blend Band Width", Range(0, 1)) = 0.15

        [Header(Toon Lighting)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.35
        _ShadowSoftness ("Shadow Edge Softness", Range(0, 0.5)) = 0.08
        _AmbientColor ("Shadow Color", Color) = (0.45, 0.55, 0.42, 1)
        _AmbientStrength ("Shadow Strength", Range(0, 1)) = 0.55

        [Header(Wind)]
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.35
        _WindFrequency ("Wind Frequency", Range(0.1, 10)) = 1.5
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1.0
        _WindDirection ("Wind Direction (XZ)", Vector) = (1, 0, 0, 0)
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassColor;
                float4 _GroundColor;
                float  _BlendSoftness;
                float  _ShadowThreshold;
                float  _ShadowSoftness;
                float4 _AmbientColor;
                float  _AmbientStrength;
                float  _WindStrength;
                float  _WindFrequency;
                float  _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float4 vertexColor : TEXCOORD1;
            };

            float3 ApplyWind(float3 positionWS, float4 vertexColor)
            {
                float2 windDir = normalize(
                    _WindDirection.xz + float2(1e-4, 0.0));
                float phase = _Time.y * _WindSpeed +
                    dot(positionWS.xz, windDir) * _WindFrequency;
                // Two out-of-phase sines for a less mechanical sway.
                float sway = sin(phase) + 0.5 * sin(phase * 2.7 + 1.3);
                // R is the grass mask, G scales wind locally (0 pins down).
                float amplitude = _WindStrength * vertexColor.r *
                    vertexColor.g;
                return positionWS +
                    float3(windDir.x, 0.0, windDir.y) * (sway * amplitude);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = ApplyWind(
                    TransformObjectToWorld(IN.positionOS.xyz),
                    IN.color);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.vertexColor = IN.color;
                return OUT;
            }

            // ── Fragment ───────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // Soft band around the 0.5 midline of the R mask.
                half mask = saturate(IN.vertexColor.r);
                half band = 0.5 * saturate(_BlendSoftness);
                half blend = smoothstep(0.5 - band, 0.5 + band, mask);
                half3 albedo = lerp(
                    _GroundColor.rgb,
                    _GrassColor.rgb,
                    blend);

                // Toon shading: hard shadow band with a soft edge.
                Light mainLight = GetMainLight();
                half3 normal = normalize(IN.normalWS);
                half NdotL = saturate(dot(normal, mainLight.direction));
                half toonLight = smoothstep(
                    _ShadowThreshold,
                    _ShadowThreshold + _ShadowSoftness,
                    NdotL);
                toonLight *= mainLight.shadowAttenuation;
                half3 lit = albedo * mainLight.color;
                half3 shade = albedo * _AmbientColor.rgb *
                    _AmbientStrength;
                return half4(lerp(shade, lit, toonLight), 1.0);
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

            CBUFFER_START(UnityPerMaterial)
                float _WindStrength;
                float _WindFrequency;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float3 ApplyWind(float3 positionWS, float4 vertexColor)
            {
                float2 windDir = normalize(
                    _WindDirection.xz + float2(1e-4, 0.0));
                float phase = _Time.y * _WindSpeed +
                    dot(positionWS.xz, windDir) * _WindFrequency;
                float sway = sin(phase) + 0.5 * sin(phase * 2.7 + 1.3);
                float amplitude = _WindStrength * vertexColor.r *
                    vertexColor.g;
                return positionWS +
                    float3(windDir.x, 0.0, windDir.y) * (sway * amplitude);
            }

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = ApplyWind(
                    TransformObjectToWorld(IN.positionOS.xyz),
                    IN.color);
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

            CBUFFER_START(UnityPerMaterial)
                float _WindStrength;
                float _WindFrequency;
                float _WindSpeed;
                float4 _WindDirection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float3 ApplyWind(float3 positionWS, float4 vertexColor)
            {
                float2 windDir = normalize(
                    _WindDirection.xz + float2(1e-4, 0.0));
                float phase = _Time.y * _WindSpeed +
                    dot(positionWS.xz, windDir) * _WindFrequency;
                float sway = sin(phase) + 0.5 * sin(phase * 2.7 + 1.3);
                float amplitude = _WindStrength * vertexColor.r *
                    vertexColor.g;
                return positionWS +
                    float3(windDir.x, 0.0, windDir.y) * (sway * amplitude);
            }

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = ApplyWind(
                    TransformObjectToWorld(IN.positionOS.xyz),
                    IN.color);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
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
