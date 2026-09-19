Shader "Hidden/lilToon/URP/ScreenProcess/DepthOfField"
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
            Name "ScreenProcess Depth Of Field"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessMask.hlsl"

            float _Intensity;
            float4 _LayerParams0; // x mode 0 Gaussian 1 Bokeh 2 Target Bokeh, y focus distance, z focal length, w aperture
            float4 _LayerParams1; // x gaussian start, y gaussian end, z max radius px, w high quality
            float4 _LayerParams2; // x blade count, y blade curvature, z blade rotation
            float4 _LayerParams3; // x coc gain, y foreground boost, z background boost, w coc curve

            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture);
            float _HoGeometryBufferValid;

            // Tap layout: golden-angle spiral, r = sqrt((i + 0.5) / N).
            // sqrt() is what makes the taps uniform per unit AREA instead of per unit radius, so the
            // disk has no inner ring gap and no empty outer ring; a golden-angle sequence keeps them
            // from lining up into spokes. Regenerate with .codex-research/dof_sim/dof_bleed_check.py
            // --print-kernels; that script also verifies these tables still match the formula.
            static const int ScreenProcessDofKernelLqCount = 16;
            static const float2 ScreenProcessDofKernelLq[ScreenProcessDofKernelLqCount] =
            {
                float2( 0.176777,  0.000000),
                float2(-0.225772,  0.206826),
                float2( 0.034558, -0.393771),
                float2( 0.284571,  0.371173),
                float2(-0.522223, -0.092374),
                float2( 0.494695, -0.314685),
                float2(-0.165466,  0.615525),
                float2(-0.315561, -0.607594),
                float2( 0.684642,  0.250030),
                float2(-0.712256,  0.294009),
                float2( 0.343354, -0.733729),
                float2( 0.253730,  0.808932),
                float2(-0.764746, -0.443186),
                float2( 0.897134, -0.197232),
                float2(-0.547507,  0.778772),
                float2(-0.126487, -0.976090)
            };

            static const int ScreenProcessDofKernelHqCount = 48;
            static const float2 ScreenProcessDofKernelHq[ScreenProcessDofKernelHqCount] =
            {
                float2( 0.102062,  0.000000),
                float2(-0.130350,  0.119411),
                float2( 0.019952, -0.227344),
                float2( 0.164297,  0.214297),
                float2(-0.301506, -0.053332),
                float2( 0.285613, -0.181683),
                float2(-0.095532,  0.355374),
                float2(-0.182189, -0.350795),
                float2( 0.395278,  0.144355),
                float2(-0.411221,  0.169746),
                float2( 0.198236, -0.423618),
                float2( 0.146491,  0.467037),
                float2(-0.441526, -0.255873),
                float2( 0.517961, -0.113872),
                float2(-0.316103,  0.449624),
                float2(-0.073027, -0.563546),
                float2( 0.448315,  0.377841),
                float2(-0.603292,  0.024948),
                float2( 0.440055, -0.437914),
                float2(-0.029441,  0.636697),
                float2(-0.418714, -0.501759),
                float2( 0.663289,  0.089245),
                float2(-0.562003,  0.391027),
                float2( 0.153572, -0.682641),
                float2( 0.355203,  0.619877),
                float2(-0.694388, -0.221529),
                float2( 0.674510, -0.311641),
                float2(-0.292214,  0.698232),
                float2(-0.260795, -0.725077),
                float2( 0.693947,  0.364719),
                float2(-0.770801,  0.203181),
                float2( 0.438129, -0.681391),
                float2( 0.139371,  0.810962),
                float2(-0.660498, -0.511527),
                float2( 0.844897, -0.069998),
                float2(-0.584002,  0.631289),
                float2( 0.004254, -0.872008),
                float2( 0.593868,  0.654653),
                float2(-0.891769, -0.082649),
                float2( 0.722597, -0.548425),
                float2(-0.164407,  0.903726),
                float2(-0.495233, -0.786974),
                float2( 0.907502,  0.248709),
                float2(-0.846956,  0.434644),
                float2( 0.334703, -0.902805),
                float2( 0.367093,  0.901754),
                float2(-0.889424, -0.421515),
                float2( 0.950623, -0.293087)
            };

            // The centre tap is the pixel's own colour. It always participates, both because nothing
            // should be able to erase a pixel's own value and because the reach test below would
            // otherwise reject it whenever the pixel itself is in focus.
            static const float ScreenProcessDofCenterWeight = 1.25;
            static const float ScreenProcessDofReachMargin = 0.1;
            static const float ScreenProcessDofReachMarginMinPx = 1.0;

            half4 SampleOutlineNormalDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture, sampler_PointClamp, uv);
            }

            float SampleEyeDepth(float2 uv)
            {
                if (_HoGeometryBufferValid <= 0.5)
                {
                    return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                }

                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                return LilHoGeometryBufferLinearDepthOrFar(normalDepth, _ProjectionParams.z);
            }

            // The outline shell has its own linear eye depth, so an outline pixel is focused and
            // blurred like the surface it hugs instead of being force-kept sharp. It is used for the
            // centre pixel AND for every tap, which is what keeps the outline consistent with itself.
            float SampleVisualEyeDepth(float2 uv)
            {
                if (_HoGeometryBufferValid <= 0.5)
                {
                    return SampleEyeDepth(uv);
                }

                half4 outlineNormalDepth = SampleOutlineNormalDepth(uv);
                return LilHoGeometryBufferCoverage(outlineNormalDepth) > 0.5
                    ? outlineNormalDepth.a
                    : SampleEyeDepth(uv);
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

            // Signed CoC: negative = nearer than the focus plane (foreground), positive = farther
            // (background). Gaussian mode has no focus plane, so it only ever produces a far side.
            float ResolveSignedCoc(float depth)
            {
                int mode = (int)round(_LayerParams0.x);
                if (mode < 1)
                {
                    return ResolveGaussianCoc(depth);
                }

                float signedDelta = depth - max(_LayerParams0.y, 0.001);
                return sign(signedDelta) * ResolveBokehCoc(depth);
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

            // Gather with CoC-consistent weights.
            //
            // A tap may only fill the part of the blur circle its OWN CoC can cover: if the tap is
            // sharper than the distance it sits at, letting it in is what smears an in-focus subject
            // over an out-of-focus background (and vice versa). The margin keeps that cut-off from
            // being a hard edge in the weighting.
            half4 SampleBlur(float2 uv, float radiusPx, float maxRadiusPx, float marginPx)
            {
                float2 texel = _BlitTexture_TexelSize.xy;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * ScreenProcessDofCenterWeight;
                float weight = ScreenProcessDofCenterWeight;
                float highQuality = step(0.5, _LayerParams1.w);

                #define ADD_DOF_SAMPLE(dir) \
                    { \
                        float2 offsetPx = ResolveBokehOffset(dir) * radiusPx; \
                        float2 sampleUv = uv + offsetPx * texel; \
                        float distPx = length(offsetPx); \
                        float tapCoc = ResolveSignedCoc(SampleVisualEyeDepth(sampleUv)); \
                        float tapWeight = saturate((abs(tapCoc) * maxRadiusPx - distPx + marginPx) / marginPx); \
                        color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv) * tapWeight; \
                        weight += tapWeight; \
                    }

                if (highQuality > 0.5)
                {
                    [unroll]
                    for (int i = 0; i < ScreenProcessDofKernelHqCount; i++)
                    {
                        ADD_DOF_SAMPLE(ScreenProcessDofKernelHq[i])
                    }
                }
                else
                {
                    [unroll]
                    for (int i = 0; i < ScreenProcessDofKernelLqCount; i++)
                    {
                        ADD_DOF_SAMPLE(ScreenProcessDofKernelLq[i])
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
                if (LilScreenProcessShouldOutputMaskDebug())
                {
                    return LilScreenProcessMaskDebugColor(uv, false, source.a);
                }

                float depth = SampleVisualEyeDepth(uv);
                float coc = ResolveSignedCoc(depth);
                float maxRadiusPx = max(_LayerParams1.z, 0.0);
                float radiusPx = abs(coc) * maxRadiusPx;
                // The blur strength lives in radiusPx: a pixel with a small CoC gets a small
                // disk, which is what a lens does. _Intensity only fades the whole layer, so it
                // must not scale with the CoC as well - doing that kept partially defocused
                // pixels mostly sharp and turned them into a ghost of the sharp image.
                float amount = saturate(_Intensity) * LilScreenProcessResolveLayerMask(uv);
                if (radiusPx <= 0.0001 || amount <= 0.0001)
                {
                    return source;
                }

                float marginPx = max(radiusPx * ScreenProcessDofReachMargin, ScreenProcessDofReachMarginMinPx);
                half4 blurred = SampleBlur(uv, radiusPx, maxRadiusPx, marginPx);
                return half4(lerp(source.rgb, blurred.rgb, amount), source.a);
            }
            ENDHLSL
        }
    }
}
