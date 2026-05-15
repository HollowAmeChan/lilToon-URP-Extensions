Shader "Hidden/lilToon-Shoost/URP/Shoost/CRTEffects"
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
            Name "Shoost CRT Effects"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCrtEffects

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x mode, y resolution scale, z scanline brightness
            float _LayerTextureEnabled;
            TEXTURE2D_X(_LayerTexture);

            half4 FragCrtEffects(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001)
                {
                    return original;
                }

                float mode = clamp(round(_LayerParams0.x), 0.0, 3.0);
                float resolutionScale = max(_LayerParams0.y, 0.01);
                float2 baseResolution = float2(256.0, 240.0);
                float screenRatio = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float pixelAspectRatio = screenRatio / (baseResolution.x / baseResolution.y);
                float2 targetResolution = max(round(float2(baseResolution.x * pixelAspectRatio, baseResolution.y) * resolutionScale), 16.0);
                float2 pixelUv = floor(input.texcoord * targetResolution) / targetResolution;

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, pixelUv);
                float2 scanlineUv = input.texcoord * targetResolution;
                float4 scanlineSample = _LayerTextureEnabled > 0.5 ? SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearRepeat, scanlineUv) : float4(1.0, 1.0, 1.0, 1.0);

                float slotMaskIntensity = 1.0;
                float shadowMaskIntensity = 1.0;
                float scanlineBrightness = max(_LayerParams0.z, mode <= 1.0 ? 3.0 : 1.5);
                float glowThreshold = 0.5;
                float glowAmount = 2.0;

                float shadowMask = 1.0 - (1.0 - scanlineSample.a) * shadowMaskIntensity;
                float3 slotMask = saturate(scanlineSample.rgb + 1.0 - slotMaskIntensity);
                float3 result = slotMask * shadowMask * source.rgb * (scanlineBrightness + 1.0);
                float luma = dot(result, float3(0.3, 0.59, 0.11));
                float glow = saturate(luma - glowThreshold) * glowAmount + 1.0;
                result = max(result * glow, 0.0);

                result = lerp(original.rgb, max(result, 0.0), amount);
                return half4(result, original.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
