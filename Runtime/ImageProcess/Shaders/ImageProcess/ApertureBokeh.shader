Shader "Hidden/lilToon/URP/ImageProcess/ApertureBokeh"
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

        static const int MaxApertureBokehSamples = 32;
        static const float ApertureBokehGoldenAngle = 2.39996323;
        static const float ApertureBokehTwoPi = 6.28318530718;

        float _Intensity;
        float _Radius;
        float4 _LayerColor;
        float4 _LayerParams0; // x aperture size, y luminance threshold, z soft knee, w exposure
        float4 _LayerParams1; // x edge extraction, y aperture hardness, z unused, w quality
        float4 _LayerParams2; // x blades, y curvature, z rotation degrees, w chromatic dispersion
        float4 _LayerParams3; // x blend mode, y show bokeh only, z bokeh gain
        TEXTURE2D_X(_OriginalTex);
        SAMPLER(sampler_OriginalTex);
        TEXTURE2D_X(_BloomTex);
        SAMPLER(sampler_BloomTex);

        half ApertureLuma(half3 color)
        {
            return dot(color, half3(0.2126, 0.7152, 0.0722));
        }

        float ApertureHash(float2 p)
        {
            p = frac(p * float2(0.1031, 0.11369));
            p += dot(p, p.yx + 19.19);
            return frac((p.x + p.y) * p.x);
        }

        half SoftHighlightMask(half3 color)
        {
            half exposure = max((half)_LayerParams0.w, 0.0);
            half threshold = max((half)_LayerParams0.y, 0.0);
            half softKnee = max((half)_LayerParams0.z, 0.0);
            half luma = ApertureLuma(max(color * exposure, 0.0));
            if (softKnee <= 0.0001)
            {
                return luma >= threshold ? 1.0 : 0.0;
            }

            return smoothstep(max(threshold - softKnee, 0.0), threshold + softKnee, luma);
        }

        half EdgeMask(float2 uv)
        {
            float2 texel = _BlitTexture_TexelSize.xy;
            half l = ApertureLuma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x, 0.0)).rgb);
            half r = ApertureLuma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb);
            half d = ApertureLuma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y)).rgb);
            half u = ApertureLuma(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb);
            return saturate((abs(r - l) + abs(u - d)) * (half)2.5);
        }

        float ApertureShape(float2 direction, float blades, float curvature, float rotationRadians)
        {
            float bladeCount = round(blades);
            if (bladeCount < 3.0)
            {
                return 1.0;
            }

            bladeCount = clamp(bladeCount, 3.0, 12.0);
            float angle = atan2(direction.y, direction.x) + rotationRadians;
            float sector = ApertureBokehTwoPi / bladeCount;
            float localAngle = angle - floor(angle / sector) * sector - sector * 0.5;
            float polygonRadius = cos(sector * 0.5) / max(cos(localAngle), 0.05);
            return max(lerp(polygonRadius, 1.0, saturate(curvature)), 0.05);
        }

        half4 FragPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            half highlight = SoftHighlightMask(source.rgb);
            half edge = EdgeMask(input.texcoord) * saturate((half)_LayerParams1.x);
            half signal = saturate(max(highlight, edge));
            half exposure = max((half)_LayerParams0.w, 0.0);
            return half4(max(source.rgb * exposure, 0.0) * signal, signal);
        }

        half4 FragBokeh(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            int quality = clamp((int)round(_LayerParams1.w), 0, 2);
            int sampleCount = quality == 0 ? 12 : (quality == 1 ? 20 : 32);
            float radius = max(_Radius, 0.0001);
            float rotation = radians(_LayerParams2.z);
            float hardness = saturate(_LayerParams1.y);
            float boundarySoftness = lerp(0.28, 0.035, hardness);
            float jitter = ApertureHash(floor(input.texcoord * _ScreenParams.xy));
            float2 texel = _BlitTexture_TexelSize.xy * radius;
            float3 sum = 0.0;
            float totalWeight = 0.0;

            [loop]
            for (int i = 0; i < MaxApertureBokehSamples; i++)
            {
                if (i >= sampleCount)
                {
                    break;
                }

                float t = ((float)i + 0.5) / max(sampleCount, 1);
                float discRadius = sqrt(t);
                float angle = ((float)i + jitter) * ApertureBokehGoldenAngle + rotation;
                float2 direction = float2(cos(angle), sin(angle));
                float shape = ApertureShape(direction, _LayerParams2.x, _LayerParams2.y, rotation);
                float normalizedRadius = saturate(discRadius / shape);
                float apertureWeight = 1.0 - smoothstep(1.0 - boundarySoftness, 1.0, normalizedRadius);
                float2 offset = direction * discRadius * texel * shape;
                float2 sampleUv = input.texcoord + offset;
                half4 sampleColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv);
                float chromatic = saturate(_LayerParams2.w);
                if (chromatic > 0.0001)
                {
                    float2 caOffset = offset * chromatic * 0.08;
                    sampleColor.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv - caOffset).r;
                    sampleColor.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv + caOffset).b;
                }
                float signal = sampleColor.a;
                float weight = max(apertureWeight * signal, 0.0);
                sum += sampleColor.rgb * weight;
                totalWeight += weight;
            }

            float3 bokeh = totalWeight > 0.0001 ? sum / totalWeight : 0.0;
            return half4(bokeh, saturate(totalWeight / max((float)sampleCount * 0.35, 1.0)));
        }

        half4 FragPostFilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 texel = _BlitTexture_TexelSize.xy;
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord) * 4.0;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0,  0.0)) * 2.0;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0,  0.0)) * 2.0;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 0.0,  1.0)) * 2.0;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 0.0, -1.0)) * 2.0;
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0,  1.0));
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0,  1.0));
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2( 1.0, -1.0));
            color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + texel * float2(-1.0, -1.0));
            return color / 16.0;
        }

        half3 CompositeBokeh(half3 source, half3 bokeh, half amount, float mode)
        {
            int blendMode = clamp((int)round(mode), 0, 3);
            if (blendMode == 1)
            {
                half3 ldrSource = saturate(source);
                half3 ldrBokeh = saturate(bokeh);
                half3 screened = 1.0 - (1.0 - ldrSource) * (1.0 - ldrBokeh);
                return lerp(source, screened + max(source - 1.0, 0.0), amount);
            }

            if (blendMode == 2)
            {
                half3 ldrSource = saturate(source);
                half3 ldrBokeh = saturate(bokeh);
                half3 overlay = lerp(2.0 * ldrSource * ldrBokeh, 1.0 - 2.0 * (1.0 - ldrSource) * (1.0 - ldrBokeh), step(0.5, ldrSource));
                return lerp(source, overlay + max(source - 1.0, 0.0), amount);
            }

            if (blendMode == 3)
            {
                return lerp(source, bokeh, amount);
            }

            return source + bokeh * amount;
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            half4 source = SAMPLE_TEXTURE2D_X(_OriginalTex, sampler_OriginalTex, input.texcoord);
            half4 bokehLayer = SAMPLE_TEXTURE2D_X(_BloomTex, sampler_BloomTex, input.texcoord);
            half3 bokeh = max(bokehLayer.rgb * _LayerColor.rgb * max((half)_LayerParams3.z, 0.0), 0.0);
            half amount = saturate((half)_Intensity);

            if (_LayerParams3.y > 0.5)
            {
                return half4(bokeh * amount, source.a);
            }

            half3 color = CompositeBokeh(source.rgb, bokeh, amount, _LayerParams3.x);
            return half4(max(color, 0.0), source.a);
        }
        ENDHLSL

        Pass
        {
            Name "ImageProcess Aperture Bokeh Prefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Aperture Bokeh Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBokeh
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Aperture Bokeh Post Filter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPostFilter
            ENDHLSL
        }

        Pass
        {
            Name "ImageProcess Aperture Bokeh Composite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
