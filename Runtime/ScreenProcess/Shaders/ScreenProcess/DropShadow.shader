Shader "Hidden/lilToon/URP/ScreenProcess/DropShadow"
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
            Name "ScreenProcess Drop Shadow"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessRuleMask.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x distance 0-1, y angle degrees, z opacity, w softness px
            float4 _LayerParams1; // x spread px, y reserved, z reserved, w keep off subject

            float SampleSubjectMask(float2 uv)
            {
                return LilScreenProcessResolveRequiredRuleMask(uv);
            }

            float SampleSpreadMask(float2 uv, float radiusPx)
            {
                float2 texel = LilScreenProcessRuleTexelSize() * max(radiusPx, 0.0);
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

            float SampleBlurredMask(float2 uv, float softnessPx)
            {
                float center = SampleSubjectMask(uv);
                if (softnessPx <= 0.0001)
                {
                    return center;
                }

                float2 texel = LilScreenProcessRuleTexelSize() * softnessPx;
                float mask = center * 0.24;
                mask += SampleSubjectMask(uv + float2( texel.x, 0.0)) * 0.095;
                mask += SampleSubjectMask(uv + float2(-texel.x, 0.0)) * 0.095;
                mask += SampleSubjectMask(uv + float2(0.0,  texel.y)) * 0.095;
                mask += SampleSubjectMask(uv + float2(0.0, -texel.y)) * 0.095;
                mask += SampleSubjectMask(uv + float2( texel.x,  texel.y)) * 0.095;
                mask += SampleSubjectMask(uv + float2(-texel.x,  texel.y)) * 0.095;
                mask += SampleSubjectMask(uv + float2( texel.x, -texel.y)) * 0.095;
                mask += SampleSubjectMask(uv + float2(-texel.x, -texel.y)) * 0.095;
                return saturate(mask);
            }

            float ResolveSimpleShadowMask(float2 uv, float2 offset)
            {
                float spreadPx = max(_LayerParams1.x, 0.0);
                float softnessPx = max(_LayerParams0.w, 0.0);
                float shifted = SampleSpreadMask(uv - offset, spreadPx);
                shifted = max(shifted, SampleBlurredMask(uv - offset, softnessPx));
                float original = SampleSubjectMask(uv);
                float keepOffSubject = _LayerParams1.w <= 0.0001 ? 1.0 : saturate(_LayerParams1.w);
                return saturate(shifted - original * keepOffSubject);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (LilScreenProcessShouldOutputRuleDebug())
                {
                    return LilScreenProcessRuleDebugColor(uv, true, source.a);
                }

                float opacity = saturate(_Intensity) * saturate(_LayerParams0.z);
                if (_lilHoAovActive <= 0.5 || opacity <= 0.0001)
                {
                    return source;
                }

                float distance = max(_LayerParams0.x, 0.0);
                float2 ruleTextureSize = LilScreenProcessRuleTextureSize();
                float minDimension = min(ruleTextureSize.x, ruleTextureSize.y);
                float distancePx = distance <= 1.0 ? distance * minDimension * 0.08 : distance;
                float angleRadians = radians(_LayerParams0.y);
                float2 direction = float2(cos(angleRadians), sin(angleRadians));
                float2 offset = direction * distancePx * LilScreenProcessRuleTexelSize();

                float shadowMask = ResolveSimpleShadowMask(uv, offset);
                float amount = shadowMask * opacity;
                if (amount <= 0.0001)
                {
                    return source;
                }

                half3 shadowColor = (half3)_LayerColor.rgb;
                return half4(lerp(source.rgb, shadowColor, amount), source.a);
            }
            ENDHLSL
        }
    }
}
