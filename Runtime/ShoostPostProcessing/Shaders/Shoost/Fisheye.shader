Shader "Hidden/lilToon-Shoost/URP/Shoost/Fisheye"
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
            Name "Shoost Fisheye"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFisheye

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0;
            float4 _LayerParams1; // x RT scale, y auto fill black edges

            half4 FragFisheye(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float distortion = saturate(_LayerParams0.x);
                float rtScale = _LayerParams1.x <= 0.0001 ? 1.0 : max(_LayerParams1.x, 0.01);
                float autoFillEnabled = step(0.5, _LayerParams1.y);
                float autoFillScale = lerp(1.0, 1.0 + distortion * 0.65, autoFillEnabled);
                float totalRtScale = max(rtScale * autoFillScale, 0.01);
                float2 scaledTexcoord = (input.texcoord - 0.5) / totalRtScale + 0.5;

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float layerIntensity = saturate(_Intensity);
                if (layerIntensity <= 0.0001)
                {
                    return source;
                }

                float2 viewCentered = scaledTexcoord - 0.5;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);

                float scale = max(_LayerParams0.y * 0.5, 0.005);
                float softness = clamp(_LayerParams0.z, 0.01, 0.5);
                bool circular = _LayerParams0.w > 0.5;

                float autoscale = 0.5 - saturate(distortion * 0.5);
                float2 fitScale = float2(min(1.0 / max(aspect, 0.0001), 1.0), min(aspect, 1.0));
                float2 lensCoord = viewCentered / scale / fitScale;
                float lensRadiusSq = dot(lensCoord, lensCoord);
                float lensDenom = max(1.0 - lensRadiusSq * distortion, 0.0001);
                float2 warpedCentered = lensCoord * (autoscale / lensDenom) * fitScale;
                float2 warpedUV = warpedCentered + 0.5;
                half4 warped = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, warpedUV);

                float maskAspect = circular ? aspect : 1.0;
                float squareRadius = max(abs(warpedCentered.x) * maskAspect, abs(warpedCentered.y)) * 2.0;
                float circularRadius = length(float2(warpedCentered.x * maskAspect, warpedCentered.y)) * 2.0;
                float edgeRadius = circular ? circularRadius : squareRadius;
                float edgeMask = smoothstep(1.0 - softness, 1.0, edgeRadius);

                float inBounds = step(0.0, warpedUV.x) * step(warpedUV.x, 1.0) * step(0.0, warpedUV.y) * step(warpedUV.y, 1.0);
                float scaledInBounds = step(0.0, scaledTexcoord.x) * step(scaledTexcoord.x, 1.0) * step(0.0, scaledTexcoord.y) * step(scaledTexcoord.y, 1.0);
                float blackMask = saturate(max(edgeMask, 1.0 - inBounds));
                blackMask = max(blackMask, 1.0 - scaledInBounds);
                warped.rgb = lerp(warped.rgb, _LayerColor.rgb, blackMask);

                float vignetteScale = 2.0 - distortion * 1.02;
                float2 vignetteCoord = abs(viewCentered) * distortion * vignetteScale / scale;
                vignetteCoord.x *= aspect;
                float vignetteMask = pow(max(1.0 - dot(vignetteCoord, vignetteCoord), 0.0), 0.001);
                warped.rgb = lerp(_LayerColor.rgb, warped.rgb, vignetteMask);
                return lerp(source, warped, layerIntensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
