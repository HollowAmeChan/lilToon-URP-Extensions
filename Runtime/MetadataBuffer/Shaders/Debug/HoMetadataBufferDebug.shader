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
            TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture);
            TEXTURE2D_X(_HoMetadataBufferMaterialCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);
            TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture);
            TEXTURE2D_X_FLOAT(_HoMetadataBufferMBufferDepthTexture);

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
                half4 surfaceData = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceDataTexture, sampler_PointClamp, uv);
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

                if (mode == 4)
                {
                    return half4(surfaceData.rrr, 1.0);
                }

                if (mode == 5)
                {
                    return half4(Heat(surfaceData.g) * step(0.0001, surfaceData.g), 1.0);
                }

                if (mode == 6)
                {
                    return half4(HashScalar(surfaceData.b), 1.0);
                }

                if (mode == 7)
                {
                    return half4(surfaceData.aaa, 1.0);
                }

                if (mode >= 8 && mode <= 11)
                {
                    half value = GetCustomValue(mode - 8, uv);
                    return half4(value, value, value, 1.0);
                }

                if (mode >= 12 && mode <= 19)
                {
                    half value = GetObjectCustomValue(mode - 12, uv);
                    return half4(value, value, value, 1.0);
                }

                if (mode == 20)
                {
                    half hasRsuv = saturate(max(max(step(0.0001, maskId.g), step(0.0001, maskId.b)), step(0.0001, maskId.a)));
                    return half4(maskId.gba * hasRsuv, 1.0);
                }

                if (mode == 21)
                {
                    half hasValue = step(0.0001, maskId.g);
                    return lerp(source, half4(HashScalar(maskId.g), 1.0), valid * hasValue);
                }

                if (mode == 22)
                {
                    half hasValue = step(0.0001, maskId.b);
                    return lerp(source, half4(HashScalar(maskId.b), 1.0), valid * hasValue);
                }

                if (mode == 23)
                {
                    half hasValue = step(0.0001, maskId.a);
                    return half4(Heat(maskId.a) * hasValue, 1.0);
                }

                if (mode == 24)
                {
                    half4 surfaceColor = SAMPLE_TEXTURE2D_X(_HoMetadataBufferSurfaceColorTexture, sampler_PointClamp, uv);
                    half surfaceCoverage = saturate(surfaceColor.a);
                    half surfaceValid = step(0.0001, maskId.r) * step(0.0001, surfaceCoverage);
                    half3 compositedColor = source.rgb * (1.0h - surfaceCoverage) + surfaceColor.rgb;
                    return half4(lerp(source.rgb, compositedColor, surfaceValid), 1.0);
                }

                if (mode == 25)
                {
                    float rawDepth = SAMPLE_TEXTURE2D_X(_HoMetadataBufferMBufferDepthTexture, sampler_PointClamp, uv).r;
                    half depthValid = step(0.0001h, abs(rawDepth - 1.0h));
                    half depth = saturate(Linear01Depth(rawDepth, _ZBufferParams));
                    return lerp(source, half4(depth, depth, depth, 1.0), depthValid);
                }

                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
