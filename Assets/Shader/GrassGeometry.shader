Shader "Custom/GrassGeometry"
{
    // Geometry-shader grass. Each triangle of the ground mesh emits the
    // ground surface plus a field of grass blades (see GrassGeometryShared.
    // hlsl for the emission logic and the vertex color semantics).
    // Colors-only cartoon (toon) style — no textures.
    //
    // Vertex color channels:
    //   R — grass mask: 1 = grass (dense blades), 0 = ground (no blades)
    //   G — wind scale: 1 = full wind, 0 = pinned down
    //   B — blade height multiplier (0 short, 1 tall)
    // Meshes without a color channel read as white = all grass.
    //
    // Requires Shader Model 4.0+ (geometry shaders are not supported on
    // Metal / OpenGL ES — those platforms fall back to GrassVertexColor).

    Properties
    {
        [Header(Colors)]
        _GrassColor ("Grass Color", Color) = (0.35, 0.6, 0.25, 1)
        _GroundColor ("Ground Color", Color) = (0.45, 0.34, 0.22, 1)
        _BladeTipColor ("Blade Tip Color", Color) = (0.62, 0.78, 0.3, 1)

        [Header(Ground Blending)]
        _BlendSoftness ("Blend Band Width", Range(0, 1)) = 0.15

        [Header(Grass Blades)]
        _Density ("Blades per m²", Range(1, 500)) = 60
        _BladeHeight ("Blade Height", Range(0.05, 2)) = 0.5
        _BladeWidth ("Blade Width", Range(0.005, 0.2)) = 0.04
        _MaxBladesPerTriangle ("Max Blades per Triangle", Range(1, 20)) = 12
        _Billboard ("Billboard Facing", Range(0, 1)) = 1
        _AlphaCutoff ("Blade Alpha Cutoff", Range(0, 1)) = 0.1

        [Header(Toon Lighting)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.35
        _ShadowSoftness ("Shadow Edge Softness", Range(0, 0.5)) = 0.08
        _AmbientColor ("Shadow Color", Color) = (0.45, 0.55, 0.42, 1)
        _AmbientStrength ("Shadow Strength", Range(0, 1)) = 0.55

        [Header(Wind)]
        _WindStrength ("Wind Strength", Range(0, 2)) = 0.3
        _WindFrequency ("Wind Frequency", Range(0.1, 10)) = 2
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1
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

            // Blades are thin random quads; draw both faces.
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GrassGeometryShared.hlsl"

            half4 frag(GSOut IN) : SV_Target
            {
                float3 positionWS = IN.positionWS;
                // Flat shading: face normal from pixel derivatives. Keeps
                // the geometry output slim enough for more blades. The pass
                // is Cull Off, so flip the derived normal toward the camera
                // — otherwise blade backsides and mixed-winding ground
                // triangles light inverted.
                float3 normalWS = normalize(
                    cross(ddx(positionWS), ddy(positionWS)));
                if (dot(normalWS, GetWorldSpaceNormalizeViewDir(positionWS)) < 0.0)
                {
                    normalWS = -normalWS;
                }

                half4 albedo;
                if (IN.packed >= 0.0)
                {
                    // Blade: gradient alpha makes the tip taper away.
                    half t = IN.packed;
                    half alpha = 1.0 - t;
                    clip(alpha - _AlphaCutoff);

                    // Cheap per-blade brightness variation from world pos,
                    // faded in along the blade so the root stays exactly
                    // the ground colour.
                    half variation = lerp(
                        1.0,
                        0.85 + 0.3 * frac(
                            sin(dot(positionWS.xz,
                                    float2(12.9898, 78.233))) *
                            43758.5453),
                        t);
                    half3 tint = lerp(_GrassColor.rgb, _BladeTipColor.rgb, t);
                    albedo = half4(tint * variation, 1.0);
                }
                else
                {
                    // Ground: grass/ground blend by the R mask, so the
                    // surface under dense grass matches the blade roots.
                    half mask = -1.0 - IN.packed;
                    half band = 0.5 * saturate(_BlendSoftness);
                    half blend = smoothstep(
                        0.5 - band,
                        0.5 + band,
                        mask);
                    albedo = half4(
                        lerp(_GroundColor.rgb, _GrassColor.rgb, blend),
                        1.0);
                }

                // Toon shading: hard shadow band with a soft edge.
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half toonLight = smoothstep(
                    _ShadowThreshold,
                    _ShadowThreshold + _ShadowSoftness,
                    NdotL);
                toonLight *= mainLight.shadowAttenuation;
                half3 lit = albedo.rgb * mainLight.color;
                half3 shade = albedo.rgb * _AmbientColor.rgb *
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

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.0

            #define GRASS_SHADOW_PASS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "GrassGeometryShared.hlsl"

            half4 frag(GSOut IN) : SV_Target
            {
                if (IN.packed >= 0.0)
                {
                    clip(1.0 - IN.packed - _AlphaCutoff);
                }
                return 0.0;
            }
            ENDHLSL
        }
    }

    FallBack "Custom/GrassVertexColor"
}
