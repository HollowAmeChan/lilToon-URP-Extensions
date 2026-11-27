Shader "Hidden/HoShadows/NativeMainShadowReadback"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_HoProbeShadow);
            float _HoProbeValid;
            float _HoProbeCompareStock;
            float _HoProbeCascadeCount;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_HoProbeValid < 0.5)
                    return float4(1, 1, 0, 0);

                uint2 pixel = (uint2)input.positionCS.xy;
                float candidate = LOAD_TEXTURE2D_X(_HoProbeShadow, pixel).r;
                float stock = _HoProbeCompareStock > .5 ? LOAD_TEXTURE2D_X(_ScreenSpaceShadowmapTexture, pixel).r : candidate;
                float depth = LoadSceneDepth(pixel);
                #if UNITY_REVERSED_Z
                    if (depth <= 0.0) return float4(candidate, stock, 1, 0);
                #else
                    if (depth >= 1.0) return float4(candidate, stock, 1, 0);
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 ws = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                float cascade = _HoProbeCascadeCount > 1 ? ComputeCascadeIndex(ws) : 0.0;
                return float4(candidate, stock, 1, (cascade + 1.0) / 8.0);
            }
            ENDHLSL
        }
    }
}
