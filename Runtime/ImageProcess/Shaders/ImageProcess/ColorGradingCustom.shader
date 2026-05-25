Shader "Hidden/lilToon/URP/ImageProcess/ColorGradingCustom"
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
            Name "ImageProcess Color Grading"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float4 _LayerParams2;
            float4 _LayerParams3;
            float4 _LayerParams4;
            float4 _LayerParams5;
            float4 _LayerParams6;
            float4 _LayerParams7;
            float4 _LayerParams8;
            float4 _LayerParams9;
            float4 _LayerParams10;
            float4 _LayerParams11;
            float4 _LayerParams12;

            float ImageProcessLuminance(float3 color)
            {
                return dot(color, float3(0.2126729, 0.7151522, 0.0721750));
            }

            float3 PrepareLift(float4 color)
            {
                float3 lift = saturate(color.rgb);
                float luminance = ImageProcessLuminance(lift);
                return lift - luminance + color.w;
            }

            float3 PrepareGamma(float4 color)
            {
                float3 gamma = saturate(color.rgb);
                float luminance = ImageProcessLuminance(gamma);
                return rcp(max(gamma - luminance + color.w + 1.0, 0.001));
            }

            float3 PrepareGain(float4 color)
            {
                float3 gain = saturate(color.rgb);
                float luminance = ImageProcessLuminance(gain);
                return gain - luminance + color.w + 1.0;
            }

            float3 PrepareLogWheel(float4 color)
            {
                return saturate(color.rgb) + color.w;
            }

            float3 ImageProcessRgbToHsv(float3 color)
            {
                float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(color.bg, k.wz), float4(color.gb, k.xy), step(color.b, color.g));
                float4 q = lerp(float4(p.xyw, color.r), float4(color.r, p.yzx), step(p.x, color.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 ImageProcessHsvToRgb(float3 hsv)
            {
                float3 p = abs(frac(hsv.xxx + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return hsv.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), hsv.y);
            }

            float HueWeight(float hue, float targetHue)
            {
                float distance = abs(frac(hue - targetHue + 0.5) - 0.5);
                return saturate(1.0 - distance * 6.0);
            }

            float LuminanceWeight(float luminance, int bandIndex)
            {
                float targetLuminance = bandIndex / 5.0;
                float distance = abs(saturate(luminance) - targetLuminance);
                return saturate(1.0 - distance * 5.0);
            }

            float GetSixColorValue(int mode, int index)
            {
                int valueIndex = mode * 6 + index;
                if (valueIndex == 0) return _LayerParams7.x;
                if (valueIndex == 1) return _LayerParams7.y;
                if (valueIndex == 2) return _LayerParams7.z;
                if (valueIndex == 3) return _LayerParams7.w;
                if (valueIndex == 4) return _LayerParams8.x;
                if (valueIndex == 5) return _LayerParams8.y;
                if (valueIndex == 6) return _LayerParams8.z;
                if (valueIndex == 7) return _LayerParams8.w;
                if (valueIndex == 8) return _LayerParams9.x;
                if (valueIndex == 9) return _LayerParams9.y;
                if (valueIndex == 10) return _LayerParams9.z;
                if (valueIndex == 11) return _LayerParams9.w;
                if (valueIndex == 12) return _LayerParams10.x;
                if (valueIndex == 13) return _LayerParams10.y;
                if (valueIndex == 14) return _LayerParams10.z;
                if (valueIndex == 15) return _LayerParams10.w;
                if (valueIndex == 16) return _LayerParams11.x;
                if (valueIndex == 17) return _LayerParams11.y;
                if (valueIndex == 18) return _LayerParams11.z;
                if (valueIndex == 19) return _LayerParams11.w;
                if (valueIndex == 20) return _LayerParams12.x;
                if (valueIndex == 21) return _LayerParams12.y;
                if (valueIndex == 22) return _LayerParams12.z;
                if (valueIndex == 23) return _LayerParams12.w;
                return 0.0;
            }

            float3 ApplySixColorAdjustments(float3 color)
            {
                float3 hsv = ImageProcessRgbToHsv(max(color, 0.0));
                float hueOffset = 0.0;
                float saturationOffset = 0.0;
                float valueOffset = 0.0;
                float luminance = ImageProcessLuminance(color);
                float luminanceSaturationOffset = 0.0;

                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float targetHue = i / 6.0;
                    float weight = HueWeight(hsv.x, targetHue);
                    hueOffset += weight * (GetSixColorValue(0, i) / 360.0);
                    saturationOffset += weight * GetSixColorValue(1, i);
                    valueOffset += weight * GetSixColorValue(2, i);
                    luminanceSaturationOffset += LuminanceWeight(luminance, i) * GetSixColorValue(3, i);
                }

                hsv.x = frac(hsv.x + hueOffset + 1.0);
                hsv.y = saturate(hsv.y + saturationOffset + luminanceSaturationOffset);
                hsv.z = max(0.0, hsv.z + valueOffset);
                return ImageProcessHsvToRgb(hsv);
            }

            float3 ApplyLogWheels(float3 color)
            {
                float luminance = ImageProcessLuminance(color);
                float shadowLimit = saturate(_LayerParams6.x);
                float highlightLimit = saturate(_LayerParams6.y);
                float shadowWeight = 1.0 - smoothstep(shadowLimit, min(shadowLimit + 0.25, 1.0), luminance);
                float highlightWeight = smoothstep(max(highlightLimit - 0.25, 0.0), highlightLimit, luminance);
                float midtoneWeight = saturate(1.0 - shadowWeight - highlightWeight);

                return color
                    + PrepareLogWheel(_LayerParams3) * shadowWeight
                    + PrepareLogWheel(_LayerParams4) * midtoneWeight
                    + PrepareLogWheel(_LayerParams5) * highlightWeight;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 lift = PrepareLift(_LayerParams0);
                float3 gamma = PrepareGamma(_LayerParams1);
                float3 gain = PrepareGain(_LayerParams2);
                float3 graded = pow(max(source.rgb + lift, 0.0), gamma) * gain;
                graded = ApplyLogWheels(graded);
                graded = ApplySixColorAdjustments(graded);

                return half4(lerp(source.rgb, graded, saturate(_Intensity)), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
