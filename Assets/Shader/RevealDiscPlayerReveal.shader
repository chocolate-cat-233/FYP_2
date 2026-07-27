Shader "SuperFinalFuse/RevealDisc/PlayerReveal"
{
    Properties
    {
        [HDR] _Color ("Reveal Color", Color) = (0.4, 0.8, 1.0, 0.85)
        _RimPower  ("Rim Power",  Range(0.1, 8.0)) = 2.5
        _RimScale  ("Rim Scale",  Range(0.0, 2.0)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
        }

        Pass
        {
            Name "PlayerReveal"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Back

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            half4 _Color;
            float _RimPower;
            float _RimScale;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float2 screenUV   : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS);
                VertexNormalInputs   nrmInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInput.positionCS;
                output.normalWS   = nrmInput.normalWS;
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(posInput.positionWS);

                float4 sp = ComputeScreenPos(output.positionCS);
                output.screenUV = sp.xy / sp.w;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // DEBUG: visualize depth values as color
                // R = scene depth, G = player depth
                float sceneRaw  = SampleSceneDepth(input.screenUV);
                float playerRaw = input.positionCS.z / input.positionCS.w;
                return half4(sceneRaw, playerRaw, 0, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
