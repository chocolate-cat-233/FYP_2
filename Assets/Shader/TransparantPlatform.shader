Shader "Custom/TransparantPlatform"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        _BaseAlpha ("Base Alpha (Invisible)", Range(0,1)) = 0.0
        _RevealEdgeSoftness ("Reveal Edge Softness", Range(0.01, 1)) = 0.3
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        // ── Forward (colour) pass ─────────────────────────────────────
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Per-material properties ────────────────────────────────
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _BaseAlpha;
                float  _RevealEdgeSoftness;
            CBUFFER_END

            // ── Global arrays set by PlatformRevealManager ──────────────
            // xyz = world-space explosion centre, w = reveal radius
            float4 _RevealPoints[16];
            float  _RevealStrengths[16];

            // ── Vertex ─────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // ── SDF-based reveal alpha ─────────────────────────────────
            float ComputeRevealAlpha(float3 worldPos)
            {
                float bestAlpha = _BaseAlpha;

                // Iterate the full array so the compiler sees a constant bound.
                // Inactive slots have strength 0 and are skipped.
                for (int i = 0; i < 16; i++)
                {
                    float strength = _RevealStrengths[i];
                    if (strength <= 0.0) continue;

                    float3 pointPos = _RevealPoints[i].xyz;
                    float  radius   = _RevealPoints[i].w;
                    float  dist     = distance(worldPos, pointPos);

                    if (dist < radius)
                    {
                        // normalizedDist: 0 at centre, 1 at the ring edge
                        float normalizedDist = dist / max(radius, 0.001);
                        // Inner region (dist < radius*softness) is solid;
                        // outer rim fades smoothly to 0 at the ring edge.
                        float edgeFade = 1.0 - smoothstep(_RevealEdgeSoftness, 1.0, normalizedDist);
                        bestAlpha = max(bestAlpha, edgeFade * strength);
                    }
                }

                return saturate(bestAlpha);
            }

            // ── Fragment ───────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // Sample albedo
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 albedo  = baseMap * _BaseColor;

                // Simple diffuse from the main directional light
                Light mainLight = GetMainLight();
                half3 normal    = normalize(IN.normalWS);
                half  NdotL     = saturate(dot(normal, mainLight.direction));
                half3 diffuse   = albedo.rgb * mainLight.color * (NdotL * mainLight.shadowAttenuation);

                // Ambient
                half3 ambient = SampleSH(normal) * albedo.rgb;

                half3 finalColor = diffuse + ambient * 0.5;

                half alpha = ComputeRevealAlpha(IN.positionWS);

                return half4(finalColor, alpha);
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
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
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
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
