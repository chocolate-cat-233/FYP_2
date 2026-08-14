Shader "Custom/LavaFlow"
{
    // Stylised, fully opaque lava waterfall for URP.
    //
    // Generated texture layout (Tools > Lava > Waterfall Texture Baker):
    //   Flow maps: RG = signed flow direction, B = local speed, A = coverage.
    //   Pattern:   R = heat, G = cooling crust, B = height, A = hot filament.
    //
    // Flat top and side faces take the normal five-texture-sample path:
    // one flow-control sample plus two seamless phases at macro/detail scale.
    // Only the narrow top/side normal transition evaluates both projections.

    Properties
    {
        [Header(Temperature Colors)]
        [HDR] _CrustColor ("Cooling Crust", Color) = (0.035, 0.012, 0.006, 1)
        [HDR] _HotColor ("Molten Orange", Color) = (1.25, 0.16, 0.012, 1)
        [HDR] _CoreColor ("White Hot Core", Color) = (2.2, 0.72, 0.1, 1)
        _EmissionStrength ("Emission Strength", Range(0, 4)) = 1.25

        [Header(Generated Textures)]
        [NoScaleOffset] _LavaPattern ("Packed Lava Pattern", 2D) = "gray" {}
        [NoScaleOffset] _FlowMapTop ("Top Flow RG Speed B Coverage A", 2D) = "gray" {}
        [NoScaleOffset] _FlowMapSide ("Side Flow RG Speed B Coverage A", 2D) = "gray" {}

        [Header(Projection)]
        _WorldScale ("World Tiling", Range(0.02, 4)) = 0.42
        _DetailScale ("Detail Tiling Multiplier", Range(1, 8)) = 3.1
        _TopThreshold ("Top Normal Threshold", Range(0, 1)) = 0.55
        _TopBlend ("Top Side Blend", Range(0.001, 0.35)) = 0.08

        [Header(Flow)]
        _FlowSpeed ("Waterfall Speed", Range(0, 2)) = 0.16
        _DetailSpeed ("Detail Speed Multiplier", Range(0.25, 4)) = 1.65
        _SpeedVariation ("Per Stream Speed Variation", Range(0, 0.9)) = 0.45
        _FlowStrength ("Flow Travel", Range(0.02, 1)) = 0.34
        _BreakupAmount ("Animated Edge Breakup", Range(0, 0.7)) = 0.24

        [Header(Surface)]
        _CrustAmount ("Cooling Crust Amount", Range(0, 1)) = 0.42
        _EdgeGlow ("Flow Band Glow", Range(0, 3)) = 1.35
        _NormalStrength ("Flow Relief", Range(0, 2)) = 0.32
        _Smoothness ("Molten Smoothness", Range(0, 1)) = 0.58
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #pragma target 3.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_LavaPattern); SAMPLER(sampler_LavaPattern);
        TEXTURE2D(_FlowMapTop);  SAMPLER(sampler_FlowMapTop);
        TEXTURE2D(_FlowMapSide); SAMPLER(sampler_FlowMapSide);

        CBUFFER_START(UnityPerMaterial)
            float4 _CrustColor;
            float4 _HotColor;
            float4 _CoreColor;
            float _EmissionStrength;
            float _WorldScale;
            float _DetailScale;
            float _TopThreshold;
            float _TopBlend;
            float _FlowSpeed;
            float _DetailSpeed;
            float _SpeedVariation;
            float _FlowStrength;
            float _BreakupAmount;
            float _CrustAmount;
            float _EdgeGlow;
            float _NormalStrength;
            float _Smoothness;
            float _Cull;
        CBUFFER_END

        struct LavaSurface
        {
            float coverage;
            float heat;
            float crust;
            float height;
            float filament;
        };

        float2 DecodeFlow(float2 encoded)
        {
            float2 flow = encoded * 2.0 - 1.0;
            float lengthSq = dot(flow, flow);
            return lengthSq > 1e-5 ? flow * rsqrt(lengthSq) : float2(0.0, -1.0);
        }

        float4 DualPhasePattern(float2 uv, float2 flow, float phase, float travel)
        {
            float phaseA = frac(phase);
            float phaseB = frac(phase + 0.5);
            float crossFade = abs(phaseA * 2.0 - 1.0);
            float4 sampleA = SAMPLE_TEXTURE2D(
                _LavaPattern, sampler_LavaPattern, uv - flow * phaseA * travel);
            float4 sampleB = SAMPLE_TEXTURE2D(
                _LavaPattern, sampler_LavaPattern, uv - flow * phaseB * travel);
            return lerp(sampleA, sampleB, crossFade);
        }

        LavaSurface EvaluateProjection(float2 uv, float4 control)
        {
            LavaSurface surface = (LavaSurface)0;
            float2 flow = DecodeFlow(control.rg);
            float localSpeed = _FlowSpeed * lerp(
                1.0 - _SpeedVariation, 1.0 + _SpeedVariation, control.b);

            float4 macroPattern = DualPhasePattern(
                uv, flow, _Time.y * localSpeed, _FlowStrength);

            float detailScale = max(_DetailScale, 1.0);
            float4 detailPattern = DualPhasePattern(
                uv * detailScale,
                flow,
                _Time.y * localSpeed * _DetailSpeed,
                _FlowStrength * detailScale);

            float breakup = (macroPattern.r - 0.5) +
                            (detailPattern.g - 0.5) * 0.35;
            surface.coverage = saturate(control.a + breakup * _BreakupAmount);
            surface.crust = saturate(lerp(macroPattern.g, detailPattern.g, 0.28));
            surface.filament = saturate(max(
                smoothstep(0.58, 0.9, macroPattern.a),
                smoothstep(0.68, 0.94, detailPattern.a) * 0.75));
            surface.heat = saturate(
                macroPattern.r * 0.64 + detailPattern.r * 0.22 +
                surface.filament * 0.38 -
                saturate(surface.crust - 0.48) * _CrustAmount);
            surface.height = saturate(lerp(macroPattern.b, detailPattern.b, 0.34));
            return surface;
        }

        float2 SideUv(float3 positionWS, float3 normalWS)
        {
            // Keep U consistently oriented around axis-aligned boxes so the
            // same periodic upper-edge profile can wrap all four faces.
            if (abs(normalWS.x) > abs(normalWS.z))
                return float2(positionWS.z * (normalWS.x >= 0.0 ? 1.0 : -1.0),
                              positionWS.y);
            return float2(positionWS.x * (normalWS.z >= 0.0 ? -1.0 : 1.0),
                          positionWS.y);
        }

        LavaSurface EvaluateLava(float3 positionWS, float3 normalWS)
        {
            LavaSurface result = (LavaSurface)0;
            float3 n = normalize(normalWS);
            float blendWidth = max(_TopBlend, 0.001);
            float topMask = smoothstep(
                _TopThreshold - blendWidth,
                _TopThreshold + blendWidth,
                n.y);

            float2 topUv = positionWS.xz * _WorldScale;
            float2 sideUv = SideUv(positionWS, n) * _WorldScale;

            UNITY_BRANCH
            if (topMask >= 0.999)
            {
                float4 control = SAMPLE_TEXTURE2D(
                    _FlowMapTop, sampler_FlowMapTop, topUv);
                result = EvaluateProjection(topUv, control);
            }
            else if (topMask <= 0.001)
            {
                float4 control = SAMPLE_TEXTURE2D(
                    _FlowMapSide, sampler_FlowMapSide, sideUv);
                result = EvaluateProjection(sideUv, control);
            }
            else
            {
                // Sloped transition geometry is uncommon for the intended
                // box / plane use case. It deliberately pays for both
                // projections so the lip remains stable instead of popping.
                LavaSurface top = EvaluateProjection(topUv, SAMPLE_TEXTURE2D(
                    _FlowMapTop, sampler_FlowMapTop, topUv));
                LavaSurface side = EvaluateProjection(sideUv, SAMPLE_TEXTURE2D(
                    _FlowMapSide, sampler_FlowMapSide, sideUv));
                result.coverage = lerp(side.coverage, top.coverage, topMask);
                result.heat = lerp(side.heat, top.heat, topMask);
                result.crust = lerp(side.crust, top.crust, topMask);
                result.height = lerp(side.height, top.height, topMask);
                result.filament = lerp(side.filament, top.filament, topMask);
            }
            return result;
        }

        float3 PerturbLavaNormal(float3 positionWS, float3 normalWS, float height)
        {
            float3 sigmaX = ddx(positionWS);
            float3 sigmaY = ddy(positionWS);
            float heightX = ddx(height);
            float heightY = ddy(height);
            float3 r1 = cross(sigmaY, normalWS);
            float3 r2 = cross(normalWS, sigmaX);
            float determinant = dot(sigmaX, r1);
            float3 gradient = sign(determinant) * (heightX * r1 + heightY * r2);
            return normalize(abs(determinant) * normalWS - gradient * _NormalStrength);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LavaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = GetShadowCoord(positions);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 LavaFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                LavaSurface surface = EvaluateLava(input.positionWS, input.normalWS);
                float flowBand = smoothstep(0.22, 0.78, surface.coverage);
                float3 normalWS = PerturbLavaNormal(
                    input.positionWS, normalize(input.normalWS), surface.height);

                float temperature = saturate(
                    surface.heat + surface.filament * 0.28 + flowBand * 0.12);
                float cooled = saturate(
                    smoothstep(0.45, 0.9, surface.crust) *
                    (1.0 - temperature) * _CrustAmount);

                half3 molten = lerp(
                    _HotColor.rgb,
                    _CoreColor.rgb,
                    smoothstep(0.42, 0.92, temperature));
                half3 albedo = lerp(
                    saturate(_CrustColor.rgb),
                    saturate(_HotColor.rgb * 0.52),
                    smoothstep(0.08, 0.72, temperature));
                albedo = lerp(albedo, saturate(_CrustColor.rgb), cooled);

                Light mainLight = GetMainLight(input.shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half attenuation = mainLight.distanceAttenuation *
                                   mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normalWS) * albedo;
                half3 diffuse = albedo * mainLight.color * ndl * attenuation;

                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                half specularPower = lerp(12.0h, 96.0h, _Smoothness);
                half specular = pow(saturate(dot(normalWS, halfDirection)), specularPower) *
                                _Smoothness * attenuation * (0.2h + temperature * 0.8h);

                half emissionMask = saturate(
                    temperature * (1.0 - cooled * 0.75) +
                    surface.filament * 0.35 + flowBand * _EdgeGlow * 0.18);
                half3 emission = molten * emissionMask * _EmissionStrength;
                emission += _CoreColor.rgb * flowBand * surface.filament *
                            _EdgeGlow * 0.16;

                half3 color = ambient + diffuse + mainLight.color * specular + emission;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetLavaShadowPositionHClip(Attributes input, out float3 positionWS,
                                              out float3 normalWS)
            {
                positionWS = TransformObjectToWorld(input.positionOS.xyz);
                normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = GetLavaShadowPositionHClip(
                    input, output.positionWS, output.normalWS);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull [_Cull]
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                LavaSurface surface = EvaluateLava(input.positionWS, input.normalWS);
                float3 normalWS = PerturbLavaNormal(
                    input.positionWS, normalize(input.normalWS), surface.height);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
