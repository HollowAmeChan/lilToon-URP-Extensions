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

            float _HoShadowCastActive;
            int _HoShadowCastLightCount;
            int _HoShadowCastSliceCount;
            float4 _HoShadowCastAtlasSize;
            float4 _HoShadowCastSliceData[32];
            TEXTURE2D_FLOAT(_HoShadowCastAtlas);

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
                if (_HoShadowCastActive < 0.5 || _HoShadowCastSliceCount <= 0)
                {
                    return source;
                }

                float rawDepth = SAMPLE_TEXTURE2D(_HoShadowCastAtlas, sampler_PointClamp, uv);
                half valid = rawDepth < 0.99999;
                half3 depthColor = Heat(1.0 - rawDepth);

                half3 gridColor = half3(0.06, 0.16, 0.22);
                [unroll]
                for (int i = 0; i < 32; i++)
                {
                    if (i >= _HoShadowCastSliceCount)
                    {
                        break;
                    }

                    float4 slice = _HoShadowCastSliceData[i];
                    float2 minUv = slice.xy;
                    float2 maxUv = slice.xy + slice.zz;
                    float2 inside = step(minUv, uv) * step(uv, maxUv);
                    float inSlice = inside.x * inside.y;
                    float2 borderDist = min(uv - minUv, maxUv - uv) * _HoShadowCastAtlasSize.xy;
                    float border = inSlice * step(min(borderDist.x, borderDist.y), 2.0);
                    gridColor = lerp(gridColor, half3(1.0, 0.85, 0.25), border);
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
