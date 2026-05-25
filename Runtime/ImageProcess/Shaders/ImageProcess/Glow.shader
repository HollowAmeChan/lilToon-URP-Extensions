Shader "Hidden/lilToon/URP/ImageProcess/Glow"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _Intensity;
        float4 _LayerColor;
        float4 _LayerParams0; // x threshold, y soft knee, z radius, w type
        float4 _LayerParams1; // x intensity, y saturation, z contrast, w opacity
        float4 _LayerParams2; // x star count, y star rotation angle
        float _Radius;
        float _Angle;
        TEXTURE2D_X(_OriginalTex);
        SAMPLER(sampler_OriginalTex);
        TEXTURE2D_X(_BloomTex);
        SAMPLER(sampler_BloomTex);

        float LuminanceMax(float3 color)
        {
            return max(color.r, max(color.g, color.b));
        }

        half3 ApplySoftThreshold(half3 color)
        {
            float threshold = max(_LayerParams0.x, 0.0);
            float knee = max(threshold * saturate(_LayerParams0.y), 0.0001);
            float brightness = LuminanceMax((float3)color);
            float soft = saturate((brightness - threshold + knee) / (2.0 * knee));
            soft = soft * soft * knee;
            float contribution = max(brightness - threshold, soft) / max(brightness, 0.0001);
            return color * (half)saturate(contribution);
        }

        half3 ApplyBloomLook(half3 bloom)
        {
            half luma = dot(bloom, half3(0.2126, 0.7152, 0.0722));
            bloom = lerp(luma.xxx, bloom, 1.0 + (half)_LayerParams1.y);
            bloom = (bloom - 0.5) * (1.0 + (half)_LayerParams1.z) + 0.5;
            return max(bloom, half3(0.0, 0.0, 0.0)) * (half3)_LayerColor.rgb;
        }

        half4 FragPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            return half4(ApplySoftThreshold(source.rgb), source.a);
        }

        half4 FragBlur(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 texel = _BlitTexture_TexelSize.xy * max(_Radius, 0.0001);
            float2 uv = input.texcoord;
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 0.227027;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x * 1.384615, 0.0)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x * 1.384615, 0.0)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y * 1.384615)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y * 1.384615)) * 0.158108;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(2.230769, 2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(-2.230769, 2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * float2(2.230769, -2.230769)) * 0.035135;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel * float2(2.230769, 2.230769)) * 0.035135;
            return color;
        }

        half3 SampleDirection(float2 uv, float angleDegrees, float radius)
        {
            float angle = radians(angleDegrees);
            float2 direction = float2(cos(angle), sin(angle));
            float2 texel = _BlitTexture_TexelSize.xy * direction * radius;
            half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.22;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * 1.5).rgb * 0.18;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel * 1.5).rgb * 0.18;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * 3.5).rgb * 0.12;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel * 3.5).rgb * 0.12;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * 6.0).rgb * 0.09;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel * 6.0).rgb * 0.09;
            return color;
        }

        half4 FragDirectional(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            int mode = (int)round(_LayerParams0.w);
            int count = mode == 2 ? (int)clamp(round(_LayerParams2.x), 1.0, 6.0) : 1;
            half3 bloom = half3(0.0, 0.0, 0.0);

            [unroll]
            for (int i = 0; i < 6; i++)
            {
                if (i < count)
                {
                    float angle = mode == 2 ? (_LayerParams2.y + (180.0 / count) * i) : _Angle;
                    bloom += SampleDirection(uv, angle, max(_Radius, 1.0));
                }
            }

            bloom /= max((float)count, 1.0);
            return half4(bloom, 1.0);
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 source = SAMPLE_TEXTURE2D_X(_OriginalTex, sampler_OriginalTex, input.texcoord);
            half3 bloom = SAMPLE_TEXTURE2D_X(_BloomTex, sampler_BloomTex, input.texcoord).rgb;
            bloom = ApplyBloomLook(bloom);

            half amount = saturate(_Intensity) * saturate(_LayerParams1.w) * max((half)_LayerParams1.x, 0.0);
            half3 color = source.rgb + bloom * amount;
            return half4(max(color, half3(0.0, 0.0, 0.0)), source.a);
        }
        ENDHLSL

        Pass
        {
            Name "ImageProcess Glow Prefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Glow Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Glow Directional"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDirectional
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Glow Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
