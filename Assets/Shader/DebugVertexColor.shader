Shader "Custom/DebugVertexColor"
{
    // Editor debug view of mesh vertex colors (unlit, no lighting).
    //
    // _DebugChannel:
    //   0 = RGB raw
    //   1 = R channel (red on black) — grass mask for GrassVertexColor
    //   2 = G channel (green on black) — wind amount
    //   3 = B channel (blue on black)
    //   4 = A channel (grayscale)
    //
    // The Vertex Color Brush window (Tools > Vertex Color Brush) applies
    // this shader as a temporary hover preview.

    Properties
    {
        _DebugChannel ("Channel (0=RGB 1=R 2=G 3=B 4=A)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DebugChannel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 vertexColor : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.vertexColor = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = IN.vertexColor;
                half v;

                if (_DebugChannel >= 4.5)
                {
                    v = c.a;
                    return half4(v, v, v, 1.0);
                }
                if (_DebugChannel >= 3.5)
                {
                    v = c.b;
                    return half4(0.0, 0.0, v, 1.0);
                }
                if (_DebugChannel >= 2.5)
                {
                    v = c.g;
                    return half4(0.0, v, 0.0, 1.0);
                }
                if (_DebugChannel >= 1.5)
                {
                    v = c.r;
                    return half4(v, 0.0, 0.0, 1.0);
                }

                return half4(c.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
