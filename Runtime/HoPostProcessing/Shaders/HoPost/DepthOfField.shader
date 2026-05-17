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
            float4 _LayerParams0; // x mode 0 Gaussian 1 Bokeh, y focus distance, z focal length, w aperture
            float4 _LayerParams1; // x gaussian start, y gaussian end, z max radius px, w high quality
            float4 _LayerParams2; // x blade count, y blade curvature, z blade rotation

            float SampleEyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float ResolveGaussianCoc(float depth)
            {
                float start = max(_LayerParams1.x, 0.0);
                float end = max(_LayerParams1.y, start + 0.001);
                return saturate((depth - start) / max(end - start, 0.001));
            }

            float ResolveBokehCoc(float depth)
            {
                float focusDistance = max(_LayerParams0.y, 0.001);
                float focalLength = max(_LayerParams0.z, 1.0);
                float aperture = max(_LayerParams0.w, 0.05);
                float focusDelta = abs(depth - focusDistance);
                float lensScale = focalLength / aperture;
                return saturate(focusDelta / max(depth, 0.001) * lensScale * 0.018);
            }

            float ResolveCoc(float depth)
            {
                int mode = (int)round(_LayerParams0.x);
                return mode == 1 ? ResolveBokehCoc(depth) : ResolveGaussianCoc(depth);
            }

            float2 ResolveBokehOffset(float2 direction)
            {
                int mode = (int)round(_LayerParams0.x);
                if (mode != 1)
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
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * 2.0;
                float weight = 2.0;
                float highQuality = step(0.5, _LayerParams1.w);

                #define ADD_DOF_SAMPLE(dir, sampleWeight) \
                    color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + ResolveBokehOffset(dir) * texelRadius) * sampleWeight; \
                    weight += sampleWeight;

                ADD_DOF_SAMPLE(float2( 1.0000,  0.0000), 1.0)
                ADD_DOF_SAMPLE(float2(-1.0000,  0.0000), 1.0)
                ADD_DOF_SAMPLE(float2( 0.0000,  1.0000), 1.0)
                ADD_DOF_SAMPLE(float2( 0.0000, -1.0000), 1.0)
                ADD_DOF_SAMPLE(float2( 0.7071,  0.7071), 1.0)
                ADD_DOF_SAMPLE(float2(-0.7071,  0.7071), 1.0)
                ADD_DOF_SAMPLE(float2( 0.7071, -0.7071), 1.0)
                ADD_DOF_SAMPLE(float2(-0.7071, -0.7071), 1.0)
                ADD_DOF_SAMPLE(float2( 0.9239,  0.3827), highQuality)
                ADD_DOF_SAMPLE(float2(-0.9239,  0.3827), highQuality)
                ADD_DOF_SAMPLE(float2( 0.9239, -0.3827), highQuality)
                ADD_DOF_SAMPLE(float2(-0.9239, -0.3827), highQuality)
                ADD_DOF_SAMPLE(float2( 0.3827,  0.9239), highQuality)
                ADD_DOF_SAMPLE(float2(-0.3827,  0.9239), highQuality)
                ADD_DOF_SAMPLE(float2( 0.3827, -0.9239), highQuality)
                ADD_DOF_SAMPLE(float2(-0.3827, -0.9239), highQuality)

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
