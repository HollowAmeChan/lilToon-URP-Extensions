Shader "Hidden/lilToon-Shoost/URP/Shoost/LensFlare"
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
            Name "Shoost Lens Flare"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLensFlare

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            static const int MaxLensFlareGhosts = 7;
            static const int MaxLensFlareRays = 8;
            static const float LensFlarePi = 3.14159265359;

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x source x [-1,1], y source y [-1,1], z axis angle degrees, w axis length
            float4 _LayerParams1; // x core size, y halo size, z ray length, w ray count
            float4 _LayerParams2; // x ghost intensity, y ghost spacing, z ring intensity, w chromatic dispersion
            float4 _LayerParams3; // x anamorphic streak, y exposure, z blend mode, w show only flare

            float ShoostLensFlareHash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 ShoostLensFlareAspectDelta(float2 uv, float2 center)
            {
                float aspect = max(_ScreenParams.x / max(_ScreenParams.y, 1.0), 0.0001);
                float2 d = uv - center;
                d.x *= aspect;
                return d;
            }

            float ShoostLensFlareSoftDisc(float2 uv, float2 center, float radius, float softness)
            {
                float dist = length(ShoostLensFlareAspectDelta(uv, center));
                radius = max(radius, 0.0001);
                softness = max(softness, 0.0001);
                return 1.0 - smoothstep(radius, radius + softness, dist);
            }

            float ShoostLensFlareGaussian(float2 uv, float2 center, float radius)
            {
                float dist = length(ShoostLensFlareAspectDelta(uv, center));
                radius = max(radius, 0.0001);
                return exp2(-dist * dist / (radius * radius));
            }

            float ShoostLensFlareRing(float2 uv, float2 center, float radius, float width)
            {
                float dist = length(ShoostLensFlareAspectDelta(uv, center));
                float band = abs(dist - radius);
                width = max(width, 0.0001);
                return 1.0 - smoothstep(width, width * 2.6, band);
            }

            float ShoostLensFlareRay(float2 delta, float2 axis, float width, float lengthValue)
            {
                float2 side = float2(-axis.y, axis.x);
                float along = dot(delta, axis);
                float across = abs(dot(delta, side));
                float thin = exp2(-across * across / max(width * width, 0.000001));
                float fade = exp2(-abs(along) / max(lengthValue, 0.0001));
                return thin * fade;
            }

            float3 ShoostLensFlareSpectralTint(float index, float chromatic)
            {
                float3 spectralA = float3(1.0, 0.54, 0.25);
                float3 spectralB = float3(0.38, 0.72, 1.0);
                float3 spectralC = float3(0.86, 0.38, 1.0);
                float phase = frac(index * 0.381966);
                float3 spectral = lerp(lerp(spectralA, spectralB, smoothstep(0.0, 0.65, phase)), spectralC, smoothstep(0.55, 1.0, phase));
                return lerp(float3(1.0, 1.0, 1.0), spectral, saturate(chromatic));
            }

            float3 ShoostLensFlareLayer(float2 uv)
            {
                float2 sourceCenter = _LayerParams0.xy * 0.5 + 0.5;
                float axisLength = max(_LayerParams0.w, 0.0);
                float angle = radians(_LayerParams0.z);
                float2 axis = float2(cos(angle), sin(angle));
                float2 sourceDelta = ShoostLensFlareAspectDelta(uv, sourceCenter);

                float coreSize = max(_LayerParams1.x, 0.0);
                float haloSize = max(_LayerParams1.y, 0.0);
                float rayLength = max(_LayerParams1.z, 0.0);
                int rayCount = (int)clamp(round(_LayerParams1.w), 2.0, (float)MaxLensFlareRays);
                float ghostIntensity = max(_LayerParams2.x, 0.0);
                float ghostSpacing = max(_LayerParams2.y, 0.0);
                float ringIntensity = max(_LayerParams2.z, 0.0);
                float chromatic = saturate(_LayerParams2.w);
                float streakIntensity = max(_LayerParams3.x, 0.0);
                float exposure = max(_LayerParams3.y, 0.0);
                float3 tint = max(_LayerColor.rgb, 0.0);

                float sourceDist = length(sourceDelta);
                float sourceGate = 1.0 - smoothstep(1.05, 1.45, length(sourceCenter - 0.5));
                float core = ShoostLensFlareSoftDisc(uv, sourceCenter, coreSize, coreSize * 0.85 + 0.006) * 4.8;
                float corona = ShoostLensFlareGaussian(uv, sourceCenter, max(haloSize, coreSize + 0.001)) * 1.55;
                float outerGlow = exp2(-sourceDist * 2.6 / max(haloSize, 0.001)) * 0.38;

                float rays = 0.0;
                [loop]
                for (int i = 0; i < MaxLensFlareRays; i++)
                {
                    if (i >= rayCount)
                    {
                        break;
                    }

                    float rayAngle = angle + ((float)i / max((float)rayCount, 1.0)) * LensFlarePi;
                    float2 rayAxis = float2(cos(rayAngle), sin(rayAngle));
                    float rayWidth = lerp(0.0035, 0.010, ShoostLensFlareHash(float2(i, rayCount)));
                    rays += ShoostLensFlareRay(sourceDelta, rayAxis, rayWidth, max(rayLength, 0.0001));
                }

                rays = rays / max((float)rayCount, 1.0) * 1.9;
                float streak = ShoostLensFlareRay(sourceDelta, axis, 0.0065, max(axisLength * 1.4, 0.001)) * streakIntensity * 1.35;
                float rings = ShoostLensFlareRing(uv, sourceCenter, max(haloSize * 0.72, coreSize + 0.02), max(haloSize * 0.045, 0.002)) * ringIntensity * 0.55;
                rings += ShoostLensFlareRing(uv, sourceCenter, max(haloSize * 1.15, coreSize + 0.04), max(haloSize * 0.032, 0.002)) * ringIntensity * 0.30;

                float3 flare = tint * (core + corona + outerGlow + rays + streak + rings) * sourceGate;

                [loop]
                for (int g = 0; g < MaxLensFlareGhosts; g++)
                {
                    float t = ((float)g + 1.0) / (float)(MaxLensFlareGhosts + 1);
                    float signedOffset = lerp(-0.95, 0.95, t) * axisLength * ghostSpacing;
                    float2 ghostCenter = sourceCenter + axis * signedOffset;
                    float size = lerp(0.030, 0.095, ShoostLensFlareHash(float2(g, 17.0))) * lerp(0.75, 1.25, t);
                    float brightness = ghostIntensity * lerp(0.55, 0.18, t) * (1.0 - smoothstep(1.12, 1.42, length(ghostCenter - 0.5)));
                    float ghostDisc = ShoostLensFlareSoftDisc(uv, ghostCenter, size, size * 1.35);
                    float ghostRing = ShoostLensFlareRing(uv, ghostCenter, size * 1.55, max(size * 0.20, 0.002)) * ringIntensity * 0.36;
                    float3 ghostTint = tint * ShoostLensFlareSpectralTint((float)g, chromatic);
                    flare += ghostTint * (ghostDisc * 0.82 + ghostRing) * brightness * sourceGate;
                }

                float edgeFade = 1.0 - smoothstep(0.15, 1.35, length(uv - 0.5));
                float grain = lerp(1.0, 0.92 + ShoostLensFlareHash(floor(uv * _ScreenParams.xy)) * 0.16, 0.22);
                return max(flare * exposure * lerp(0.82, 1.0, edgeFade) * grain, 0.0);
            }

            float3 ShoostLensFlareComposite(float3 source, float3 flare, float amount, float mode)
            {
                int blendMode = clamp((int)round(mode), 0, 2);
                if (blendMode == 1)
                {
                    float3 screened = 1.0 - (1.0 - saturate(source)) * (1.0 - saturate(flare));
                    return lerp(source, screened + max(source - 1.0, 0.0), amount);
                }

                if (blendMode == 2)
                {
                    return lerp(source, flare, amount);
                }

                return source + flare * amount;
            }

            half4 FragLensFlare(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float amount = saturate(_Intensity);
                if (amount <= 0.0001 || _LayerParams3.y <= 0.0001)
                {
                    return source;
                }

                float3 flare = ShoostLensFlareLayer(input.texcoord);
                if (_LayerParams3.w > 0.5)
                {
                    return half4(flare * amount, source.a);
                }

                return half4(ShoostLensFlareComposite(source.rgb, flare, amount, _LayerParams3.z), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
