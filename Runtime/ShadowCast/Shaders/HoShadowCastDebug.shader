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
            float4 _HoShadowCastSecondDirectionalSliceData[16];
            TEXTURE2D_FLOAT(_HoShadowCastAtlas);
            TEXTURE2D_FLOAT(_HoShadowCastSecondDirectionalAtlas);

            half3 Heat(float value)
            {
                value = saturate(value);
                return saturate(half3(value * 2.0, 1.0 - abs(value - 0.5) * 2.0, (1.0 - value) * 2.0));
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
                float4 atlasSize = debugSecondDirectional ? _HoShadowCastSecondDirectionalAtlasSize : _HoShadowCastAtlasSize;
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
                half3 depthColor = Heat(1.0 - rawDepth);

                half3 gridColor = half3(0.06, 0.16, 0.22);
                if (debugSecondDirectional)
                {
                    [unroll]
                    for (int i = 0; i < 16; i++)
                    {
                        if (i >= sliceCount)
                        {
                            break;
                        }

                        float4 slice = _HoShadowCastSecondDirectionalSliceData[i];
                        float2 minUv = slice.xy;
                        float2 maxUv = slice.xy + slice.zz;
                        float2 inside = step(minUv, uv) * step(uv, maxUv);
                        float inSlice = inside.x * inside.y;
                        float2 borderDist = min(uv - minUv, maxUv - uv) * atlasSize.xy;
                        float border = inSlice * step(min(borderDist.x, borderDist.y), 2.0);
                        gridColor = lerp(gridColor, half3(1.0, 0.85, 0.25), border);
                    }
                }
                else
                {
                    [unroll]
                    for (int i = 0; i < 32; i++)
                    {
                        if (i >= sliceCount)
                        {
                            break;
                        }

                        float4 slice = _HoShadowCastSliceData[i];
                        float2 minUv = slice.xy;
                        float2 maxUv = slice.xy + slice.zz;
                        float2 inside = step(minUv, uv) * step(uv, maxUv);
                        float inSlice = inside.x * inside.y;
                        float2 borderDist = min(uv - minUv, maxUv - uv) * atlasSize.xy;
                        float border = inSlice * step(min(borderDist.x, borderDist.y), 2.0);
                        gridColor = lerp(gridColor, half3(1.0, 0.85, 0.25), border);
                    }
                }

                half3 atlasColor = lerp(depthColor, half3(0.0, 0.0, 0.0), 1.0 - valid);
                atlasColor = max(atlasColor, gridColor);
                return half4(atlasColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
