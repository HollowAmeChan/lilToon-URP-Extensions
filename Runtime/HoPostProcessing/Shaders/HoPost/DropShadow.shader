Shader "Hidden/lilToon-HoPost/URP/HoPost/DropShadow"
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
            Name "HoPost Drop Shadow"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float _SubjectMaskValid;
            float4 _LayerColor;
            float4 _LayerParams0; // x distance 0-1, y angle degrees, z opacity, w softness px
            float4 _LayerParams1; // x spread px, y sky/depth fade, z reserved, w keep off subject

            TEXTURE2D_X(_lilHoPostSubjectMaskTexture);
            float4 _lilHoPostSubjectMaskTexture_TexelSize;

            float SampleSubjectMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoPostSubjectMaskTexture, sampler_LinearClamp, uv).r;
            }

            float SampleDilatedMask(float2 uv, float radiusPx)
            {
                float2 texel = _lilHoPostSubjectMaskTexture_TexelSize.xy * max(radiusPx, 0.0);
                float mask = SampleSubjectMask(uv);
                if (radiusPx <= 0.0001)
                {
                    return mask;
                }

                mask = max(mask, SampleSubjectMask(uv + float2( texel.x, 0.0)));
                mask = max(mask, SampleSubjectMask(uv + float2(-texel.x, 0.0)));
                mask = max(mask, SampleSubjectMask(uv + float2(0.0,  texel.y)));
                mask = max(mask, SampleSubjectMask(uv + float2(0.0, -texel.y)));
                mask = max(mask, SampleSubjectMask(uv + float2( texel.x,  texel.y)));
                mask = max(mask, SampleSubjectMask(uv + float2(-texel.x,  texel.y)));
                mask = max(mask, SampleSubjectMask(uv + float2( texel.x, -texel.y)));
                mask = max(mask, SampleSubjectMask(uv + float2(-texel.x, -texel.y)));
                return mask;
            }

            float SampleSoftMask(float2 uv, float spreadPx, float softnessPx)
            {
                float center = SampleDilatedMask(uv, spreadPx);
                if (softnessPx <= 0.0001)
                {
                    return center;
                }

                float2 texel = _lilHoPostSubjectMaskTexture_TexelSize.xy * softnessPx;
                float mask = center * 0.24;
                mask += SampleDilatedMask(uv + float2( texel.x, 0.0), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2(-texel.x, 0.0), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2(0.0,  texel.y), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2(0.0, -texel.y), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2( texel.x,  texel.y), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2(-texel.x,  texel.y), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2( texel.x, -texel.y), spreadPx) * 0.095;
                mask += SampleDilatedMask(uv + float2(-texel.x, -texel.y), spreadPx) * 0.095;
                return saturate(mask);
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

            float ResolveSkyFade(float2 uv)
            {
                float depthFade = saturate(_LayerParams1.y);
                if (depthFade <= 0.0001)
                {
                    return 1.0;
                }

                float rawDepth = SampleSceneDepth(uv);
                float linearDepth = Linear01Depth(rawDepth, _ZBufferParams);
                float skyMask = smoothstep(0.985, 1.0, linearDepth);
                return lerp(1.0, 1.0 - skyMask, depthFade);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float opacity = saturate(_Intensity) * saturate(_LayerParams0.z) * _LayerColor.a;
                if (_SubjectMaskValid <= 0.5 || opacity <= 0.0001)
                {
                    return source;
                }

                float distance = max(_LayerParams0.x, 0.0);
                float minDimension = min(_lilHoPostSubjectMaskTexture_TexelSize.z, _lilHoPostSubjectMaskTexture_TexelSize.w);
                float distancePx = distance <= 1.0 ? distance * minDimension * 0.08 : distance;
                float angleRadians = radians(_LayerParams0.y);
                float2 direction = float2(cos(angleRadians), sin(angleRadians));
                float2 offset = direction * distancePx * _lilHoPostSubjectMaskTexture_TexelSize.xy;

                float spreadPx = max(_LayerParams1.x, 0.0);
                float softnessPx = max(_LayerParams0.w, 0.0);
                float shiftedMask = SampleSoftMask(uv - offset, spreadPx, softnessPx);
                float subjectMask = SampleSubjectMask(uv);
                float keepOffSubject = _LayerParams1.w <= 0.0001 ? 1.0 : saturate(_LayerParams1.w);
                float shadowMask = saturate(shiftedMask - subjectMask * keepOffSubject);
                shadowMask *= ResolveSkyFade(uv);

                float amount = shadowMask * opacity;
                if (amount <= 0.0001)
                {
                    return source;
                }

                half3 blended = ApplyBlend(source.rgb, (half3)_LayerColor.rgb, _LayerBlendMode);
                return half4(lerp(source.rgb, blended, amount), source.a);
            }
            ENDHLSL
        }
    }
}
