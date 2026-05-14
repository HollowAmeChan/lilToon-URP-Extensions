Shader "Hidden/lilToon-Shoost/URP/Shoost/LUTColorGrading"
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
            Name "Shoost LUT Color Grading"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerTextureEnabled;
            float4 _LayerParams0;
            float4 _LayerParams1;
            TEXTURE2D_X(_LayerTexture);

            float StandardIlluminantY(float x)
            {
                return 2.87 * x - 3.0 * x * x - 0.27509507;
            }

            float3 CIExyToLMS(float x, float y)
            {
                y = max(y, 0.0001);
                float Y = 1.0;
                float X = Y * x / y;
                float Z = Y * (1.0 - x - y) / y;
                float3 xyz = float3(X, Y, Z);

                return float3(
                    dot(float3(0.390405, 0.549941, 0.008926), xyz),
                    dot(float3(0.070842, 0.963172, 0.001358), xyz),
                    dot(float3(0.023108, 0.128021, 0.936245), xyz));
            }

            float3 ColorBalance(float temperature, float tint)
            {
                float t1 = temperature / 55.0;
                float t2 = tint / 55.0;
                float x = 0.31271 - t1 * (t1 < 0.0 ? 0.1 : 0.05);
                float y = StandardIlluminantY(x) + t2 * 0.05;
                float3 w1 = CIExyToLMS(x, y);
                float3 w2 = CIExyToLMS(0.31271, 0.32902);
                return w1 / max(w2, 0.0001);
            }

            float3 RgbToLms(float3 rgb)
            {
                return float3(
                    dot(float3(0.390405, 0.549941, 0.008926), rgb),
                    dot(float3(0.070842, 0.963172, 0.001358), rgb),
                    dot(float3(0.023108, 0.128021, 0.936245), rgb));
            }

            float3 LmsToRgb(float3 lms)
            {
                return float3(
                    dot(float3(2.858470, -1.628790, -0.024891), lms),
                    dot(float3(-0.210182, 1.158200, 0.000324), lms),
                    dot(float3(-0.041812, -0.118169, 1.068670), lms));
            }

            float3 ApplyTone(float3 color, float saturation, float brightness, float contrast)
            {
                float sat = 1.0 + saturation / 100.0;
                float bri = brightness / 100.0;
                float con = 1.0 + contrast / 100.0;
                float luminance = dot(color, float3(0.2126729, 0.7151522, 0.0721750));
                color = lerp(float3(luminance, luminance, luminance), color, sat);
                color = (color - 0.5) * con + 0.5 + bri;
                return saturate(color);
            }

            float3 SampleLut2D(float3 color)
            {
                float3 scaled = saturate(color) * 31.0;
                float slice = floor(scaled.b);
                float sliceBlend = scaled.b - slice;
                float2 texelSize = float2(1.0 / 1024.0, 1.0 / 32.0);
                float2 uv0 = float2(slice * 32.0 + scaled.r, scaled.g) * texelSize + texelSize * 0.5;
                float2 uv1 = float2(min(slice + 1.0, 31.0) * 32.0 + scaled.r, scaled.g) * texelSize + texelSize * 0.5;
                float3 lut0 = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearClamp, uv0).rgb;
                float3 lut1 = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearClamp, uv1).rgb;
                return lerp(lut0, lut1, sliceBlend);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 graded = saturate(source.rgb);
                float3 balance = ColorBalance(_LayerParams0.x, _LayerParams0.y);
                graded = LmsToRgb(RgbToLms(graded) * balance);
                graded = ApplyTone(graded, _LayerParams0.z, _LayerParams0.w, _LayerParams1.x);

                if (_LayerTextureEnabled > 0.5)
                {
                    graded = SampleLut2D(graded);
                }

                float contribution = saturate(_LayerParams1.y);
                float blend = saturate(_Intensity * contribution);
                return half4(lerp(source.rgb, graded, blend), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
