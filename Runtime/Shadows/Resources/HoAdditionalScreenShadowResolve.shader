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
            #pragma multi_compile_local_fragment _ _HO_TRANSPARENT_SHADOW_SURFACE _HO_CAPTURE_SHADOW_SURFACE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
            #define _ADDITIONAL_LIGHT_SHADOWS 1
            #define _AdditionalLightsShadowmapTexture _HoAdditionalScreenShadowAtlas
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #undef _AdditionalLightsShadowmapTexture

            #if defined(_HO_TRANSPARENT_SHADOW_SURFACE)
                Texture2D<uint2> _HoAdditionalScreenShadowDepth;
            #elif defined(_HO_CAPTURE_SHADOW_SURFACE)
                TEXTURE2D(_HoAdditionalScreenShadowDepth);
            #else
                TEXTURE2D_FLOAT(_HoAdditionalScreenShadowDepth);
            #endif
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
                uint2 pixel = (uint2)input.positionCS.xy;
                #if defined(_HO_TRANSPARENT_SHADOW_SURFACE)
                    uint2 surface = LOAD_TEXTURE2D(_HoAdditionalScreenShadowDepth, pixel);
                    if ((surface.y & 65535u) < 256u) return 1.0h;
                    float depth = asfloat(surface.x);
                #elif defined(_HO_CAPTURE_SHADOW_SURFACE)
                    float4 surface = LOAD_TEXTURE2D(_HoAdditionalScreenShadowDepth, pixel);
                    if (surface.a < 0.5 || surface.g < 1.0) return 1.0h;
                    float depth = surface.r;
                #else
                    float depth = LOAD_TEXTURE2D(_HoAdditionalScreenShadowDepth, pixel).r;
                #endif
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
