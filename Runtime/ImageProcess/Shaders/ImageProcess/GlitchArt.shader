Shader "Hidden/lilToon/URP/ImageProcess/GlitchArt"
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
            Name "ImageProcess Glitch Art"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlitchArt

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x screen shake, y scanline jitter, z block shift, w flicker
            float4 _LayerParams1; // x chromatic dispersion, y speed, z noise, w stripe density

            float ImageProcessGlitchHash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453123);
            }

            float ImageProcessGlitchHash21(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453123);
            }

            half3 ImageProcessGlitchSampleRgb(float2 uv, float2 dispersion, float chromatic)
            {
                half3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 plus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dispersion * 1.35).rgb;
                half3 minus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - dispersion * 1.35).rgb;
                half3 split = half3(plus.r, center.g, minus.b);

                half3 farPlus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dispersion * 2.8).rgb;
                half3 farMinus = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - dispersion * 2.8).rgb;
                half3 neonFringe = farPlus.r * half3(1.0, 0.05, 0.95) + farMinus.b * half3(0.0, 0.95, 1.0);
                return lerp(center, split + neonFringe * 0.28, saturate(chromatic));
            }

            half4 FragGlitchArt(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float shakeAmount = saturate(_LayerParams0.x);
                float scanlineJitter = saturate(_LayerParams0.y);
                float blockShift = saturate(_LayerParams0.z);
                float flicker = saturate(_LayerParams0.w);
                float chromatic = saturate(_LayerParams1.x);
                float speed = max(_LayerParams1.y, 0.0);
                float noiseAmount = saturate(_LayerParams1.z);
                float stripeDensity = max(_LayerParams1.w, 1.0);

                float time = _Time.y * max(speed, 0.001);
                float frame = floor(time * 2.0);
                float slowFrame = floor(time * 0.65);
                float2 uv = input.texcoord;

                float burst = smoothstep(0.35, 1.0, ImageProcessGlitchHash11(slowFrame * 2.13 + 19.0));
                float globalShake = (ImageProcessGlitchHash11(frame + 3.17) - 0.5) * shakeAmount * lerp(0.055, 0.135, burst);
                float verticalGate = step(1.0 - flicker * 0.65, ImageProcessGlitchHash11(slowFrame * 0.37 + 41.0));
                float verticalJump = (ImageProcessGlitchHash11(slowFrame + 17.0) - 0.5) * flicker * verticalGate * 0.10;

                float lineIndex = floor(uv.y * stripeDensity);
                float lineRnd = ImageProcessGlitchHash11(lineIndex + frame * 1.31);
                float lineGate = step(1.0 - scanlineJitter * 0.72, ImageProcessGlitchHash11(lineIndex * 3.7 + frame));
                float lineShift = (lineRnd - 0.5) * scanlineJitter * lineGate * lerp(0.16, 0.28, burst);

                float blockDensity = lerp(2.0, 24.0, saturate(stripeDensity / 32.0));
                float blockIndex = floor(uv.y * blockDensity);
                float blockRnd = ImageProcessGlitchHash11(blockIndex * 9.13 + slowFrame);
                float blockGate = step(1.0 - blockShift * 0.80, ImageProcessGlitchHash11(blockIndex + slowFrame * 2.0 + 9.0));
                float blockOffset = (blockRnd - 0.5) * blockShift * blockGate * lerp(0.24, 0.42, burst);

                float2 shiftedUv = saturate(uv + float2(globalShake + lineShift + blockOffset, verticalJump));
                float distortion = saturate(abs(lineShift + blockOffset) * 8.0 + blockGate * 0.65 + scanlineJitter * 0.20 + burst * 0.25);
                float dispersionPixels = chromatic * lerp(6.0, 54.0, distortion);
                float diagonal = (ImageProcessGlitchHash11(slowFrame + 91.0) - 0.5) * 0.45;
                float2 dispersion = float2(dispersionPixels / max(_ScreenParams.x, 1.0), diagonal * dispersionPixels / max(_ScreenParams.y, 1.0));

                half3 glitch = ImageProcessGlitchSampleRgb(shiftedUv, dispersion, chromatic);

                float scan = sin((uv.y * _ScreenParams.y + time * 18.0) * 3.14159265);
                float scanMask = lerp(1.0, lerp(0.70, 1.24, smoothstep(-0.55, 0.8, scan)), scanlineJitter * 0.55);
                float flash = lerp(1.0, lerp(0.55, 1.45, ImageProcessGlitchHash11(frame + 73.0)), flicker);
                float snow = (ImageProcessGlitchHash21(floor(uv * _ScreenParams.xy * 0.55) + frame) - 0.5) * noiseAmount * 0.32;
                float dropout = step(1.0 - flicker * 0.12, ImageProcessGlitchHash11(lineIndex * 5.0 + slowFrame * 4.0));

                glitch = glitch * scanMask * flash + snow;
                glitch = lerp(glitch, glitch.bgr * 0.85, dropout * flicker);

                source.rgb = lerp(source.rgb, glitch, intensity);
                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
