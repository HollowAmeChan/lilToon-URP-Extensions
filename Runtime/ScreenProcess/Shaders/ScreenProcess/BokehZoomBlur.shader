Shader "Hidden/lilToon/URP/ScreenProcess/BokehZoomBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ScreenProcess Bokeh Zoom Blur"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessDepthInputs.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessMask.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // focus mode, focus distance, focal length mm, f-number
            float4 _LayerParams1; // zoom radius, highlight threshold, soft knee, exposure
            float4 _LayerParams2; // center x/y [-1,1], distance decay, quality
            float4 _LayerParams3; // defocus gain, foreground multiplier, background multiplier, curve
            float4 _LayerParams4; // blade count, curvature, rotation degrees, chromatic dispersion
            float4 _LayerParams5; // bokeh gain, bokeh only, in-focus half range in metres, reserved

            float Defocus(float depth)
            {
                float focus = max(_LayerParams0.y, 0.001);
                float delta = depth - focus;
                float side = delta < 0.0 ? _LayerParams3.y : _LayerParams3.z;
                float error = max(abs(delta) - max(_LayerParams5.z, 0.0), 0.0);
                // Match the existing artistic DoF lens response, but use only this layer's inputs.
                float response = error / focus * max(_LayerParams0.z, 1.0)
                    / max(_LayerParams0.w, 0.05) * 0.014 * max(_LayerParams3.x, 0.0) * max(side, 0.0);
                return pow(saturate(response), max(_LayerParams3.w, 0.25));
            }

            float Highlight(float3 color)
            {
                float luma = dot(max(color * max(_LayerParams1.w, 0.0), 0.0), float3(0.2126, 0.7152, 0.0722));
                float threshold = max(_LayerParams1.y, 0.0);
                float knee = max(_LayerParams1.z, 0.0);
                return knee <= 0.0001 ? step(threshold, luma)
                    : smoothstep(max(threshold - knee, 0.0), threshold + knee, luma);
            }

            float Aperture(float2 direction)
            {
                float blades = round(_LayerParams4.x);
                if (blades < 3.0) return 1.0;
                blades = clamp(blades, 3.0, 12.0);
                float sector = 6.28318530718 / blades;
                float angle = atan2(direction.y, direction.x) + radians(_LayerParams4.z);
                float localAngle = angle - floor(angle / sector) * sector - sector * 0.5;
                float polygon = cos(sector * 0.5) / max(cos(localAngle), 0.05);
                return saturate(lerp(polygon, 1.0, saturate(_LayerParams4.y)));
            }

            // Gate all four color contributors independently, including visual outline depth.
            // One point-depth check followed by bilinear color can drag a focused outline outwards.
            float4 SampleDefocused(float2 uv)
            {
                float2 pixel = uv * _BlitTexture_TexelSize.zw - 0.5;
                float2 origin = (floor(pixel) + 0.5) * _BlitTexture_TexelSize.xy;
                float2 blend = frac(pixel);
                float2 uv00 = origin;
                float2 uv10 = origin + float2(_BlitTexture_TexelSize.x, 0);
                float2 uv01 = origin + float2(0, _BlitTexture_TexelSize.y);
                float2 uv11 = origin + _BlitTexture_TexelSize.xy;
                float4 eligible = float4(
                    Defocus(LilScreenProcessVisualEyeDepth(uv00)), Defocus(LilScreenProcessVisualEyeDepth(uv10)),
                    Defocus(LilScreenProcessVisualEyeDepth(uv01)), Defocus(LilScreenProcessVisualEyeDepth(uv11)));
                float4 area = float4((1-blend.x)*(1-blend.y), blend.x*(1-blend.y),
                    (1-blend.x)*blend.y, blend.x*blend.y);
                float4 weights = area * eligible;
                float coverage = dot(weights, 1.0);
                if (coverage <= 0.0001) return 0.0;
                if (all(eligible == eligible.x))
                    return float4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * coverage, coverage);
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv00).rgb * weights.x
                    + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv10).rgb * weights.y
                    + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv01).rgb * weights.z
                    + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv11).rgb * weights.w;
                return float4(color, coverage);
            }

            float4 SampleChromatic(float2 uv, float2 offset)
            {
                float4 color = SampleDefocused(uv);
                float dispersion = saturate(_LayerParams4.w);
                if (dispersion <= 0.0001) return color;
                float4 red = SampleDefocused(uv - offset * dispersion * 0.18);
                float4 blue = SampleDefocused(uv + offset * dispersion * 0.18);
                float coverage = min(color.a, min(red.a, blue.a));
                if (coverage <= 0.0001) return 0.0;
                return float4(float3(red.r / red.a, color.g / color.a, blue.b / blue.a) * coverage, coverage);
            }

            float3 ZoomBokeh(float2 uv, float defocus)
            {
                float2 direction = uv - (_LayerParams2.xy * 0.5 + 0.5);
                float aspect = abs(_BlitTexture_TexelSize.z / max(_BlitTexture_TexelSize.w, 1.0));
                float radius = saturate(_LayerParams1.x) * defocus * Aperture(direction * float2(aspect, 1.0));
                int quality = clamp((int)round(_LayerParams2.w), 0, 2);
                int count = quality == 0 ? 8 : (quality == 1 ? 16 : 32);
                float3 sum = 0.0;
                float total = 0.0;
                [loop]
                for (int i = 0; i < 32; ++i)
                {
                    if (i >= count) break;
                    float t = ((float)i + 0.5) / count;
                    float2 offset = direction * radius * t;
                    float4 sampleColor = SampleChromatic(uv - offset, offset);
                    if (sampleColor.a <= 0.0001) continue;
                    float highlight = Highlight(sampleColor.rgb / sampleColor.a);
                    float weight = pow(saturate(1.0 - t), max(_LayerParams2.z, 0.0)) * highlight;
                    sum += sampleColor.rgb * max(_LayerParams1.w, 0.0) * weight;
                    total += sampleColor.a * weight;
                }
                return total > 0.0001 ? sum / total * max(_LayerParams5.x, 0.0) * _LayerColor.rgb : 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (LilScreenProcessShouldOutputMaskDebug())
                    return LilScreenProcessMaskDebugColor(uv, false, source.a);
                if (!LilScreenProcessHasVisualDepth()) return source;
                float defocus = Defocus(LilScreenProcessVisualEyeDepth(uv));
                float amount = saturate(_Intensity) * defocus * LilScreenProcessResolveLayerMask(uv);
                bool onlyBokeh = _LayerParams5.y > 0.5;
                if (amount <= 0.0001 || _LayerParams1.x <= 0.0001 || _LayerParams5.x <= 0.0001)
                    return onlyBokeh ? half4(0, 0, 0, source.a) : source;
                float3 bokeh = ZoomBokeh(uv, defocus);
                if (onlyBokeh) return half4(bokeh * amount, source.a);
                return half4(lerp(source.rgb, ApplyLayerBlendHdr(source.rgb, bokeh, _LayerBlendMode), amount), source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
