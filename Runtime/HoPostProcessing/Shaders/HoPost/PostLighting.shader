Shader "Hidden/lilToon-HoPost/URP/HoPost/PostLighting"
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
            Name "HoPost Post Lighting"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/AOV/Shaders/HoAOV/HoAovSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x mode, y brightness, z contrast, w opacity
            float4 _LayerParams1; // x angle degrees, y gradient width, z offset, w normal amount
            float4 _LayerParams2; // x center x, y center y, z radius, w softness
            float4 _LayerParams3; // gradient color A
            float4 _LayerParams4; // gradient color B
            float4 _LayerParams5; // x ambient, y shadow amount, z matcap invert, w matcap focus

            TEXTURE2D_X(_lilHoAovNormalDepthTexture);

            half4 SampleAovNormalDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_PointClamp, uv);
            }

            half3 ApplyBlend(half3 baseColor, half3 layerColor, float blendMode)
            {
                int mode = (int)round(blendMode);
                if (mode == 1)
                {
                    return max(baseColor + layerColor, 0.0);
                }

                if (mode == 2)
                {
                    return 1.0 - (1.0 - baseColor) * (1.0 - layerColor);
                }

                if (mode == 3)
                {
                    return baseColor * layerColor;
                }

                return layerColor;
            }

            float ApplyContrast(float value, float contrast)
            {
                float slope = lerp(1.0, 5.0, saturate(contrast));
                return saturate((value - 0.5) * slope + 0.5);
            }

            float ResolveCoverage(float2 uv, half4 normalDepth)
            {
                float coverage = LilHoAovCoverage(normalDepth);
                if (_LayerAovMaskEnabled > 0.5)
                {
                    return coverage * LilHoPostResolveRequiredAovMask(uv);
                }

                return coverage * LilHoPostAovCoverage(uv);
            }

            half3 ResolveLineGradient(float2 uv, float2 direction, float width, float offset, half3 colorA, half3 colorB, out float gradientMask)
            {
                float projection = dot(uv - 0.5, direction);
                float t = projection / max(width, 0.0001) + 0.5 + offset;
                t = smoothstep(0.0, 1.0, saturate(t));
                gradientMask = t;
                return lerp(colorA, colorB, t);
            }

            half3 ResolveCenterGradient(float2 uv, float2 center, float radius, float softness, half3 colorA, half3 colorB, out float gradientMask)
            {
                float dist = distance(uv, center);
                float inner = saturate(1.0 - smoothstep(max(radius - softness, 0.0), radius + softness, dist));
                gradientMask = inner;
                return lerp(colorB, colorA, inner);
            }

            half3 ResolveMatcapGradient(float3 normalVS, float2 direction, float focus, float invert, half3 colorA, half3 colorB, out float gradientMask)
            {
                float2 n = normalVS.xy;
                float directional = dot(n, direction) * 0.5 + 0.5;
                float radial = 1.0 - saturate(length(n));
                float focus01 = saturate(focus / 4.0);
                float directionalLobe = pow(saturate(directional), lerp(1.0, 9.0, focus01));
                float radialLobe = pow(saturate(radial), lerp(0.55, 18.0, focus01));
                float matcap = saturate(directionalLobe * lerp(0.45, 0.30, focus01) + radialLobe * lerp(0.65, 1.35, focus01));
                matcap = smoothstep(lerp(0.0, 0.52, focus01), lerp(1.0, 0.86, focus01), matcap);
                matcap = lerp(matcap, 1.0 - matcap, saturate(invert));
                gradientMask = matcap;
                return lerp(colorB, colorA, matcap);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_lilHoAovActive <= 0.5)
                {
                    if (LilHoPostShouldOutputAovDebug())
                    {
                        return half4(0.0, 0.0, 0.0, source.a);
                    }

                    return source;
                }

                half4 normalDepth = SampleAovNormalDepth(uv);
                float subjectMask = ResolveCoverage(uv, normalDepth);
                if (LilHoPostShouldOutputAovDebug())
                {
                    return half4(subjectMask, subjectMask, subjectMask, source.a);
                }

                float3 normalWS = LilHoAovWorldNormalOrZero(normalDepth);
                if (subjectMask <= 0.0001 || dot(normalWS, normalWS) <= 0.0001)
                {
                    return source;
                }

                float3 normalVS = normalize(TransformWorldToViewDir(normalWS, true));
                int mode = (int)clamp(round(_LayerParams0.x), 0.0, 2.0);
                float brightness = max(_LayerParams0.y, 0.0);
                float contrast = saturate(_LayerParams0.z);
                float opacity = saturate(_LayerParams0.w);
                float angleRadians = radians(_LayerParams1.x);
                float2 direction = normalize(float2(cos(angleRadians), sin(angleRadians)) + 0.0001);
                float width = max(_LayerParams1.y, 0.02);
                float offset = _LayerParams1.z;
                float normalAmount = saturate(_LayerParams1.w);
                float2 center = _LayerParams2.xy;
                float radius = max(_LayerParams2.z, 0.001);
                float softness = max(_LayerParams2.w, 0.0);
                float ambient = saturate(_LayerParams5.x);
                float shadowAmount = saturate(_LayerParams5.y);
                float maskPower = 1.0;
                float matcapInvert = saturate(_LayerParams5.z);
                float matcapFocus = max(_LayerParams5.w, 0.0);

                half3 colorA = _LayerParams3.rgb;
                half3 colorB = _LayerParams4.rgb;
                if (dot(abs(colorA), half3(1.0, 1.0, 1.0)) <= 0.0001)
                {
                    colorA = _LayerColor.rgb;
                }

                if (dot(abs(colorB), half3(1.0, 1.0, 1.0)) <= 0.0001)
                {
                    colorB = _LayerColor.rgb * 0.35;
                }

                float gradientMask = 0.0;
                half3 lightColor = colorA;
                if (mode == 1)
                {
                    lightColor = ResolveCenterGradient(uv, center, radius, softness, colorA, colorB, gradientMask);
                }
                else if (mode == 2)
                {
                    lightColor = ResolveMatcapGradient(normalVS, direction, matcapFocus, matcapInvert, colorA, colorB, gradientMask);
                }
                else
                {
                    lightColor = ResolveLineGradient(uv, direction, width, offset, colorA, colorB, gradientMask);
                }

                float3 lightDirVS = normalize(float3(direction.xy * 0.85, 0.62));
                if (mode == 1)
                {
                    float2 centerDir = normalize(center - uv + 0.0001);
                    lightDirVS = normalize(float3(centerDir * 0.75, 0.70));
                }

                float ndl = saturate(dot(normalVS, lightDirVS) * 0.5 + 0.5);
                float normalLight = lerp(1.0, ndl, normalAmount);
                float shade = saturate(ambient + ApplyContrast(gradientMask * normalLight, contrast) * (1.0 - ambient));
                float shadow = lerp(1.0, shade, shadowAmount);
                float amount = saturate(pow(subjectMask, maskPower) * opacity * saturate(_Intensity));

                half3 layerColor = lightColor * brightness * shade;
                half3 blended = ApplyBlend(source.rgb * shadow, layerColor, _LayerBlendMode);
                return half4(lerp(source.rgb, blended, amount), source.a);
            }
            ENDHLSL
        }
    }
}
