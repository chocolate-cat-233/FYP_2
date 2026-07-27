Shader "SuperFinalFuse/RevealDisc/Decal"
{
    // Cosmetic world-space disc drawn around the player's feet.
    // Renders a glowing rim and a subtle inner fill.
    // Placed on a flat horizontal disc mesh; reads global
    // properties broadcast by RevealDiscController.
    Properties
    {
        [HDR] _RimColor ("Rim Color", Color) = (1.0, 0.85, 0.2, 0.9)
        [HDR] _FillColor ("Fill Color", Color) = (1.0, 0.9, 0.5, 0.15)
        _RimWidth ("Rim Width", Range(0.0, 0.5)) = 0.08
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        _PulseAmount ("Pulse Amount", Range(0.0, 0.3)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            Stencil
            {
                Ref  64
                Comp Equal
                Pass Keep
                Fail Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Global reveal disc properties
            float  _RevealDiscActive;
            float3 _RevealDiscCenter;
            float  _RevealDiscRadius;

            // Material properties
            half4 _RimColor;
            half4 _FillColor;
            float _RimWidth;
            float _PulseSpeed;
            float _PulseAmount;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = mul(unity_ObjectToWorld, float4(input.positionOS, 1.0)).xyz;
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Hidden when inactive
                if (_RevealDiscActive < 0.5) discard;

                float discRadius = max(_RevealDiscRadius, 0.001);
                float3 toCenter = input.positionWS - _RevealDiscCenter;
                float distXZ = length(toCenter.xz);

                // Normalized distance from center (0 at center, 1 at edge)
                float normDist = distXZ / discRadius;

                // Fade out beyond the disc edge
                float edgeFade = 1.0 - smoothstep(1.0 - _RimWidth, 1.0 + _RimWidth * 0.5, normDist);
                clip(edgeFade - 0.001);

                // Rim — bright ring at the outer edge
                float rimMask = smoothstep(1.0 - _RimWidth, 1.0, normDist);
                rimMask *= 1.0 - smoothstep(1.0, 1.0 + _RimWidth * 0.5, normDist);

                // Inner fill — subtle glow inside
                float fillMask = 1.0 - smoothstep(0.0, 0.98, normDist);
                fillMask *= 1.0 - smoothstep(0.98, 1.0, normDist);

                // Pulse animation on the rim
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                half4 rimContrib = _RimColor * rimMask * pulse;
                half4 fillContrib = _FillColor * fillMask;

                half4 color = rimContrib + fillContrib;
                color.a *= edgeFade;

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
