Shader "Hidden/HoShadows/NativeMainResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D_FLOAT(_HoNativeMainShadowDepth);
            TEXTURE2D_SHADOW(_HoNativeMainShadowAtlas);
            float _HoNativeMainCascadeCount;

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

            half Frag(Varyings input) : SV_Target
            {
                float depth = LOAD_TEXTURE2D(_HoNativeMainShadowDepth, uint2(input.positionCS.xy)).r;
                #if UNITY_REVERSED_Z
                    if (depth <= 0.0) return 1.0h;
                #else
                    if (depth >= 1.0) return 1.0h;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif

                // Use native cascade selection and filter parameters with the atlas bound to this pass.
                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                int cascade = _HoNativeMainCascadeCount > 1.0 ? (int)ComputeCascadeIndex(positionWS) : 0;
                float4 coords = float4(mul(_MainLightWorldToShadow[cascade], float4(positionWS, 1.0)).xyz, 0.0);
                return SampleShadowmap(TEXTURE2D_ARGS(_HoNativeMainShadowAtlas, sampler_LinearClampCompare),
                    coords, GetMainLightShadowSamplingData(), GetMainLightShadowParams(), false);
            }
            ENDHLSL
        }
    }
}
