Shader "Hidden/lilToon/URP/ScreenProcess/RTLighting"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ScreenProcess RT Lighting"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RT/Shaders/HoRTPrimarySurface.hlsl"

            TEXTURE2D_X(_HoSPRTDirectDiffuse);
            TEXTURE2D_X(_HoSPRTDirectSpecular);
            TEXTURE2D_X(_HoSPRTIndirectDiffuse);
            TEXTURE2D_FLOAT(_CameraDepthTexture);

            float _HoSPRTDirectWeight;
            float _HoSPRTDirectMode;
            float _HoSPRTIndirectIntensity;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                HoRTPrimarySurface surface;
                if (!HoRTTryLoadPrimarySurfaceAtUV(uv, surface))
                    return original;

                float rawDepth = SAMPLE_TEXTURE2D_LOD(
                    _CameraDepthTexture,
                    sampler_PointClamp,
                    uv,
                    0).r;
                float cameraDepth = HoSurfaceDeviceToEyeDepth(
                    rawDepth,
                    _HoRTPrimaryZBufferParameters,
                    _HoRTPrimaryClipParameters);
                if (abs(cameraDepth - surface.eyeDepth) >
                    max(0.0001, abs(surface.eyeDepth) * 0.00001))
                {
                    return original;
                }

                float4 directDiffuse = SAMPLE_TEXTURE2D_X(
                    _HoSPRTDirectDiffuse,
                    sampler_PointClamp,
                    uv);
                float4 directSpecular = SAMPLE_TEXTURE2D_X(
                    _HoSPRTDirectSpecular,
                    sampler_PointClamp,
                    uv);
                float4 indirectDiffuse = SAMPLE_TEXTURE2D_X(
                    _HoSPRTIndirectDiffuse,
                    sampler_PointClamp,
                    uv);

                bool directValid = _HoSPRTDirectWeight > 0.0 &&
                    directDiffuse.a >= 0.5 &&
                    directSpecular.a >= 0.5;
                bool indirectValid = _HoSPRTIndirectIntensity > 0.0 &&
                    indirectDiffuse.a >= 0.5;
                if (!directValid && !indirectValid)
                    return original;

                float fog = InitializeInputDataFog(float4(surface.positionWS, 1), 0);
                float3 result = original.rgb;

                if (directValid)
                {
                    if (_HoSPRTDirectMode > 0.5)
                    {
                        float3 extra = MixFogColor(
                            (directDiffuse.rgb + directSpecular.rgb) * _HoSPRTDirectWeight,
                            float3(0, 0, 0),
                            fog);
                        #if defined(UNITY_COLORSPACE_GAMMA)
                            result = LinearToSRGB(SRGBToLinear(result) + extra);
                        #else
                            result += extra;
                        #endif
                    }
                    else
                    {
                        float3 replacement = directDiffuse.rgb +
                            directSpecular.rgb +
                            surface.emission;
                        #if defined(UNITY_COLORSPACE_GAMMA)
                            replacement = LinearToSRGB(replacement);
                        #endif
                        replacement = MixFog(replacement, fog);
                        result = lerp(
                            result,
                            replacement,
                            saturate(_HoSPRTDirectWeight));
                    }
                }

                if (indirectValid)
                {
                    float3 extra = MixFogColor(
                        indirectDiffuse.rgb * _HoSPRTIndirectIntensity,
                        float3(0, 0, 0),
                        fog);
                    #if defined(UNITY_COLORSPACE_GAMMA)
                        result = LinearToSRGB(SRGBToLinear(result) + extra);
                    #else
                        result += extra;
                    #endif
                }

                return float4(result, original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
