Shader "Hidden/lilToon-HoShadowCast/URP/DebugView"
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
            Name "HoShadowCast Debug View"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            int _HoShadowCastDebugMode;
            float _HoShadowCastActive;
            int _HoShadowCastSliceCount;
            float4 _HoShadowCastAtlasSize;
            float4 _HoShadowCastSliceData[32];
            float4 _HoShadowCastSecondDirectionalParams;
            float4 _HoShadowCastSecondDirectionalAtlasSize;
            float4 _HoShadowCastSecondDirectionalLightData[4];
            float4 _HoShadowCastSecondDirectionalSliceData[16];
            TEXTURE2D_FLOAT(_HoShadowCastAtlas);
            TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas);

            half3 Heat(float value)
            {
                value = saturate(value);
                return saturate(half3(value * 2.0, 1.0 - abs(value - 0.5) * 2.0, (1.0 - value) * 2.0));
            }

            half3 ShadowDepthRamp(float value)
            {
                value = saturate(value);
                half3 farColor = half3(0.055h, 0.070h, 0.085h);
                half3 midColor = half3(0.200h, 0.330h, 0.400h);
                half3 nearColor = half3(0.780h, 0.620h, 0.340h);
                half midWeight = smoothstep(0.05h, 0.70h, value);
                half nearWeight = smoothstep(0.68h, 1.00h, value);
                return lerp(lerp(farColor, midColor, midWeight), nearColor, nearWeight);
            }

            float RectLine(float2 uv, float4 rect, float lineUv)
            {
                float2 minUv = rect.xy;
                float2 maxUv = rect.xy + rect.zw;
                if (any(uv < minUv) || any(uv > maxUv))
                {
                    return 0.0;
                }

                float2 edgeDistance = min(uv - minUv, maxUv - uv);
                return 1.0 - step(lineUv, min(edgeDistance.x, edgeDistance.y));
            }

            float SecondDirectionalBlockLine(float2 uv, int firstSlice, int sliceCount, float lineUv)
            {
                if (sliceCount <= 0)
                {
                    return 0.0;
                }

                float2 blockMin = float2(1.0, 1.0);
                float2 blockMax = float2(0.0, 0.0);
                [unroll]
                for (int sliceOffset = 0; sliceOffset < 4; sliceOffset++)
                {
                    if (sliceOffset >= sliceCount)
                    {
                        break;
                    }

                    int sliceIndex = firstSlice + sliceOffset;
                    if (sliceIndex < 0 || sliceIndex >= 16)
                    {
                        continue;
                    }

                    float4 slice = _HoShadowCastSecondDirectionalSliceData[sliceIndex];
                    if (slice.z <= 0.0)
                    {
                        continue;
                    }

                    blockMin = min(blockMin, slice.xy);
                    blockMax = max(blockMax, slice.xy + slice.zz);
                }

                if (any(blockMax <= blockMin))
                {
                    return 0.0;
                }

                return RectLine(uv, float4(blockMin, max(blockMax - blockMin, float2(0.0, 0.0))), lineUv);
            }

            half3 ApplySliceOverlay(float2 uv, half3 color)
            {
                float lineUv = max(max(_HoShadowCastAtlasSize.z, _HoShadowCastAtlasSize.w) * 2.0, 0.001);
                int sliceCount = min(_HoShadowCastSliceCount, 32);
                float sliceLine = 0.0;

                [unroll]
                for (int i = 0; i < 32; i++)
                {
                    if (i >= sliceCount)
                    {
                        break;
                    }

                    float4 slice = _HoShadowCastSliceData[i];
                    sliceLine = max(sliceLine, RectLine(uv, float4(slice.xy, slice.zz), lineUv));
                }

                return lerp(color, half3(0.34h, 0.78h, 0.86h), saturate(sliceLine * 0.72));
            }

            half3 ApplySecondDirectionalOverlay(float2 uv, half3 color)
            {
                float atlasTexel = max(_HoShadowCastSecondDirectionalAtlasSize.z, _HoShadowCastSecondDirectionalAtlasSize.w);
                float cascadeLineUv = max(atlasTexel * 2.0, 0.001);
                float blockLineUv = max(atlasTexel * 4.0, 0.0015);
                int sliceCount = min((int)round(_HoShadowCastSecondDirectionalParams.y) * (int)round(_HoShadowCastSecondDirectionalParams.z), 16);
                int lightCount = min((int)round(_HoShadowCastSecondDirectionalParams.y), 4);
                float cascadeLine = 0.0;
                float blockLine = 0.0;

                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    if (i >= sliceCount)
                    {
                        break;
                    }

                    float4 slice = _HoShadowCastSecondDirectionalSliceData[i];
                    cascadeLine = max(cascadeLine, RectLine(uv, float4(slice.xy, slice.zz), cascadeLineUv));
                }

                [unroll]
                for (int lightIndex = 0; lightIndex < 4; lightIndex++)
                {
                    if (lightIndex >= lightCount)
                    {
                        break;
                    }

                    int firstSlice = (int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].x);
                    int perLightSliceCount = min((int)round(_HoShadowCastSecondDirectionalLightData[lightIndex].y), 4);
                    blockLine = max(blockLine, SecondDirectionalBlockLine(uv, firstSlice, perLightSliceCount, blockLineUv));
                }

                color = lerp(color, half3(0.72h, 0.50h, 0.25h), saturate(cascadeLine * 0.60));
                return lerp(color, half3(0.92h, 0.80h, 0.34h), saturate(blockLine * 0.85));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                bool debugSecondDirectional = _HoShadowCastDebugMode == 2;
                float active = debugSecondDirectional ? _HoShadowCastSecondDirectionalParams.x : _HoShadowCastActive;
                int sliceCount = debugSecondDirectional
                    ? (int)round(_HoShadowCastSecondDirectionalParams.y) * (int)round(_HoShadowCastSecondDirectionalParams.z)
                    : _HoShadowCastSliceCount;
                if (active < 0.5 || sliceCount <= 0)
                {
                    return source;
                }

                float rawDepth = 1.0;
                if (debugSecondDirectional)
                {
                    rawDepth = SAMPLE_TEXTURE2D(_HoShadowCastSecondDirectionalAtlas, sampler_PointClamp, uv);
                }
                else
                {
                    rawDepth = SAMPLE_TEXTURE2D(_HoShadowCastAtlas, sampler_PointClamp, uv);
                }
                half valid = rawDepth < 0.99999;
                half3 depthColor = ShadowDepthRamp(1.0 - rawDepth);

                half3 atlasColor = lerp(depthColor, half3(0.015h, 0.018h, 0.022h), 1.0h - valid);
                atlasColor = debugSecondDirectional
                    ? ApplySecondDirectionalOverlay(uv, atlasColor)
                    : ApplySliceOverlay(uv, atlasColor);
                return half4(atlasColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
