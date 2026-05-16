Shader "Hidden/lilToon-HoAOV/URP/DebugView"
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
            Name "HoAOV Debug View"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _lilHoAovActive;
            float _HoAovDebugMode;
            float4 _HoAovDebugDepthParams; // x near, y far, z inv range

            TEXTURE2D_X(_lilHoAovMaskIdTexture);
            TEXTURE2D_X(_lilHoAovNormalDepthTexture);
            TEXTURE2D_X(_lilHoAovSurfaceDataTexture);
            TEXTURE2D_X(_lilHoAovCustom0_3Texture);
            TEXTURE2D_X(_lilHoAovCustom4_7Texture);
            TEXTURE2D_X(_lilHoAovCustom8_11Texture);

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
                    half4 values = SAMPLE_TEXTURE2D_X(_lilHoAovCustom0_3Texture, sampler_LinearClamp, uv);
                    return values[customIndex];
                }

                return 0.0;
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
                half4 maskId = SAMPLE_TEXTURE2D_X(_lilHoAovMaskIdTexture, sampler_LinearClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_LinearClamp, uv);
                half4 surfaceData = SAMPLE_TEXTURE2D_X(_lilHoAovSurfaceDataTexture, sampler_LinearClamp, uv);

                if (mode == 1)
                {
                    return half4(maskId.rrr, 1.0);
                }

                if (mode == 2)
                {
                    return half4(HashColor(maskId.gba), 1.0);
                }

                if (mode == 3)
                {
                    return half4(Heat(maskId.a) * step(0.0001, maskId.a), 1.0);
                }

                if (mode == 4)
                {
                    half depth = saturate((normalDepth.a - _HoAovDebugDepthParams.x) * _HoAovDebugDepthParams.z);
                    return half4(depth, depth, depth, 1.0);
                }

                if (mode == 5)
                {
                    return half4(normalDepth.rgb, 1.0);
                }

                if (mode == 6 || mode == 7 || mode == 8 || mode == 9 || mode == 10 || mode == 11 || mode == 12)
                {
                    if (mode == 6)
                    {
                        float3 normalWS = normalize((float3)normalDepth.rgb * 2.0 - 1.0);
                        float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                        return half4(normalVS * 0.5 + 0.5, 1.0);
                    }

                    if (mode == 7)
                    {
                        return half4(normalDepth.rgb, 1.0);
                    }

                    if (mode == 8)
                    {
                        return half4(0.0, 0.0, 0.0, 1.0);
                    }

                    if (mode == 9)
                    {
                        return half4(surfaceData.rrr, 1.0);
                    }

                    if (mode == 10)
                    {
                        return half4(Heat(surfaceData.g) * step(0.0001, surfaceData.g), 1.0);
                    }

                    if (mode == 11)
                    {
                        return half4(HashColor(float3(surfaceData.b, surfaceData.b * 2.17, surfaceData.b * 4.31)) * step(0.0001, surfaceData.b), 1.0);
                    }

                    return half4(surfaceData.aaa, 1.0);
                }

                if (mode >= 13 && mode <= 24)
                {
                    half value = GetCustomValue(mode - 13, uv);
                    return half4(value, value, value, 1.0);
                }

                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
