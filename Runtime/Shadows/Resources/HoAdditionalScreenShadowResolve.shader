Shader "Hidden/HoShadows/AdditionalResolve"
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
            #define _ADDITIONAL_LIGHT_SHADOWS 1
            #define _AdditionalLightsShadowmapTexture _HoAdditionalScreenShadowAtlas
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #undef _AdditionalLightsShadowmapTexture

            TEXTURE2D_FLOAT(_HoAdditionalScreenShadowDepth);
            int _HoAdditionalScreenShadowLightIndex;
            float3 _HoAdditionalScreenShadowLightPosition;

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
                float depth = LOAD_TEXTURE2D(_HoAdditionalScreenShadowDepth, uint2(input.positionCS.xy)).r;
                #if UNITY_REVERSED_Z
                    if (depth <= 0.0) return 1.0h;
                #else
                    if (depth >= 1.0) return 1.0h;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif

                float3 positionWS = ComputeWorldSpacePosition(input.uv, depth, UNITY_MATRIX_I_VP);
                half3 lightDirection = SafeNormalize(_HoAdditionalScreenShadowLightPosition - positionWS);
                return AdditionalLightRealtimeShadow(_HoAdditionalScreenShadowLightIndex, positionWS, lightDirection);
            }
            ENDHLSL
        }
    }
}
