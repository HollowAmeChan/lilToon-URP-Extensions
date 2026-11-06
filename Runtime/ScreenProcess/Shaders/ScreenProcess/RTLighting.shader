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

            float4 _HoSPRTBlend; // mode, opacity, exposure*gain, indirect
            float4 _HoSPRTComponents; // diffuse, specular, retained emission, receiver
            float4 _HoSPRTTint;
            float4 _HoSPRTDepthRange;

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

                bool directValid = _HoSPRTBlend.w < 0.5 &&
                    directDiffuse.a >= 0.5 &&
                    directSpecular.a >= 0.5;
                bool indirectValid = _HoSPRTBlend.w >= 0.5 &&
                    indirectDiffuse.a >= 0.5;
                if (!directValid && !indirectValid)
                    return original;

                float receive = 1;
                if (_HoSPRTComponents.w > 1.5) receive = saturate(surface.metallic);
                else if (_HoSPRTComponents.w > 0.5) receive = 1 - saturate(surface.metallic);
                if (_HoSPRTDepthRange.y > _HoSPRTDepthRange.x)
                    receive *= step(_HoSPRTDepthRange.x, surface.eyeDepth) * step(surface.eyeDepth, _HoSPRTDepthRange.y);
                float opacity = saturate(_HoSPRTBlend.y) * receive;
                if (opacity <= 0) return original;
                // Products already contain the receiver BRDF, albedo and ray visibility.
                float3 light = indirectValid ? indirectDiffuse.rgb * _HoSPRTComponents.x :
                    directDiffuse.rgb * _HoSPRTComponents.x + directSpecular.rgb * _HoSPRTComponents.y;
                light *= _HoSPRTTint.rgb * _HoSPRTBlend.z;
                if (!HoRTPrimaryFinite3(light)) return original;
                light = max(light, 0);
                float fog = InitializeInputDataFog(float4(surface.positionWS, 1), 0);
                float3 scene = original.rgb;
                #if defined(UNITY_COLORSPACE_GAMMA)
                    scene = SRGBToLinear(scene);
                #endif
                float3 extra = MixFogColor(light, float3(0, 0, 0), fog);
                float3 fogColor = unity_FogColor.rgb;
                #if defined(UNITY_COLORSPACE_GAMMA)
                    fogColor = SRGBToLinear(fogColor);
                #endif
                float3 target;
                if (_HoSPRTBlend.x < 0.5) // Replace: preserve primary emission exactly once.
                    target = MixFogColor(light + surface.emission * _HoSPRTComponents.z, fogColor, fog);
                else if (_HoSPRTBlend.x < 1.5)
                    target = scene + extra;
                else if (_HoSPRTBlend.x < 2.5)
                    target = max(scene, extra);
                else if (_HoSPRTBlend.x < 3.5)
                    // Fog affects the new modulation only; fogged pixels approach identity.
                    target = scene * MixFogColor(light, float3(1, 1, 1), fog);
                else
                    target = scene + extra / (1 + dot(extra, float3(0.2126, 0.7152, 0.0722)));
                float3 result = lerp(scene, target, opacity);
                #if defined(UNITY_COLORSPACE_GAMMA)
                    result = LinearToSRGB(result);
                #endif
                return float4(result, original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
