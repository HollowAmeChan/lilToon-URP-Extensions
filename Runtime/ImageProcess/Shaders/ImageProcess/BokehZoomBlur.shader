Shader "Hidden/lilToon/URP/ImageProcess/BokehZoomBlur"
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
            Name "ImageProcess Bokeh Zoom Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBokehZoomBlur

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            static const int MaxBokehZoomSamples = 32;
            static const float ImageProcessBokehZoomTwoPi = 6.28318530718;

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x radius, y threshold, z soft knee, w exposure
            float4 _LayerParams1; // x center x [-1,1], y center y [-1,1], z decay, w quality
            float4 _LayerParams2; // x blades, y curvature, z rotation degrees, w chromatic dispersion
            float4 _LayerParams3; // x blend mode, y show only bokeh, z bokeh gain

            float ImageProcessBokehZoomLuma(float3 color)
            {
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float ImageProcessBokehZoomHash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.11369));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float ImageProcessBokehZoomHighlightWeight(float3 color, float threshold, float softKnee, float exposure)
            {
                float luma = ImageProcessBokehZoomLuma(max(color * max(exposure, 0.0), 0.0));
                if (softKnee <= 0.0001)
                {
                    return luma >= threshold ? 1.0 : 0.0;
                }

                return smoothstep(max(threshold - softKnee, 0.0), threshold + softKnee, luma);
            }

            float ImageProcessBokehZoomAperture(float2 direction, float blades, float curvature, float rotationRadians)
            {
                float bladeCount = round(blades);
                if (bladeCount < 3.0)
                {
                    return 1.0;
                }

                bladeCount = clamp(bladeCount, 3.0, 12.0);
                float angle = atan2(direction.y, direction.x) + rotationRadians;
                float sector = ImageProcessBokehZoomTwoPi / bladeCount;
                float localAngle = angle - floor(angle / sector) * sector - sector * 0.5;
                float polygonRadius = cos(sector * 0.5) / max(cos(localAngle), 0.05);
                return saturate(lerp(polygonRadius, 1.0, saturate(curvature)));
            }

            float3 ImageProcessBokehZoomSampleRgb(float2 uv, float2 radialOffset, float chromatic)
            {
                chromatic = saturate(chromatic);
                if (chromatic <= 0.0001)
                {
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                }

                float2 caOffset = radialOffset * chromatic * 0.18;
                float r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - caOffset).r;
                float g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                float b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + caOffset).b;
                return float3(r, g, b);
            }

            float3 ImageProcessBokehZoomLayer(float2 uv)
            {
                float2 center = _LayerParams1.xy * 0.5 + 0.5;
                float2 direction = uv - center;
                float2 aspectDirection = float2(direction.x * (_ScreenParams.x / max(_ScreenParams.y, 1.0)), direction.y);
                float aperture = ImageProcessBokehZoomAperture(aspectDirection, _LayerParams2.x, _LayerParams2.y, radians(_LayerParams2.z));

                int quality = clamp((int)round(_LayerParams1.w), 0, 2);
                int sampleCount = quality == 0 ? 8 : (quality == 1 ? 16 : 32);
                float radius = saturate(_LayerParams0.x) * aperture;
                float threshold = max(_LayerParams0.y, 0.0);
                float softKnee = max(_LayerParams0.z, 0.0);
                float exposure = max(_LayerParams0.w, 0.0);
                float decay = max(_LayerParams1.z, 0.0);
                float chromatic = saturate(_LayerParams2.w);

                float jitter = (ImageProcessBokehZoomHash(floor(uv * _ScreenParams.xy)) - 0.5) / max(sampleCount, 1);
                float3 sum = 0.0;
                float totalWeight = 0.0;

                [loop]
                for (int i = 0; i < MaxBokehZoomSamples; i++)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    float t = saturate(((float)i + 0.5 + jitter) / max(sampleCount, 1));
                    float distanceWeight = pow(saturate(1.0 - t), decay);
                    float2 offset = direction * radius * t;
                    float2 sampleUV = uv - offset;
                    float3 sampleColor = ImageProcessBokehZoomSampleRgb(sampleUV, offset, chromatic);
                    float highlightWeight = ImageProcessBokehZoomHighlightWeight(sampleColor, threshold, softKnee, exposure);
                    float weight = max(distanceWeight * highlightWeight, 0.0);
                    sum += sampleColor * exposure * weight;
                    totalWeight += weight;
                }

                return totalWeight > 0.0001 ? (sum / totalWeight) * max(_LayerParams3.z, 0.0) * _LayerColor.rgb : 0.0;
            }

            float3 ImageProcessBokehZoomComposite(float3 source, float3 bokeh, float amount, float mode)
            {
                int blendMode = clamp((int)round(mode), 0, 3);
                if (blendMode == 1)
                {
                    float3 ldrSource = saturate(source);
                    float3 ldrBokeh = saturate(bokeh);
                    float3 screened = 1.0 - (1.0 - ldrSource) * (1.0 - ldrBokeh);
                    return lerp(source, screened + max(source - 1.0, 0.0), amount);
                }

                if (blendMode == 2)
                {
                    float3 ldrSource = saturate(source);
                    float3 ldrBokeh = saturate(bokeh);
                    float3 overlay = lerp(2.0 * ldrSource * ldrBokeh, 1.0 - 2.0 * (1.0 - ldrSource) * (1.0 - ldrBokeh), step(0.5, ldrSource));
                    return lerp(source, overlay + max(source - 1.0, 0.0), amount);
                }

                if (blendMode == 3)
                {
                    return lerp(source, bokeh, amount);
                }

                return source + bokeh * amount;
            }

            half4 FragBokehZoomBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001 || _LayerParams0.x <= 0.0001 || _LayerParams3.z <= 0.0001)
                {
                    return source;
                }

                float3 bokeh = ImageProcessBokehZoomLayer(input.texcoord);
                if (_LayerParams3.y > 0.5)
                {
                    return half4(bokeh * amount, source.a);
                }

                return half4(ImageProcessBokehZoomComposite(source.rgb, bokeh, amount, _LayerParams3.x), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
