Shader "Hidden/lilToon-HoPost/URP/HoPost/DepthOfField"
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
            Name "HoPost Depth Of Field"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x mode 0 Gaussian 1 Bokeh 2 Target Bokeh, y focus distance, z focal length, w aperture
            float4 _LayerParams1; // x gaussian start, y gaussian end, z max radius px, w high quality
            float4 _LayerParams2; // x blade count, y blade curvature, z blade rotation
            float4 _LayerParams3; // x coc gain, y foreground boost, z background boost, w coc curve

            static const int HoPostDofKernelLqCount = 12;
            static const float2 HoPostDofKernelLq[HoPostDofKernelLqCount] =
            {
                float2(-0.326212, -0.405810),
                float2(-0.840144, -0.073580),
                float2(-0.695914,  0.457137),
                float2(-0.203345,  0.620716),
                float2( 0.962340, -0.194983),
                float2( 0.473434, -0.480026),
                float2( 0.519456,  0.767022),
                float2( 0.185461, -0.893124),
                float2( 0.507431,  0.064425),
                float2( 0.896420,  0.412458),
                float2(-0.321940, -0.932615),
                float2(-0.791559, -0.597710)
            };

            static const int HoPostDofKernelHqCount = 28;
            static const float3 HoPostDofKernelHq[HoPostDofKernelHqCount] =
            {
                float3( 0.62463,  0.54337, 0.82790),
                float3(-0.13414, -0.94488, 0.95435),
                float3( 0.38772, -0.43475, 0.58253),
                float3( 0.12126, -0.19282, 0.22778),
                float3(-0.20388,  0.11133, 0.23230),
                float3( 0.83114, -0.29218, 0.88100),
                float3( 0.10759, -0.57839, 0.58831),
                float3( 0.28285,  0.79036, 0.83945),
                float3(-0.36622,  0.39516, 0.53876),
                float3( 0.75591,  0.21916, 0.78704),
                float3(-0.52610,  0.02386, 0.52664),
                float3(-0.88216, -0.24471, 0.91547),
                float3(-0.48888, -0.29330, 0.57011),
                float3( 0.44014, -0.08558, 0.44838),
                float3( 0.21179,  0.51373, 0.55567),
                float3( 0.05483,  0.95701, 0.95858),
                float3(-0.59001, -0.70509, 0.91938),
                float3(-0.80065,  0.24631, 0.83768),
                float3(-0.19424, -0.18402, 0.26757),
                float3(-0.43667,  0.76751, 0.88304),
                float3( 0.21666,  0.11602, 0.24577),
                float3( 0.15696, -0.85600, 0.87027),
                float3(-0.75821,  0.58363, 0.95682),
                float3( 0.99284, -0.02904, 0.99327),
                float3(-0.22234, -0.57907, 0.62029),
                float3( 0.55052, -0.66984, 0.86704),
                float3( 0.46431,  0.28115, 0.54280),
                float3(-0.07214,  0.60554, 0.60982)
            };

            float SampleEyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float ResolvePositiveDefault(float value, float fallback)
            {
                return value > 0.0001 ? value : fallback;
            }

            float ResolveGaussianCoc(float depth)
            {
                float start = max(_LayerParams1.x, 0.0);
                float end = max(_LayerParams1.y, start + 0.001);
                float gain = ResolvePositiveDefault(_LayerParams3.x, 1.0);
                float curve = max(ResolvePositiveDefault(_LayerParams3.w, 1.0), 0.25);
                float coc = saturate((depth - start) / max(end - start, 0.001) * gain);
                return pow(coc, curve);
            }

            float ResolveBokehCoc(float depth)
            {
                float focusDistance = max(_LayerParams0.y, 0.001);
                float focalLength = max(_LayerParams0.z, 1.0);
                float aperture = max(_LayerParams0.w, 0.05);
                float signedDelta = depth - focusDistance;
                float focusDelta = abs(signedDelta);
                float gain = ResolvePositiveDefault(_LayerParams3.x, 1.0);
                float foregroundBoost = ResolvePositiveDefault(_LayerParams3.y, 1.0);
                float backgroundBoost = ResolvePositiveDefault(_LayerParams3.z, 1.0);
                float sideBoost = signedDelta < 0.0 ? foregroundBoost : backgroundBoost;
                float curve = max(ResolvePositiveDefault(_LayerParams3.w, 1.0), 0.25);
                float lensScale = focalLength / aperture;
                float coc = focusDelta / max(focusDistance, 0.001) * lensScale * 0.014 * gain * sideBoost;
                return pow(saturate(coc), curve);
            }

            float ResolveCoc(float depth)
            {
                int mode = (int)round(_LayerParams0.x);
                return mode >= 1 ? ResolveBokehCoc(depth) : ResolveGaussianCoc(depth);
            }

            float2 ResolveBokehOffset(float2 direction)
            {
                int mode = (int)round(_LayerParams0.x);
                if (mode < 1)
                {
                    return direction;
                }

                float bladeCount = clamp(round(_LayerParams2.x), 3.0, 9.0);
                float curvature = saturate(_LayerParams2.y);
                float rotation = radians(_LayerParams2.z);
                float angle = atan2(direction.y, direction.x) + rotation;
                float sector = 6.2831853 / bladeCount;
                float bladeAngle = abs(frac(angle / sector + 0.5) * 2.0 - 1.0);
                float polygon = lerp(0.72, 1.0, curvature + (1.0 - curvature) * bladeAngle);
                return direction * polygon;
            }

            half4 SampleBlur(float2 uv, float radiusPx)
            {
                float2 texelRadius = _BlitTexture_TexelSize.xy * radiusPx;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 1.25;
                float weight = 1.25;
                float highQuality = step(0.5, _LayerParams1.w);

                #define ADD_DOF_SAMPLE(dir, sampleWeight) \
                    color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + ResolveBokehOffset(dir) * texelRadius) * sampleWeight; \
                    weight += sampleWeight;

                if (highQuality > 0.5)
                {
                    [unroll]
                    for (int i = 0; i < HoPostDofKernelHqCount; i++)
                    {
                        float3 kernel = HoPostDofKernelHq[i];
                        ADD_DOF_SAMPLE(kernel.xy, lerp(1.12, 0.9, saturate(kernel.z)))
                    }
                }
                else
                {
                    [unroll]
                    for (int i = 0; i < HoPostDofKernelLqCount; i++)
                    {
                        ADD_DOF_SAMPLE(HoPostDofKernelLq[i], 1.0)
                    }
                }

                #undef ADD_DOF_SAMPLE
                return color / max(weight, 0.0001);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (LilHoPostShouldOutputAovDebug())
                {
                    return LilHoPostAovDebugColor(uv, false, source.a);
                }

                float depth = SampleEyeDepth(uv);
                float coc = ResolveCoc(depth);
                float radiusPx = coc * max(_LayerParams1.z, 0.0);
                float amount = saturate(coc * _Intensity) * LilHoPostResolveAovLayerMask(uv);
                if (radiusPx <= 0.0001 || amount <= 0.0001)
                {
                    return source;
                }

                half4 blurred = SampleBlur(uv, radiusPx);
                return half4(lerp(source.rgb, blurred.rgb, amount), source.a);
            }
            ENDHLSL
        }
    }
}
