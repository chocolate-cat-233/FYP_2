Shader "SuperFinalFuse/RevealDisc/StencilWrite"
{
    // Fullscreen pass — stamps a screen-space disc into the stencil buffer.
    // The disc is centered on the player's screen position and sized
    // according to the world-space radius projected to screen space.
    //
    // This is drawn as a fullscreen triangle (3 vertices via DrawProcedural).
    // Fragments outside the disc are discarded → stencil NOT written there.
    Properties
    {
        _EdgeSoftness ("Edge Softness", Range(0.0, 0.3)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "StencilWrite"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off

            Stencil
            {
                Ref  64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Global properties set by RevealDiscController
            float  _RevealDiscActive;
            float2 _RevealDiscScreenPos;     // 0–1, player's viewport position
            float  _RevealDiscScreenRadius;  // 0–1, disc radius in viewport space
            float  _EdgeSoftness;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV    : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                // Fullscreen triangle covering the entire viewport.
                // Vertices: (-1,-1), (3,-1), (-1,3)
                Varyings output;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(uv * float2(2.0, -2.0)
                                          + float2(-1.0, 1.0), 0.0, 1.0);
                output.screenUV = uv;
                return output;
            }

            void Frag(Varyings input)
            {
                if (_RevealDiscActive < 0.5) discard;

                float2 delta = input.screenUV - _RevealDiscScreenPos;
                float  dist  = length(delta);
                float  radius = max(_RevealDiscScreenRadius, 0.001);

                // Smooth edge falloff
                float edge = 1.0 - smoothstep(radius - _EdgeSoftness, radius, dist);
                clip(edge - 0.001);

                // Stencil { Ref 64 Pass Replace } writes the stencil mark.
                // ColorMask 0 means no color output.
            }
            ENDHLSL
        }
    }

    FallBack Off
}
