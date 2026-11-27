Shader "Hidden/HoShadows/AdditionalShadowReadback"
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_HoAdditionalProbeVisibility);
            TEXTURE2D_FLOAT(_HoAdditionalProbeDepth);
            float4 _HoAdditionalProbeIndices;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id);
                return output;
            }

            float ReadResult(uint2 pixel, int slot)
            {
                UNITY_BRANCH if (slot < 0)
                    return 1.0;
                return LOAD_TEXTURE2D_ARRAY(_HoAdditionalProbeVisibility, pixel, slot).r;
            }

            float NativeRealtime(float3 ws, int shaderIndex)
            {
                UNITY_BRANCH if (shaderIndex < 0)
                    return 1.0;
                Light light = GetAdditionalPerObjectLight(shaderIndex, ws);
                return AdditionalLightRealtimeShadow(shaderIndex, ws, light.direction);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int4 indices = (int4)_HoAdditionalProbeIndices;
                if (indices.z < 0 && indices.w < 0)
                    return 1.0;
                uint2 pixel = (uint2)input.positionCS.xy;
                float depth = LOAD_TEXTURE2D(_HoAdditionalProbeDepth, pixel).r;
                #if UNITY_REVERSED_Z
                    if (depth <= 0.0) return 1.0;
                #else
                    if (depth >= 1.0) return 1.0;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                return float4(ReadResult(pixel, indices.x), ReadResult(pixel, indices.y),
                    NativeRealtime(positionWS, indices.z), NativeRealtime(positionWS, indices.w));
            }
            ENDHLSL
        }
    }
}
