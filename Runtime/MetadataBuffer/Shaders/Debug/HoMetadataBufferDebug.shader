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

            float _HoMetadataBufferActive;
            float _HoMetadataBufferDebugMode;

            TEXTURE2D_X(_HoMetadataBufferMaskIdTexture);
            TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);

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
                    half4 values = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture, sampler_PointClamp, uv);
                    return values[customIndex];
                }

                return 0.0;
            }

            half GetObjectCustomValue(int customIndex, float2 uv)
            {
                if (customIndex < 4)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_PointClamp, uv);
                    return values[customIndex];
                }

                if (customIndex < 8)
                {
                    half4 values = SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_PointClamp, uv);
                    return values[customIndex - 4];
                }

                return 0.0;
            }

            half3 HashScalar(half value)
            {
                float id = ceil(saturate(value) * 255.0);
                return HashColor(float3(id, id * 2.17, id * 4.31)) * step(0.5, id);
            }

            half3 HashId(float3 value)
            {
                return HashColor(ceil(saturate(value) * 255.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_HoMetadataBufferActive < 0.5)
                {
                    return source;
                }

                int mode = (int)round(_HoMetadataBufferDebugMode);
                half4 maskId = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMaskIdTexture, sampler_PointClamp, uv);
                half valid = step(0.0001, maskId.r);

                if (mode == 1)
                {
                    return half4(maskId.rrr, 1.0);
                }

                if (mode == 2)
                {
                    half hasValue = saturate(max(max(step(0.0001, maskId.g), step(0.0001, maskId.b)), step(0.0001, maskId.a)));
                    return lerp(source, half4(HashId(maskId.gba), 1.0), valid * hasValue);
                }

                if (mode == 3)
                {
                    return half4(Heat(maskId.a) * step(0.0001, maskId.a), 1.0);
                }

                if (mode >= 4 && mode <= 7)
                {
                    half value = GetCustomValue(mode - 4, uv);
                    return half4(value, value, value, 1.0);
                }

                if (mode >= 8 && mode <= 15)
                {
                    half value = GetObjectCustomValue(mode - 8, uv);
                    return half4(value, value, value, 1.0);
                }

                if (mode == 16)
                {
                    half hasRsuv = saturate(max(max(step(0.0001, maskId.g), step(0.0001, maskId.b)), step(0.0001, maskId.a)));
                    return half4(maskId.gba * hasRsuv, 1.0);
                }

                if (mode == 17)
                {
                    half hasValue = step(0.0001, maskId.g);
                    return lerp(source, half4(HashScalar(maskId.g), 1.0), valid * hasValue);
                }

                if (mode == 18)
                {
                    half hasValue = step(0.0001, maskId.b);
                    return lerp(source, half4(HashScalar(maskId.b), 1.0), valid * hasValue);
                }

                if (mode == 19)
                {
                    half hasValue = step(0.0001, maskId.a);
                    return half4(Heat(maskId.a) * hasValue, 1.0);
                }

                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
