Shader "Hidden/lilToon/URP/ImageProcess/Particle"
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
            Name "ImageProcess Feather Particle"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragParticle

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x spawn rate, y opacity, z fade speed, w count
            float4 _LayerParams1; // x direction mode, y line angle, z speed, w direction strength
            float4 _LayerParams2; // x spawn center x, y spawn center y, z/w unused
            float4 _LayerParams3; // x drift, y sway frequency, z turbulence, w randomness
            float4 _LayerParams4; // x size, y texture rotation, z size random, w rotation random
            float4 _LayerParams5; // x random hue, y saturation, z value, w random brightness
            float4 _LayerParams6; // x motion noise, y noise frequency, z 3D rotation, w blend mode
            float4 _LayerParams7; // x depth layers, y foreground scale, z foreground blur, w background blur
            float4 _LayerParams8; // x texture alpha mode: 0 auto, 1 alpha, 2 grayscale mask, y global fade invert, z global fade softness, w params version
            float4 _LayerParams9; // x global fade mode, y fade angle, z fade strength, w fade range
            float4 _LogoTextureEnabled0;

            TEXTURE2D(_LogoTexture0);
            TEXTURE2D(_LogoTexture1);
            TEXTURE2D(_LogoTexture2);
            TEXTURE2D(_LogoTexture3);
            float4 _LogoTexture0_TexelSize;
            float4 _LogoTexture1_TexelSize;
            float4 _LogoTexture2_TexelSize;
            float4 _LogoTexture3_TexelSize;

            float ImageProcessParticleHash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 ImageProcessParticleHash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float ImageProcessParticleNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = ImageProcessParticleHash12(i);
                float b = ImageProcessParticleHash12(i + float2(1.0, 0.0));
                float c = ImageProcessParticleHash12(i + float2(0.0, 1.0));
                float d = ImageProcessParticleHash12(i + float2(1.0, 1.0));
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ImageProcessParticleFbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    value += ImageProcessParticleNoise(p) * amplitude;
                    p = p * 2.07 + 19.19;
                    amplitude *= 0.5;
                }

                return value;
            }

            float GetFeatherTextureEnabled(int index)
            {
                return _LogoTextureEnabled0[index];
            }

            int GetEnabledFeatherTextureCount()
            {
                int count = 0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    count += GetFeatherTextureEnabled(i) > 0.5 ? 1 : 0;
                }

                return count;
            }

            int SelectEnabledFeatherTexture(float randomValue)
            {
                int enabledCount = GetEnabledFeatherTextureCount();
                if (enabledCount <= 0)
                {
                    return -1;
                }

                int target = min((int)floor(saturate(randomValue) * enabledCount), enabledCount - 1);
                int seen = 0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (GetFeatherTextureEnabled(i) > 0.5)
                    {
                        if (seen == target)
                        {
                            return i;
                        }

                        seen++;
                    }
                }

                return 0;
            }

            half4 SampleFeatherTexture(int index, float2 uv)
            {
                if (index == 0) return SAMPLE_TEXTURE2D(_LogoTexture0, sampler_LinearClamp, uv);
                if (index == 1) return SAMPLE_TEXTURE2D(_LogoTexture1, sampler_LinearClamp, uv);
                if (index == 2) return SAMPLE_TEXTURE2D(_LogoTexture2, sampler_LinearClamp, uv);
                return SAMPLE_TEXTURE2D(_LogoTexture3, sampler_LinearClamp, uv);
            }

            half4 SampleFeatherTextureLod(int index, float2 uv, float mipLevel)
            {
                if (index == 0) return SAMPLE_TEXTURE2D_LOD(_LogoTexture0, sampler_LinearClamp, uv, mipLevel);
                if (index == 1) return SAMPLE_TEXTURE2D_LOD(_LogoTexture1, sampler_LinearClamp, uv, mipLevel);
                if (index == 2) return SAMPLE_TEXTURE2D_LOD(_LogoTexture2, sampler_LinearClamp, uv, mipLevel);
                return SAMPLE_TEXTURE2D_LOD(_LogoTexture3, sampler_LinearClamp, uv, mipLevel);
            }

            float GetFeatherTextureAspect(int index)
            {
                if (index == 0 && _LogoTexture0_TexelSize.z > 0.0) return max(_LogoTexture0_TexelSize.w / _LogoTexture0_TexelSize.z, 0.1);
                if (index == 1 && _LogoTexture1_TexelSize.z > 0.0) return max(_LogoTexture1_TexelSize.w / _LogoTexture1_TexelSize.z, 0.1);
                if (index == 2 && _LogoTexture2_TexelSize.z > 0.0) return max(_LogoTexture2_TexelSize.w / _LogoTexture2_TexelSize.z, 0.1);
                if (index == 3 && _LogoTexture3_TexelSize.z > 0.0) return max(_LogoTexture3_TexelSize.w / _LogoTexture3_TexelSize.z, 0.1);
                return 2.35;
            }

            float3 ImageProcessParticleRgbToHsv(float3 c)
            {
                float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 ImageProcessParticleHsvToRgb(float3 c)
            {
                float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
                return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
            }

            float3 RotateAroundAxis(float3 value, float3 axis, float angle)
            {
                axis = normalize(axis);
                float s = sin(angle);
                float c = cos(angle);
                return value * c + cross(axis, value) * s + axis * dot(axis, value) * (1.0 - c);
            }

            float4 SampleProceduralFeather(float2 uv, float breakup)
            {
                float2 centered = uv * 2.0 - 1.0;
                centered.x *= 0.72;
                centered.y = centered.y * 1.08 + 0.05;

                float spineCurve = sin((centered.y + 0.2) * 2.1) * 0.055;
                float spineDistance = abs(centered.x - spineCurve);
                float halfWidth = max(0.02, (1.0 - centered.y * centered.y) * 0.46);
                halfWidth *= lerp(0.75, 1.08, smoothstep(-0.75, 0.15, centered.y));
                float body = 1.0 - smoothstep(halfWidth, halfWidth + 0.08, abs(centered.x));
                float tip = smoothstep(-0.98, -0.68, centered.y) * (1.0 - smoothstep(0.72, 1.03, centered.y));
                float spine = 1.0 - smoothstep(0.012, 0.052, spineDistance);
                float vaneLines = pow(abs(sin((centered.x * 22.0 + centered.y * 8.0) + spineCurve * 10.0)), 1.7);
                float fray = ImageProcessParticleFbm(uv * 14.0) * ImageProcessParticleFbm(float2(uv.y * 20.0, uv.x * 8.0));
                float edgeFray = smoothstep(0.55, 0.95, fray + abs(centered.x) * 0.42 + saturate(centered.y) * 0.18);
                float alpha = saturate(body * tip * lerp(1.0, 0.58 + edgeFray * 0.72, saturate(breakup)) + spine * 0.72);
                float shade = 0.72 + spine * 0.28 + vaneLines * 0.09 + ImageProcessParticleFbm(uv * 22.0) * 0.10;
                return float4(shade.xxx, alpha);
            }

            float AlphaFromSample(float4 sampleValue, float textureMode)
            {
                float luminance = dot(sampleValue.rgb, float3(0.299, 0.587, 0.114));
                float alpha = saturate(sampleValue.a);
                float mask = saturate(luminance);
                if (textureMode > 1.5)
                {
                    return mask;
                }

                if (textureMode > 0.5)
                {
                    return alpha;
                }

                // Auto keeps transparent PNGs intact, while black-background masks can still work.
                return saturate(max(alpha * step(alpha, 0.995), mask * alpha) * 1.35);
            }

            float4 ResolveFeatherSample(float4 premultipliedSample)
            {
                float alpha = saturate(premultipliedSample.a);
                return float4(premultipliedSample.rgb / max(alpha, 0.0001), alpha);
            }

            float4 SampleFeatherTap(int textureIndex, float2 uv, float textureMode, float mipLevel)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                if (textureIndex < 0)
                {
                    float4 procedural = SampleProceduralFeather(uv, 0.0);
                    float alpha = saturate(procedural.a * inside);
                    return float4(procedural.rgb * alpha, alpha);
                }

                float4 sampleValue = SampleFeatherTextureLod(textureIndex, uv, mipLevel);
                float sampleAlpha = AlphaFromSample(sampleValue, textureMode);
                float3 sampleColor = textureMode > 1.5 ? float3(1.0, 1.0, 1.0) : sampleValue.rgb;
                float alpha = saturate(sampleAlpha * inside);
                return float4(sampleColor * alpha, alpha);
            }

            float4 SampleFeather(int textureIndex, float2 uv, float textureMode, float blurAmount, float blurRadius)
            {
                blurAmount = max(blurAmount, 0.0);
                float mipLevel = min(blurAmount * 2.2 + blurAmount * blurAmount * 0.35, 5.0);
                if (blurAmount <= 0.0001 || blurRadius <= 0.0001)
                {
                    return ResolveFeatherSample(SampleFeatherTap(textureIndex, uv, textureMode, 0.0));
                }

                float2 axis = float2(blurRadius, blurRadius);
                float2 diagonal = axis * 0.7071068;
                float4 sum = SampleFeatherTap(textureIndex, uv, textureMode, mipLevel) * 0.24;
                sum += SampleFeatherTap(textureIndex, uv + float2(axis.x, 0.0), textureMode, mipLevel) * 0.12;
                sum += SampleFeatherTap(textureIndex, uv - float2(axis.x, 0.0), textureMode, mipLevel) * 0.12;
                sum += SampleFeatherTap(textureIndex, uv + float2(0.0, axis.y), textureMode, mipLevel) * 0.12;
                sum += SampleFeatherTap(textureIndex, uv - float2(0.0, axis.y), textureMode, mipLevel) * 0.12;
                sum += SampleFeatherTap(textureIndex, uv + diagonal, textureMode, mipLevel) * 0.07;
                sum += SampleFeatherTap(textureIndex, uv - diagonal, textureMode, mipLevel) * 0.07;
                sum += SampleFeatherTap(textureIndex, uv + float2(diagonal.x, -diagonal.y), textureMode, mipLevel) * 0.07;
                sum += SampleFeatherTap(textureIndex, uv + float2(-diagonal.x, diagonal.y), textureMode, mipLevel) * 0.07;
                return ResolveFeatherSample(sum);
            }

            float FeatherDepthBlur(float depth, float foregroundBlur, float backgroundBlur)
            {
                float foreground = saturate((0.48 - depth) / 0.48) * max(foregroundBlur, 0.0);
                float background = saturate((depth - 0.52) / 0.48) * max(backgroundBlur, 0.0);
                return saturate(max(foreground, background));
            }

            float3 BlendFeather(float3 source, float3 feather, float alpha, float blendMode)
            {
                alpha = saturate(alpha);
                feather = max(feather, 0.0);
                if (blendMode < 0.5)
                {
                    return lerp(source, feather, alpha);
                }

                if (blendMode < 1.5)
                {
                    return max(source + feather * alpha, 0.0);
                }

                if (blendMode < 2.5)
                {
                    float3 screenColor = 1.0 - (1.0 - source) * (1.0 - saturate(feather));
                    return lerp(source, screenColor, alpha);
                }

                float3 softTint = saturate(feather);
                float3 softLight = (1.0 - 2.0 * softTint) * source * source + 2.0 * softTint * source;
                return lerp(source, softLight, alpha);
            }

            float FeatherFadeRamp(float edge0, float edge1, float value, float softness)
            {
                softness = max(softness, 0.0);
                float center = (edge0 + edge1) * 0.5;
                float halfWidth = max((edge1 - edge0) * 0.5, 0.0001) * max(softness, 0.0001);
                float t = saturate((value - (center - halfWidth)) / max(halfWidth * 2.0, 0.0001));
                float smooth = t * t * (3.0 - 2.0 * t);
                float smoother = smooth * smooth * (3.0 - 2.0 * smooth);
                smooth = lerp(smooth, smoother, saturate((softness - 1.0) * 0.5));
                float hard = step(0.5, t);
                return softness <= 0.0001 ? hard : smooth;
            }

            float GlobalFeatherFade(float2 uv, float fadeMode, float fadeAngle, float fadeStrength, float fadeRange, float fadeInvert, float fadeSoftness)
            {
                fadeStrength = saturate(fadeStrength);
                if (fadeStrength <= 0.0001)
                {
                    return 1.0;
                }

                float range = max(fadeRange, 0.05);
                float fade = 1.0;
                if (fadeMode > 0.5)
                {
                    float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                    float2 centered = uv - 0.5;
                    centered.x *= aspect;
                    float distanceFromCenter = length(centered) / max(length(float2(0.5 * aspect, 0.5)), 0.0001);
                    fade = 1.0 - FeatherFadeRamp(range * 0.5, range, saturate(distanceFromCenter), fadeSoftness);
                }
                else
                {
                    float angle = radians(fadeAngle);
                    float2 direction = float2(cos(angle), sin(angle));
                    float directional = dot(uv - 0.5, direction) * 0.7071068 + 0.5;
                    fade = FeatherFadeRamp(0.5 - range * 0.5, 0.5 + range * 0.5, directional, fadeSoftness);
                }

                fade = lerp(fade, 1.0 - fade, saturate(fadeInvert));
                return lerp(1.0, saturate(fade), fadeStrength);
            }

            void AccumulateFeather(
                float2 uv,
                float2 cell,
                float layer,
                float count,
                float spawnRate,
                float opacity,
                float fadeSpeed,
                float directionMode,
                float lineAngle,
                float speed,
                float directionStrength,
                float2 spawnCenter,
                float drift,
                float swayFrequency,
                float turbulence,
                float randomness,
                float sizeBase,
                float textureRotation,
                float sizeRandom,
                float rotationRandom,
                float randomHue,
                float saturation,
                float value,
                float randomBrightness,
                float motionNoise,
                float noiseFrequency,
                float rotation3DStrength,
                float depthLayers,
                float foregroundScale,
                float foregroundBlur,
                float backgroundBlur,
                float textureMode,
                inout float3 accumColor,
                inout float accumAlpha)
            {
                float2 random0 = ImageProcessParticleHash22(cell + layer * 29.17);
                float2 random1 = ImageProcessParticleHash22(cell + layer * 73.31 + 17.0);
                float random2 = ImageProcessParticleHash12(cell + layer * 113.1 + 3.7);
                float random3 = ImageProcessParticleHash12(cell + layer * 149.7 + 11.4);

                if (random0.x > spawnRate)
                {
                    return;
                }

                float lifetime = lerp(1.25, 7.5, 1.0 - saturate(fadeSpeed));
                lifetime *= lerp(0.70, 1.35, random2);
                float age = frac((_Time.y * max(speed, 0.0) / max(lifetime, 0.001)) + random0.y + layer * 0.131);
                float fadeIn = smoothstep(0.0, 0.16, age);
                float fadeOut = 1.0 - smoothstep(lerp(0.46, 0.88, 1.0 - saturate(fadeSpeed)), 1.0, age);
                float fade = fadeIn * fadeOut;

                float fakeDepth = saturate((layer + random1.x * max(depthLayers, 0.0)) / max(count - 1.0 + max(depthLayers, 0.0), 1.0));
                float depthBlur = FeatherDepthBlur(fakeDepth, foregroundBlur, backgroundBlur);
                float nearScale = lerp(1.0, max(foregroundScale, 0.05), pow(1.0 - fakeDepth, 1.65));
                float depthAlpha = lerp(1.2, 0.64, fakeDepth);

                float2 spawn = float2(
                    lerp(-0.22, 1.22, random1.x),
                    lerp(-0.22, 1.22, random1.y));
                float2 direction = float2(cos(radians(lineAngle)), sin(radians(lineAngle)));
                float2 radialVector = spawn - spawnCenter + (random0 - 0.5) * 0.08;
                float radialLength = length(radialVector);
                float2 radial = radialLength > 0.0001 ? radialVector / radialLength : direction;
                float2 mixedDirection = lerp(direction, radial, saturate(directionMode));
                direction = mixedDirection / max(length(mixedDirection), 0.0001);
                float perspectivePush = directionMode > 0.5 ? lerp(0.45, 2.1, 1.0 - fakeDepth) : 1.0;
                float travel = age * directionStrength * perspectivePush;
                float2 position = spawn + direction * travel;

                float sway = sin((_Time.y * 1.25 + random2 * 6.2831853 + age * 6.2831853) * max(swayFrequency, 0.0));
                float noise = ImageProcessParticleFbm(position * max(noiseFrequency, 0.1) + _Time.y * 0.15 + random3 * 11.0) - 0.5;
                float2 side = float2(-direction.y, direction.x);
                position += side * (sway * drift * 0.10 + noise * turbulence * 0.15 + (random0 - 0.5) * randomness * 0.10);
                position += float2(
                    sin(_Time.y * 0.47 + random2 * 7.2),
                    cos(_Time.y * 0.31 + random3 * 9.1)) * motionNoise * 0.03;

                int textureIndex = SelectEnabledFeatherTexture(ImageProcessParticleHash12(cell + layer * 191.7 + 5.3));
                float localSize = sizeBase * nearScale * lerp(1.0, lerp(0.58, 1.75, random2), saturate(sizeRandom));
                float localAspect = max(GetFeatherTextureAspect(textureIndex), 0.1);
                float baseRotation = atan2(direction.y, direction.x) - 1.5707963;
                float rotation = baseRotation + textureRotation + lerp(-3.1415926, 3.1415926, random3) * saturate(rotationRandom);
                rotation += sway * drift * 0.42 + noise * turbulence * 0.45;

                float2 scale = float2(max(localSize / max(localAspect, 0.0001), 0.0001), max(localSize, 0.0001));
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float perspective3D = saturate(rotation3DStrength) * lerp(0.35, 1.0, saturate(directionMode));
                float flutter = sin(_Time.y * lerp(0.75, 1.6, random2) + age * 6.2831853 + random3 * 8.0);
                float pitch = (lerp(-1.05, 1.05, random1.y) + dot(direction, float2(0.0, 1.0)) * 0.34 * saturate(directionMode) + flutter * 0.22) * perspective3D;
                float yaw = (lerp(-1.12, 1.12, random2) + dot(direction, float2(1.0, 0.0)) * 0.38 * saturate(directionMode) + noise * 0.42) * perspective3D;
                float depthZ = lerp(0.68, 2.25, fakeDepth) * lerp(0.92, 1.18, saturate(directionMode) * age);

                float rotationSin = sin(rotation);
                float rotationCos = cos(rotation);
                float3 right3 = float3(rotationCos, rotationSin, 0.0);
                float3 up3 = float3(-rotationSin, rotationCos, 0.0);
                float3 normal3 = float3(0.0, 0.0, 1.0);
                if (perspective3D > 0.0001)
                {
                    up3 = RotateAroundAxis(up3, right3, pitch);
                    normal3 = RotateAroundAxis(normal3, right3, pitch);
                    right3 = RotateAroundAxis(right3, up3, yaw);
                    normal3 = RotateAroundAxis(normal3, up3, yaw);
                }

                float2 screenUv = uv - 0.5;
                float2 centerScreen = position - 0.5;
                float3 ray = float3(screenUv.x * aspect, screenUv.y, 1.0);
                float3 center3 = float3(centerScreen.x * aspect * depthZ, centerScreen.y * depthZ, depthZ);
                float denom = dot(ray, normal3);
                if (abs(denom) <= 0.015)
                {
                    return;
                }

                float t = dot(center3, normal3) / denom;
                if (t <= 0.0)
                {
                    return;
                }

                float3 hit = ray * t;
                float3 local3 = hit - center3;
                float2 featherUv = float2(
                    dot(local3, right3) / max(scale.x * depthZ, 0.0001),
                    dot(local3, up3) / max(scale.y * depthZ, 0.0001)) + 0.5;
                float planeFacing = saturate(abs(normal3.z));
                float blurAmount = depthBlur * max(foregroundBlur, backgroundBlur);
                float blurRadius = blurAmount * lerp(0.010, 0.036, saturate(localSize * 7.0));
                float blurMargin = blurRadius * 1.5;
                if (featherUv.x < -blurMargin || featherUv.x > 1.0 + blurMargin || featherUv.y < -blurMargin || featherUv.y > 1.0 + blurMargin)
                {
                    return;
                }

                float4 feather = SampleFeather(textureIndex, featherUv, textureMode, blurAmount, blurRadius);
                if (feather.a <= 0.0001)
                {
                    return;
                }

                float3 particleColorRandom = float3(
                    ImageProcessParticleHash12(cell + layer * 211.3 + 19.1),
                    ImageProcessParticleHash12(cell + layer * 233.7 + 41.9),
                    ImageProcessParticleHash12(cell + layer * 269.1 + 73.3));
                float3 featherTintedColor = max(feather.rgb, 0.0) * max(_LayerColor.rgb, 0.0);
                float randomHueStrength = saturate(randomHue);
                float randomBrightnessStrength = saturate(randomBrightness);
                float tintedValue = max(max(featherTintedColor.r, featherTintedColor.g), featherTintedColor.b);
                float3 randomHueColor = ImageProcessParticleHsvToRgb(float3(particleColorRandom.x, 1.0, 1.0)) * tintedValue;
                float randomBrightnessValue = smoothstep(0.30 * randomBrightnessStrength, 1.0, particleColorRandom.z);
                float randomBrightnessScale = lerp(1.0, randomBrightnessValue * 1.45, randomBrightnessStrength);
                float3 featherBaseColor = lerp(featherTintedColor, randomHueColor, randomHueStrength) * randomBrightnessScale;
                float3 hsv = ImageProcessParticleRgbToHsv(featherBaseColor);
                hsv.y = saturate(hsv.y * max(saturation, 0.0));
                hsv.z = max(hsv.z * max(value, 0.0), 0.0);
                float3 featherColor = ImageProcessParticleHsvToRgb(hsv);
                featherColor *= lerp(1.0, 0.58 + planeFacing * 0.52, perspective3D);

                float planeAlpha = lerp(1.0, 0.30 + planeFacing * 0.90, perspective3D * 0.75);
                float alpha = feather.a * opacity * fade * depthAlpha * planeAlpha * saturate(_Intensity);
                accumColor += featherColor * alpha * (1.0 - accumAlpha);
                accumAlpha = saturate(accumAlpha + alpha * (1.0 - accumAlpha));
            }

            half4 FragParticle(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float spawnRate = saturate(_LayerParams0.x);
                float opacity = saturate(_LayerParams0.y);
                float fadeSpeed = saturate(_LayerParams0.z);
                float count = clamp(round(_LayerParams0.w <= 0.0001 ? 13.0 : _LayerParams0.w), 1.0, 32.0);
                if (spawnRate <= 0.0001 || opacity <= 0.0001 || _Intensity <= 0.0001)
                {
                    return source;
                }

                float4 one = float4(1.0, 1.0, 1.0, 1.0);
                bool p1Default = dot(abs(_LayerParams1), one) <= 0.0001;
                bool p2Default = dot(abs(_LayerParams2), one) <= 0.0001;
                bool p3Default = dot(abs(_LayerParams3), one) <= 0.0001;
                bool p4Default = dot(abs(_LayerParams4), one) <= 0.0001;
                bool p5Default = dot(abs(_LayerParams5), one) <= 0.0001;
                bool p6Default = dot(abs(_LayerParams6), one) <= 0.0001;
                bool p7Default = dot(abs(_LayerParams7), one) <= 0.0001;
                bool p8Default = dot(abs(_LayerParams8), one) <= 0.0001;
                bool p9Default = dot(abs(_LayerParams9), one) <= 0.0001;

                float4 p1 = p1Default ? float4(0.0, -90.0, 0.16, 0.85) : _LayerParams1;
                float4 p2 = p2Default ? float4(0.5, 0.58, 0.0, 0.0) : _LayerParams2;
                float4 p3 = p3Default ? float4(0.62, 0.85, 0.35, 2.0) : _LayerParams3;
                float4 p4 = p4Default ? float4(0.16, 0.0, 0.55, 0.34) : _LayerParams4;
                float4 p5 = p5Default ? float4(0.0, 1.0, 1.0, 0.22) : _LayerParams5;
                float4 p6 = p6Default ? float4(0.13, 2.4, 0.65, 0.58) : _LayerParams6;
                float4 p7 = p7Default ? float4(1.15, 1.45, 0.75, 1.25) : _LayerParams7;
                float4 p8 = p8Default ? float4(2.0, 0.0, 1.0, 3.0) : _LayerParams8;
                float4 p9 = p9Default ? float4(0.0, -90.0, 0.0, 0.75) : _LayerParams9;

                float directionMode = saturate(round(p1.x));
                float lineAngle = p1.y;
                float speed = max(p1.z, 0.0);
                float directionStrength = max(p1.w, 0.0);
                float2 spawnCenter = p2.xy;
                float drift = max(p3.x, 0.0);
                float swayFrequency = max(p3.y, 0.0);
                float turbulence = max(p3.z, 0.0);
                float randomness = max(p3.w, 0.0);
                float sizeBase = max(p4.x, 0.001);
                float textureRotation = radians(p4.y);
                float sizeRandom = saturate(p4.z);
                float rotationRandom = saturate(p4.w);
                float randomHue = saturate(p5.x);
                float saturation = max(p5.y, 0.0);
                float value = max(p5.z, 0.0);
                float randomBrightness = saturate(p5.w);
                float motionNoise = max(p6.x, 0.0);
                float noiseFrequency = max(p6.y, 0.1);
                float rotation3DStrength = saturate(p6.z);
                float blendMode = floor(p6.w + 0.5);
                float depthLayers = max(p7.x, 0.0);
                float foregroundScale = max(p7.y, 0.05);
                float foregroundBlur = max(p7.z, 0.0);
                float backgroundBlur = max(p7.w, 0.0);
                float textureMode = clamp(floor(p8.x + 0.5), 0.0, 2.0);
                float globalFadeInvert = saturate(round(p8.y));
                float globalFadeSoftness = p8.w < 1.5 ? 1.0 : max(p8.z, 0.0);
                float globalFadeMode = saturate(round(p9.x));
                float globalFadeAngle = p9.y;
                float globalFadeStrength = saturate(p9.z);
                float globalFadeRange = max(p9.w, 0.05);

                float3 accumColor = 0.0;
                float accumAlpha = 0.0;

                [loop]
                for (int y = 0; y < 6; y++)
                {
                    [loop]
                    for (int x = 0; x < 6; x++)
                    {
                        float layer = y * 6 + x;
                        if (layer >= count)
                        {
                            continue;
                        }

                        float2 cell = float2(layer, layer * 37.17 + 11.31);
                        AccumulateFeather(
                            uv,
                            cell,
                            layer,
                            count,
                            spawnRate,
                            opacity,
                            fadeSpeed,
                            directionMode,
                            lineAngle,
                            speed,
                            directionStrength,
                            spawnCenter,
                            drift,
                            swayFrequency,
                            turbulence,
                            randomness,
                            sizeBase,
                            textureRotation,
                            sizeRandom,
                            rotationRandom,
                            randomHue,
                            saturation,
                            value,
                            randomBrightness,
                            motionNoise,
                            noiseFrequency,
                            rotation3DStrength,
                            depthLayers,
                            foregroundScale,
                            foregroundBlur,
                            backgroundBlur,
                            textureMode,
                            accumColor,
                            accumAlpha);
                    }
                }

                float globalFade = GlobalFeatherFade(uv, globalFadeMode, globalFadeAngle, globalFadeStrength, globalFadeRange, globalFadeInvert, globalFadeSoftness);
                accumColor *= globalFade;
                accumAlpha *= globalFade;

                float3 result = BlendFeather(source.rgb, accumColor / max(accumAlpha, 0.0001), accumAlpha, blendMode);
                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
