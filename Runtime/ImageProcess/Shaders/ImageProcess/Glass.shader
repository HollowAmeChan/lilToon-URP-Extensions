Shader "Hidden/lilToon/URP/ImageProcess/Glass"
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
            Name "ImageProcess Glass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlass

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // center x/y, width/height in UV space
            float4 _LayerParams1; // rotation degrees, shape (0/1/2), corner radius, polygon sides
            float4 _LayerParams2; // blur pixels, quality, edge width pixels, edge softness pixels
            float4 _LayerParams3; // displacement enabled, displacement pixels, edge image-multiply mix, glass opacity

            static const int GlassDiscKernelCount = 24;
            static const float2 GlassDiscKernel[GlassDiscKernelCount] =
            {
                float2(0.3536, 0.3536), float2(-0.3536, 0.3536), float2(0.3536, -0.3536), float2(-0.3536, -0.3536),
                float2(0.9239, 0.3827), float2(0.3827, 0.9239), float2(-0.3827, 0.9239), float2(-0.9239, 0.3827),
                float2(-0.9239, -0.3827), float2(-0.3827, -0.9239), float2(0.3827, -0.9239), float2(0.9239, -0.3827),
                float2(0.7071, 0.0), float2(0.0, 0.7071), float2(-0.7071, 0.0), float2(0.0, -0.7071),
                float2(0.2706, 0.6533), float2(-0.2706, 0.6533), float2(0.2706, -0.6533), float2(-0.2706, -0.6533),
                float2(0.6533, 0.2706), float2(-0.6533, 0.2706), float2(0.6533, -0.2706), float2(-0.6533, -0.2706)
            };

            float GlassRoundedBoxSdf(float2 p, float2 halfExtent, float radius)
            {
                float2 q = abs(p) - max(halfExtent - radius, 0.0001);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float GlassPolygonSdf(float2 p, float radius, float sides)
            {
                float safeSides = max(3.0, round(sides));
                float angle = atan2(p.y, p.x) + 1.57079632679;
                float sector = 6.28318530718 / safeSides;
                float localAngle = frac((angle + sector * 0.5) / sector) * sector - sector * 0.5;
                float apothem = radius * cos(sector * 0.5);
                return length(p) * cos(localAngle) - apothem;
            }

            float GlassSdf(float2 uv)
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 center = _LayerParams0.xy;
                float2 halfSize = max(_LayerParams0.zw, 0.001) * 0.5;
                float2 p = (uv - center) * float2(aspect, 1.0);
                float2 extent = halfSize * float2(aspect, 1.0);
                float angle = radians(_LayerParams1.x);
                float s = sin(angle);
                float c = cos(angle);
                p = float2(c * p.x + s * p.y, -s * p.x + c * p.y);

                int shape = clamp((int)round(_LayerParams1.y), 0, 2);
                if (shape == 0)
                {
                    return GlassRoundedBoxSdf(p, extent, 0.0);
                }

                if (shape == 1)
                {
                    float radius = min(max(_LayerParams1.z, 0.0), min(extent.x, extent.y));
                    return GlassRoundedBoxSdf(p, extent, radius);
                }

                float polygonRadius = min(extent.x, extent.y);
                return GlassPolygonSdf(p, polygonRadius, _LayerParams1.w);
            }

            float GlassMask(float sdf, float feather)
            {
                float aa = max(feather, fwidth(sdf) * 1.5);
                return 1.0 - smoothstep(0.0, aa, sdf);
            }

            half3 GlassBlur(float2 uv, float radiusPixels, float quality)
            {
                float2 texel = _BlitTexture_TexelSize.xy * max(radiusPixels, 0.0);
                half3 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                int sampleCount = quality > 1.5 ? 24 : (quality > 0.5 ? 16 : 8);

                [unroll]
                for (int i = 0; i < GlassDiscKernelCount; i++)
                {
                    if (i >= sampleCount)
                    {
                        break;
                    }

                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel * GlassDiscKernel[i]).rgb;
                }

                return sum / (sampleCount + 1.0);
            }

            half4 FragGlass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float sdf = GlassSdf(uv);
                float intensity = saturate(_Intensity);
                float mask = GlassMask(sdf, _LayerParams2.w * _BlitTexture_TexelSize.y);
                float edgeWidth = max(_LayerParams2.z * _BlitTexture_TexelSize.y, fwidth(sdf) * 2.0);
                float edgeMask = saturate(1.0 - smoothstep(0.0, edgeWidth, abs(sdf))) * mask;

                float2 sampleUv = uv;
                float2 gradient = float2(ddx(sdf), ddy(sdf));
                float2 normal = normalize(gradient + float2(1e-5, 1e-5));
                float2 tangent = float2(-normal.y, normal.x);
                if (_LayerParams3.x > 0.5 && edgeMask > 0.001)
                {
                    float2 refractDirection = normalize(normal * 0.68 + tangent * 0.32);
                    sampleUv += refractDirection * (_LayerParams3.y * _BlitTexture_TexelSize.y) * edgeMask;
                }

                half3 blurred = GlassBlur(sampleUv, _LayerParams2.x, _LayerParams2.y);
                float opacity = mask * saturate(_LayerParams3.w) * intensity;
                float tintMix = saturate(_LayerParams3.z);
                half3 edgeTint = _LayerColor.rgb * lerp(float3(1.0, 1.0, 1.0), source.rgb, tintMix);
                float highlightDirection = saturate(0.5 + 0.5 * (normal.x - normal.y));
                float edgeHighlight = pow(highlightDirection, 5.0) * edgeMask * saturate(_LayerColor.a);
                edgeTint += edgeHighlight * 0.35;
                half3 glassColor = lerp(blurred, edgeTint, edgeMask * saturate(_LayerColor.a));
                half3 result = lerp(source.rgb, glassColor, opacity);
                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
