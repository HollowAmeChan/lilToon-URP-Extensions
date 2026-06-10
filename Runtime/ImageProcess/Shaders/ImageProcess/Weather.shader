Shader "Hidden/lilToon/URP/ImageProcess/Weather"
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
            Name "ImageProcess Weather"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragWeather

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x particle type, y spawn rate, z opacity, w focus distance
            float4 _LayerParams1; // x blur amount, y blur softness, z blur curve, w blend mode
            float4 _LayerParams2; // x speed, y count, z size, w randomness
            float4 _LayerParams3; // x depth spread, y vertical unevenness, z shimmer, w drift

            float ImageProcessWeatherHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ImageProcessWeatherNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = ImageProcessWeatherHash12(i);
                float b = ImageProcessWeatherHash12(i + float2(1.0, 0.0));
                float c = ImageProcessWeatherHash12(i + float2(0.0, 1.0));
                float d = ImageProcessWeatherHash12(i + float2(1.0, 1.0));
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ImageProcessWeatherFbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += ImageProcessWeatherNoise(p) * a;
                    p = p * 2.03 + 17.17;
                    a *= 0.5;
                }

                return v;
            }

            float ImageProcessWeatherVerticalBand(float y, float center, float width, float softness)
            {
                float d = abs(y - center);
                return 1.0 - smoothstep(width, width + softness, d);
            }

            float ImageProcessWeatherScaledDepth(float depth, float depthSpread)
            {
                return saturate(0.5 + (saturate(depth) - 0.5) * max(depthSpread, 0.0));
            }

            float ImageProcessWeatherVary(float value, float seed, float randomness, float minValue, float maxValue)
            {
                float rnd = ImageProcessWeatherHash12(float2(seed, seed * 1.37 + 9.13));
                return value * lerp(1.0, lerp(minValue, maxValue, rnd), saturate(randomness));
            }

            float ImageProcessWeatherSoftDiscLayer(float2 uv, float scale, float speed, float density, float size, float softness, float seed, float randomness, float driftAmount, float shimmerAmount)
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float2 p = uv;
                p.x += sin((uv.y + _Time.y * 0.06 + seed) * 5.0) * 0.025 * max(driftAmount, 0.0);
                p.y += _Time.y * speed;
                p *= scale;

                float2 cell = floor(p);
                float2 local = frac(p);
                float rnd = ImageProcessWeatherHash12(cell + seed);
                float active = smoothstep(1.0 - saturate(density), 1.0, rnd);
                float2 center = float2(ImageProcessWeatherHash12(cell + seed + 12.7), ImageProcessWeatherHash12(cell + seed + 58.1));
                center = lerp(0.5, 0.18 + center * 0.64, saturate(randomness));
                float2 d = local - center;
                d.x *= aspect;
                float localSize = size * lerp(1.0, lerp(0.55, 1.7, rnd), saturate(randomness));
                float shimmer = lerp(1.0, lerp(0.55, 1.25, ImageProcessWeatherHash12(cell + floor(_Time.y * 0.9) + seed + 19.0)), saturate(shimmerAmount));
                return smoothstep(localSize + softness, localSize, length(d)) * active * lerp(0.45, 1.0, rnd) * shimmer;
            }

            float ImageProcessWeatherDepthBlur(float particleDepth, float focusDistance, float blurAmount, float blurSoftness, float blurCurve)
            {
                focusDistance = max(saturate(focusDistance), 0.05);
                float range = max(focusDistance * lerp(0.12, 1.35, saturate(blurSoftness)), 0.001);
                float blur = saturate((focusDistance - saturate(particleDepth)) / range);
                blur = pow(blur, max(blurCurve, 0.05));
                return saturate(blur * max(blurAmount, 0.0));
            }

            float ImageProcessWeatherRainLayer(float2 uv, float scale, float speed, float density, float length, float width, float slant, float seed)
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float2 p = uv;
                p.x = p.x * aspect + p.y * slant;
                p.y += _Time.y * speed;
                p *= scale;

                float2 cell = floor(p);
                float2 local = frac(p);
                float rnd = ImageProcessWeatherHash12(cell + seed);
                float active = smoothstep(1.0 - density, 1.0, rnd);
                float x = ImageProcessWeatherHash12(cell + seed + 11.31);
                float y = ImageProcessWeatherHash12(cell + seed + 47.73);
                float dx = abs(local.x - x);
                float dy = abs(local.y - y);
                dy = min(dy, 1.0 - dy);
                return smoothstep(width, 0.0, dx) * smoothstep(length, 0.0, dy) * active;
            }

            float ImageProcessWeatherRainDepthLayer(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float fakeDepth, float scale, float speed, float density, float length, float width, float slant, float seed, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float driftAmount)
            {
                fakeDepth = ImageProcessWeatherScaledDepth(fakeDepth, depthSpread);
                float blur = ImageProcessWeatherDepthBlur(fakeDepth, focusDistance, blurAmount, blurSoftness, blurCurve);
                speed = ImageProcessWeatherVary(speed * speedMul, seed, randomness, 0.72, 1.34);
                density = saturate(density * countMul);
                length = ImageProcessWeatherVary(length * sizeMul, seed + 3.1, randomness, 0.65, 1.55);
                width = max(width * sizeMul, 0.0001);
                slant *= max(driftAmount, 0.0);
                float sharp = ImageProcessWeatherRainLayer(uv, scale, speed, density, length, width, slant, seed);
                float soft = ImageProcessWeatherRainLayer(uv, scale * 0.72, speed * 0.86, density * 0.72, length * 1.85, width * 3.4, slant, seed + 211.0);
                return lerp(sharp, soft * 0.58, blur);
            }

            float ImageProcessWeatherRain(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float verticalAmount, float shimmerAmount, float driftAmount)
            {
                float r = saturate(rate);
                float vertical = lerp(1.0, lerp(0.82, 1.16, smoothstep(0.08, 0.94, uv.y)), saturate(verticalAmount));
                float a = 0.0;
                a += ImageProcessWeatherRainDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.22, 18.0, 2.8, lerp(0.03, 0.16, r), 0.40, 0.020, 0.22, 3.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.55; // Storm
                a += ImageProcessWeatherRainDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.36, 34.0, 4.8, lerp(0.06, 0.30, r), 0.24, 0.012, 0.18, 23.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.70; // S/M
                a += ImageProcessWeatherRainDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.66, 48.0, 6.0, lerp(0.05, 0.24, r), 0.20, 0.010, 0.15, 73.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.80; // L
                a += ImageProcessWeatherRainDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.18, 14.0, 2.2, lerp(0.02, 0.10, r), 0.34, 0.030, 0.20, 119.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.40; // Storm_L
                float shimmer = lerp(1.0, lerp(0.82, 1.18, ImageProcessWeatherFbm(uv * 9.0 + _Time.y * speedMul * 0.12)), saturate(shimmerAmount));
                return saturate((a + pow(ImageProcessWeatherFbm(uv * 6.0 + _Time.y * float2(0.05, 0.22) * speedMul), 3.0) * r * 0.05) * vertical * shimmer);
            }

            float ImageProcessWeatherSnowLayer(float2 uv, float scale, float speed, float density, float size, float drift, float seed)
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float2 p = uv;
                p.x += sin((uv.y + _Time.y * 0.10 + seed) * 7.0) * drift;
                p.y += _Time.y * speed;
                p *= scale;

                float2 cell = floor(p);
                float2 local = frac(p);
                float rnd = ImageProcessWeatherHash12(cell + seed);
                float active = smoothstep(1.0 - density, 1.0, rnd);
                float2 center = float2(ImageProcessWeatherHash12(cell + seed + 17.0), ImageProcessWeatherHash12(cell + seed + 41.0));
                center = 0.12 + center * 0.76;
                float2 d = local - center;
                d.x *= aspect;
                float flake = smoothstep(size, size * 0.2, length(d));
                float soft = smoothstep(size * 2.2, size * 0.4, length(d)) * 0.3;
                return (flake + soft) * active;
            }

            float ImageProcessWeatherSnowDepthLayer(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float fakeDepth, float scale, float speed, float density, float size, float drift, float seed, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float driftAmount)
            {
                fakeDepth = ImageProcessWeatherScaledDepth(fakeDepth, depthSpread);
                float blur = ImageProcessWeatherDepthBlur(fakeDepth, focusDistance, blurAmount, blurSoftness, blurCurve);
                speed = ImageProcessWeatherVary(speed * speedMul, seed, randomness, 0.68, 1.48);
                density = saturate(density * countMul);
                size = max(ImageProcessWeatherVary(size * sizeMul, seed + 8.3, randomness, 0.65, 1.65), 0.0001);
                drift *= max(driftAmount, 0.0);
                float sharp = ImageProcessWeatherSnowLayer(uv, scale, speed, density, size, drift, seed);
                float soft = ImageProcessWeatherSnowLayer(uv, scale * 0.65, speed * 0.72, density * 0.70, size * 2.7, drift * 1.15, seed + 173.0);
                return lerp(sharp, soft * 0.70, blur);
            }

            float ImageProcessWeatherSnow(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float verticalAmount, float shimmerAmount, float driftAmount)
            {
                float r = saturate(rate);
                float topBias = lerp(0.42, 1.18, smoothstep(0.12, 0.95, uv.y));
                float lowerPocket = ImageProcessWeatherVerticalBand(uv.y, 0.24, 0.26, 0.28);
                float upperPocket = ImageProcessWeatherVerticalBand(uv.y, 0.76, 0.18, 0.32);
                topBias = lerp(1.0, topBias, saturate(verticalAmount));
                lowerPocket = lerp(1.0, lowerPocket, saturate(verticalAmount));
                upperPocket = lerp(1.0, upperPocket, saturate(verticalAmount));
                float foregroundBlur = ImageProcessWeatherDepthBlur(ImageProcessWeatherScaledDepth(0.18, depthSpread), focusDistance, blurAmount, blurSoftness, blurCurve);
                float a = 0.0;
                a += ImageProcessWeatherSnowDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.86, 12.0, 0.08, lerp(0.04, 0.16, r), 0.030, 0.018, 5.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.55 * topBias; // BG
                a += ImageProcessWeatherSnowDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.58, 20.0, 0.14, lerp(0.05, 0.22, r), 0.024, 0.026, 31.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.70 * lerp(0.72, 1.12, upperPocket); // M
                a += ImageProcessWeatherSnowDepthLayer(uv, r, focusDistance, blurAmount, blurSoftness, blurCurve, 0.32, 34.0, 0.24, lerp(0.04, 0.18, r), 0.018, 0.034, 67.0, speedMul, countMul, sizeMul, randomness, depthSpread, driftAmount) * 0.85 * lerp(0.65, 1.15, saturate(topBias)); // L
                a += ImageProcessWeatherSoftDiscLayer(uv, lerp(8.0, 5.6, foregroundBlur), 0.055 * speedMul, lerp(0.02, 0.08, r) * countMul, lerp(0.050, 0.105, foregroundBlur) * sizeMul, 0.080 * sizeMul, 89.0, randomness, driftAmount, shimmerAmount) * r * 0.20 * lowerPocket;
                float shimmer = lerp(1.0, lerp(0.76, 1.24, ImageProcessWeatherFbm(uv * 10.0 + _Time.y * speedMul * 0.08)), saturate(shimmerAmount));
                a += pow(ImageProcessWeatherFbm(uv * 5.0 + _Time.y * float2(0.03, -0.05) * speedMul), 3.0) * r * 0.08 * lerp(0.45, 1.15, lowerPocket); // smoke layers
                return saturate(a * shimmer);
            }

            float ImageProcessWeatherSmoke(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float verticalAmount, float shimmerAmount, float driftAmount)
            {
                float r = saturate(rate);
                float t = _Time.y * speedMul;
                float2 centered = uv - 0.5;
                float vignette = saturate(1.0 - dot(centered, centered) * 1.45);
                float lowerMass = 1.0 - smoothstep(0.20, 0.92, uv.y);
                float middleMass = ImageProcessWeatherVerticalBand(uv.y, 0.46, 0.25, 0.34);
                float upperBreakup = lerp(0.28, 1.0, 1.0 - smoothstep(0.55, 1.0, uv.y));
                float foregroundBlur = ImageProcessWeatherDepthBlur(ImageProcessWeatherScaledDepth(0.24, depthSpread), focusDistance, blurAmount, blurSoftness, blurCurve);
                float noiseScale = lerp(1.0, lerp(0.82, 1.22, ImageProcessWeatherNoise(uv * 3.0 + randomness)), saturate(randomness));
                float2 drift = float2(0.035, 0.0) * max(driftAmount, 0.0);
                float n0 = ImageProcessWeatherFbm(uv * (3.2 / max(sizeMul, 0.1)) * noiseScale + t * float2(-0.06, 0.02) + drift);
                float n1 = ImageProcessWeatherFbm(uv * (8.0 / max(sizeMul, 0.1)) * noiseScale + t * float2(0.12, -0.05) + drift * 1.7);
                float cloudSource = n0 * 0.85 + n1 * 0.30;
                float cloud = lerp(smoothstep(0.36 + (1.0 - countMul) * 0.08, 0.82, cloudSource), smoothstep(0.28, 0.74, cloudSource) * 0.78, foregroundBlur);
                float wisps = smoothstep(lerp(0.50, 0.42, foregroundBlur), 0.90, ImageProcessWeatherFbm(uv * lerp(12.0, 7.5, foregroundBlur) / max(sizeMul, 0.1) + t * float2(0.20, 0.08)));
                float softPuffs = ImageProcessWeatherSoftDiscLayer(uv, lerp(5.2, 3.8, foregroundBlur) / max(sizeMul, 0.1), -0.018 * speedMul, lerp(0.04, 0.16, r) * countMul, lerp(0.085, 0.160, foregroundBlur) * sizeMul, 0.180 * sizeMul, 141.0, randomness, driftAmount, shimmerAmount);
                float vertical = saturate(lowerMass * 0.70 + middleMass * 0.75 + 0.18) * upperBreakup;
                vertical = lerp(1.0, vertical, saturate(verticalAmount));
                float shimmer = lerp(1.0, lerp(0.78, 1.18, ImageProcessWeatherFbm(uv * 6.0 + t * 0.04)), saturate(shimmerAmount));
                return saturate((cloud * 0.55 + wisps * 0.24 + softPuffs * 0.22) * vignette * vertical * r * max(countMul, 0.0) * shimmer);
            }

            float ImageProcessWeatherDustLayer(float2 uv, float scale, float speed, float density, float size, float softness, float drift, float seed, float randomness, float shimmerAmount)
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float2 p = uv;
                p.x += sin((uv.y + _Time.y * 0.075 + seed) * 5.8) * drift;
                p.y += _Time.y * speed;
                p *= scale;

                float2 cell = floor(p);
                float2 local = frac(p);
                float rnd = ImageProcessWeatherHash12(cell + seed);
                float active = smoothstep(1.0 - density, 1.0, rnd);
                float2 center = float2(ImageProcessWeatherHash12(cell + seed + 23.4), ImageProcessWeatherHash12(cell + seed + 71.8));
                center = lerp(0.5, 0.18 + center * 0.64, saturate(randomness));
                float2 d = local - center;
                d.x *= aspect;
                float angle = ImageProcessWeatherHash12(cell + seed + 151.0) * 6.2831853;
                float2 axis = float2(cos(angle), sin(angle));
                float2 side = float2(-axis.y, axis.x);
                float stretch = lerp(1.25, 2.65, ImageProcessWeatherHash12(cell + seed + 83.0));
                float particleSize = max(size * lerp(1.0, lerp(0.55, 1.70, ImageProcessWeatherHash12(cell + seed + 101.0)), saturate(randomness)), 0.0001);
                float particleSoftness = softness * lerp(1.0, lerp(0.90, 1.65, rnd), saturate(randomness));
                float2 q = float2(dot(d, axis) / stretch, dot(d, side));
                float dist = length(q);
                float veil = smoothstep(particleSize + particleSoftness * 1.85, 0.0, dist);
                float body = exp2(-dist * dist / max(particleSize * particleSize * 1.75, 0.000001)) * 0.38;
                float breakup = lerp(1.0, lerp(0.62, 1.08, ImageProcessWeatherNoise((p + seed) * 1.65 + float2(rnd, seed * 0.01))), saturate(randomness));
                float rimFade = lerp(0.55, 1.0, smoothstep(particleSize + particleSoftness, 0.0, dist));
                float shimmer = lerp(1.0, lerp(0.72, 1.08, ImageProcessWeatherHash12(cell + floor(_Time.y * 0.8) + seed + 19.0)), saturate(shimmerAmount));
                return saturate((veil * 0.72 + body) * breakup * rimFade) * active * shimmer;
            }

            float ImageProcessWeatherDust(float2 uv, float rate, float focusDistance, float blurAmount, float blurSoftness, float blurCurve, float speedMul, float countMul, float sizeMul, float randomness, float depthSpread, float verticalAmount, float shimmerAmount, float driftAmount)
            {
                float r = saturate(rate);
                float lowerMass = 1.0 - smoothstep(0.18, 0.92, uv.y);
                float midBand = ImageProcessWeatherVerticalBand(uv.y, 0.52, 0.34, 0.38);
                float topDust = smoothstep(0.20, 0.86, uv.y) * (1.0 - smoothstep(0.86, 1.0, uv.y));
                lowerMass = lerp(1.0, lowerMass, saturate(verticalAmount));
                midBand = lerp(1.0, midBand, saturate(verticalAmount));
                topDust = lerp(1.0, topDust, saturate(verticalAmount));
                float nearBlur = ImageProcessWeatherDepthBlur(ImageProcessWeatherScaledDepth(0.16, depthSpread), focusDistance, blurAmount, blurSoftness, blurCurve);
                float midBlur = ImageProcessWeatherDepthBlur(ImageProcessWeatherScaledDepth(0.46, depthSpread), focusDistance, blurAmount, blurSoftness, blurCurve);

                float drift = max(driftAmount, 0.0);
                float fine = ImageProcessWeatherDustLayer(uv, 30.0 / max(sizeMul, 0.1), -0.028 * speedMul, saturate(lerp(0.035, 0.150, r) * countMul), 0.008 * sizeMul, 0.040 * sizeMul, 0.030 * drift, 211.0, randomness, shimmerAmount);
                float mid = ImageProcessWeatherDustLayer(uv, lerp(15.0, 10.0, midBlur) / max(sizeMul, 0.1), -0.018 * speedMul, saturate(lerp(0.045, 0.165, r) * countMul), lerp(0.018, 0.034, midBlur) * sizeMul, 0.085 * sizeMul, 0.045 * drift, 251.0, randomness, shimmerAmount);
                float near = ImageProcessWeatherDustLayer(uv, lerp(7.0, 4.5, nearBlur) / max(sizeMul, 0.1), -0.009 * speedMul, saturate(lerp(0.025, 0.090, r) * countMul), lerp(0.034, 0.080, nearBlur) * sizeMul, 0.180 * sizeMul, 0.060 * drift, 307.0, randomness, shimmerAmount);

                float hazeNoise = ImageProcessWeatherFbm(uv * 4.2 / max(sizeMul, 0.1) + _Time.y * float2(0.035, -0.012) * speedMul);
                float haze = smoothstep(0.34, 0.82, hazeNoise) * lerp(0.30, 1.0, lowerMass + midBand * 0.6) * r * 0.12;
                float layers = fine * (0.30 + topDust * 0.30) + mid * (0.42 + midBand * 0.30) + near * (0.20 + lowerMass * 0.25);
                return saturate((layers + haze) * lerp(0.65, 1.18, midBand + lowerMass * 0.4));
            }

            float3 ImageProcessWeatherBlend(float3 source, float3 tint, float alpha, float blendMode)
            {
                alpha = saturate(alpha);
                tint = max(tint, 0.0);
                if (blendMode < 0.5)
                {
                    return lerp(source, tint, alpha);
                }

                if (blendMode < 1.5)
                {
                    return max(source + tint * alpha, 0.0);
                }

                if (blendMode < 2.5)
                {
                    float3 screenColor = 1.0 - (1.0 - source) * (1.0 - saturate(tint));
                    return lerp(source, screenColor, alpha);
                }

                float3 softTint = saturate(tint);
                float3 softLight = (1.0 - 2.0 * softTint) * source * source + 2.0 * softTint * source;
                return lerp(source, softLight, alpha);
            }

            half4 FragWeather(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float rate = saturate(_LayerParams0.y);
                float opacity = saturate(_LayerParams0.z);
                float amount = saturate(_Intensity) * rate * opacity;
                if (amount <= 0.0001)
                {
                    return source;
                }

                float type = floor(_LayerParams0.x + 0.5);
                float focusDistance = _LayerParams0.w <= 0.0001 ? 1.0 : saturate(_LayerParams0.w);
                float blurAmount = _LayerParams1.x <= 0.0001 && _LayerParams1.y <= 0.0001 && _LayerParams1.z <= 0.0001 ? 1.0 : _LayerParams1.x;
                float blurSoftness = _LayerParams1.x <= 0.0001 && _LayerParams1.y <= 0.0001 && _LayerParams1.z <= 0.0001 ? 0.35 : _LayerParams1.y;
                float blurCurve = _LayerParams1.x <= 0.0001 && _LayerParams1.y <= 0.0001 && _LayerParams1.z <= 0.0001 ? 1.0 : _LayerParams1.z;
                float blendMode = _LayerParams1.x <= 0.0001 && _LayerParams1.y <= 0.0001 && _LayerParams1.z <= 0.0001 && _LayerParams1.w <= 0.0001 ? 1.0 : floor(_LayerParams1.w + 0.5);
                float speedMul = _LayerParams2.x <= 0.0001 && _LayerParams2.y <= 0.0001 && _LayerParams2.z <= 0.0001 && _LayerParams2.w <= 0.0001 ? 1.0 : max(_LayerParams2.x, 0.0);
                float countMul = _LayerParams2.x <= 0.0001 && _LayerParams2.y <= 0.0001 && _LayerParams2.z <= 0.0001 && _LayerParams2.w <= 0.0001 ? 1.0 : max(_LayerParams2.y, 0.0);
                float sizeMul = _LayerParams2.x <= 0.0001 && _LayerParams2.y <= 0.0001 && _LayerParams2.z <= 0.0001 && _LayerParams2.w <= 0.0001 ? 1.0 : max(_LayerParams2.z, 0.05);
                float randomness = _LayerParams2.x <= 0.0001 && _LayerParams2.y <= 0.0001 && _LayerParams2.z <= 0.0001 && _LayerParams2.w <= 0.0001 ? 1.0 : max(_LayerParams2.w, 0.0);
                float depthSpread = _LayerParams3.x <= 0.0001 && _LayerParams3.y <= 0.0001 && _LayerParams3.z <= 0.0001 && _LayerParams3.w <= 0.0001 ? 1.0 : max(_LayerParams3.x, 0.0);
                float verticalAmount = _LayerParams3.x <= 0.0001 && _LayerParams3.y <= 0.0001 && _LayerParams3.z <= 0.0001 && _LayerParams3.w <= 0.0001 ? 1.0 : max(_LayerParams3.y, 0.0);
                float shimmerAmount = _LayerParams3.x <= 0.0001 && _LayerParams3.y <= 0.0001 && _LayerParams3.z <= 0.0001 && _LayerParams3.w <= 0.0001 ? 1.0 : max(_LayerParams3.z, 0.0);
                float driftAmount = _LayerParams3.x <= 0.0001 && _LayerParams3.y <= 0.0001 && _LayerParams3.z <= 0.0001 && _LayerParams3.w <= 0.0001 ? 1.0 : max(_LayerParams3.w, 0.0);
                float3 tint = max(_LayerColor.rgb, 0.0);
                float alpha;
                float3 result;

                if (type < 0.5)
                {
                    alpha = ImageProcessWeatherRain(input.texcoord, rate, focusDistance, blurAmount, blurSoftness, blurCurve, speedMul, countMul, sizeMul, randomness, depthSpread, verticalAmount, shimmerAmount, driftAmount) * amount;
                    result = ImageProcessWeatherBlend(source.rgb, tint, alpha * 1.25, blendMode);
                }
                else if (type < 1.5)
                {
                    alpha = ImageProcessWeatherSnow(input.texcoord, rate, focusDistance, blurAmount, blurSoftness, blurCurve, speedMul, countMul, sizeMul, randomness, depthSpread, verticalAmount, shimmerAmount, driftAmount) * amount;
                    result = ImageProcessWeatherBlend(source.rgb, tint, alpha * 1.10, blendMode);
                }
                else if (type < 2.5)
                {
                    alpha = ImageProcessWeatherSmoke(input.texcoord, rate, focusDistance, blurAmount, blurSoftness, blurCurve, speedMul, countMul, sizeMul, randomness, depthSpread, verticalAmount, shimmerAmount, driftAmount) * amount;
                    result = ImageProcessWeatherBlend(source.rgb, tint, alpha * 0.72, blendMode);
                    result = max(result + tint * alpha * 0.08, 0.0);
                }
                else
                {
                    float3 dustTint = tint;
                    alpha = ImageProcessWeatherDust(input.texcoord, rate, focusDistance, blurAmount, blurSoftness, blurCurve, speedMul, countMul, sizeMul, randomness, depthSpread, verticalAmount, shimmerAmount, driftAmount) * amount;
                    result = ImageProcessWeatherBlend(source.rgb, dustTint, alpha * 0.55, blendMode);
                }

                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
