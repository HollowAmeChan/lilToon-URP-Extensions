Shader "Hidden/lilToon/URP/MetadataBuffer/DebugView"
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
            Name "MetadataBuffer Debug View"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AOV/Shaders/HoAOV/HoAovSampling.hlsl"

            float _lilHoAovActive;
            float _HoAovDebugMode;
            float4 _HoAovDebugDepthParams; // x near, y far, z inv range

            TEXTURE2D_X(_lilHoAovMaskIdTexture);
            TEXTURE2D_X(_lilHoAovNormalDepthTexture);
            TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
            TEXTURE2D_X(_lilHoAovCustom0_3Texture);
            TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture);
            TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture);
            TEXTURE2D_X(_lilHoAovSssTexture);

            half3 HashColor(float3 value)
            {
                half r = frac(sin(dot(value, float3(12.9898, 78.233, 37.719))) * 43758.5453);
                half g = frac(sin(dot(value, float3(39.3468, 11.135, 83.155))) * 24634.6345);
                half b = frac(sin(dot(value, float3(73.1567, 52.235, 9.151))) * 14578.2341);
                return half3(r, g, b);
            }

            half3 Heat(float value)
            {
                value = saturate(value);
                return saturate(half3(value * 2.0, 1.0 - abs(value - 0.5) * 2.0, (1.0 - value) * 2.0));
            }

            half GetCustomValue(int customIndex, float2 uv)
            {
                if (customIndex < 4)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_lilHoAovCustom0_3Texture, sampler_PointClamp, uv);
                    return values[customIndex];
                }

                return 0.0;
            }

            half GetObjectCustomValue(int customIndex, float2 uv)
            {
                if (customIndex < 4)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture, sampler_PointClamp, uv);
                    return values[customIndex];
                }

                if (customIndex < 8)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture, sampler_PointClamp, uv);
                    return values[customIndex - 4];
                }

                return 0.0;
            }

            half GetObjectCustomAny(float2 uv)
            {
                half4 objectCustom0 = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom0_3Texture, sampler_PointClamp, uv);
                half4 objectCustom1 = SAMPLE_TEXTURE2D_X(_lilHoAovObjectCustom4_7Texture, sampler_PointClamp, uv);
                half sum = dot(step(0.0001, objectCustom0), half4(1.0, 1.0, 1.0, 1.0))
                    + dot(step(0.0001, objectCustom1), half4(1.0, 1.0, 1.0, 1.0));
                return saturate(sum);
            }

            half3 HashScalar(half value)
            {
                return HashColor(float3(value, value * 2.17, value * 4.31)) * step(0.0001, value);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_lilHoAovActive < 0.5)
                {
                    return source;
                }

                int mode = (int)round(_HoAovDebugMode);
                half4 maskId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_PointClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_PointClamp, uv);
                half4 surfaceData = SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_PointClamp, uv);

                if (mode == 1)
                {
                    return half4(maskId.rrr, 1.0);
                }

                if (mode == 2)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasValue = saturate(max(max(step(0.0001, maskId.g), step(0.0001, maskId.b)), step(0.0001, maskId.a)));
                    return lerp(source, half4(HashColor(maskId.gba), 1.0), valid * hasValue);
                }

                if (mode == 3)
                {
                    return half4(Heat(maskId.a) * step(0.0001, maskId.a), 1.0);
                }

                if (mode == 4)
                {
                    half depth = saturate((LilHoAovLinearDepthOrFar(normalDepth, _HoAovDebugDepthParams.y) - _HoAovDebugDepthParams.x) * _HoAovDebugDepthParams.z);
                    return half4(depth, depth, depth, 1.0);
                }

                if (mode == 5)
                {
                    return half4(LilHoAovEncodedNormalOrBlack(normalDepth), 1.0);
                }

                if (mode == 6)
                {
                    return half4(0.0, 0.0, 0.0, 1.0);
                }

                if (mode == 7)
                {
                    return half4(surfaceData.rrr, 1.0);
                }

                if (mode == 8)
                {
                    return half4(Heat(surfaceData.g) * step(0.0001, surfaceData.g), 1.0);
                }

                if (mode == 9)
                {
                    return half4(HashColor(float3(surfaceData.b, surfaceData.b * 2.17, surfaceData.b * 4.31)) * step(0.0001, surfaceData.b), 1.0);
                }

                if (mode == 10)
                {
                    return half4(surfaceData.aaa, 1.0);
                }

                if (mode >= 11 && mode <= 14)
                {
                    half value = GetCustomValue(mode - 11, uv);
                    half valid = step(0.0001, maskId.r);
                    return lerp(source, half4(value, value, value, 1.0), valid);
                }

                if (mode >= 15 && mode <= 22)
                {
                    half value = GetObjectCustomValue(mode - 15, uv);
                    half valid = step(0.0001, maskId.r);
                    return lerp(source, half4(value, value, value, 1.0), valid);
                }

                if (mode == 23)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasRsuv = saturate(max(max(step(0.0001, maskId.g), step(0.0001, maskId.b)), step(0.0001, maskId.a)));
                    return lerp(source, half4(maskId.gba, 1.0), valid * hasRsuv);
                }

                if (mode == 24)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasValue = step(0.0001, maskId.g);
                    return lerp(source, half4(HashScalar(maskId.g), 1.0), valid * hasValue);
                }

                if (mode == 25)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasValue = step(0.0001, maskId.b);
                    return lerp(source, half4(HashScalar(maskId.b), 1.0), valid * hasValue);
                }

                if (mode == 26)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasValue = step(0.0001, maskId.a);
                    return lerp(source, half4(Heat(maskId.a), 1.0), valid * hasValue);
                }

                if (mode == 27)
                {
                    half valid = step(0.0001, maskId.r);
                    half hasId = saturate(max(step(0.0001, maskId.g), step(0.0001, maskId.b)));
                    half noObjectCustom = 1.0 - GetObjectCustomAny(uv);
                    half selected = valid * hasId * noObjectCustom;
                    return lerp(source, half4(0.15, 0.78, 1.0, 1.0), selected);
                }

                if (mode == 28)
                {
                    half4 sss = SAMPLE_TEXTURE2D_X(_lilHoAovSssTexture, sampler_PointClamp, uv);
                    half valid = step(0.0001, maskId.r) * step(0.0001, sss.a);
                    return lerp(source, half4(sss.rgb, 1.0), valid);
                }

                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
