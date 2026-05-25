Shader "Hidden/lilToon-HoPost/URP/HoPost/EdgeLight"
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
            Name "HoPost Edge Light"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x size, y brightness, z contrast, w opacity
            float4 _LayerParams1; // x angle degrees, y mode, z outer width px, w outer amount
            float4 _LayerParams2; // x surface weight, y depth edge weight, z depth sensitivity, w direction amount

            TEXTURE2D_X(_lilHoAovNormalDepthTexture);
            float4 _lilHoAovNormalDepthTexture_TexelSize;

            struct BoundaryInfo
            {
                float edge;
                float2 direction;
            };

            half4 SampleAovNormalDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_lilHoAovNormalDepthTexture, sampler_PointClamp, uv);
            }

            int ResolveMode()
            {
                return (int)clamp(round(_LayerParams1.y), 0.0, 3.0);
            }

            float4 ResolveRimTuning()
            {
                float4 tuning = _LayerParams2;
                if (dot(abs(tuning), float4(1.0, 1.0, 1.0, 1.0)) <= 0.0001)
                {
                    tuning = float4(1.0, 0.65, 0.45, 1.0);
                }

                return float4(max(tuning.x, 0.0), max(tuning.y, 0.0), saturate(tuning.z), saturate(tuning.w));
            }

            float ResolveEdgeMask(float2 uv, half4 normalDepth)
            {
                float depthCoverage = LilHoGeometryBufferCoverage(normalDepth);
                if (_lilHoAovActive <= 0.5 || depthCoverage <= 0.0)
                {
                    return 0.0;
                }

                if (_LayerAovMaskEnabled > 0.5)
                {
                    return depthCoverage * LilHoPostResolveRequiredAovMask(uv);
                }

                return depthCoverage * LilHoPostAovCoverage(uv);
            }

            float ResolveNeighborMask(float2 uv, out half4 normalDepth)
            {
                normalDepth = SampleAovNormalDepth(uv);
                return ResolveEdgeMask(uv, normalDepth);
            }

            float ResolveDirectionalMask(float2 rimVector, int mode, float directionAmount)
            {
                if (directionAmount <= 0.0001)
                {
                    return 1.0;
                }

                float vectorLength = length(rimVector);
                if (vectorLength <= 0.0001)
                {
                    return 1.0;
                }

                float angleRadians = radians(_LayerParams1.x);
                float2 lightDirection = float2(cos(angleRadians), sin(angleRadians));
                float directional = dot(rimVector / vectorLength, lightDirection);
                directional = mode == 1 || mode == 3 ? abs(directional) : saturate(directional);
                if (mode == 2 || mode == 3)
                {
                    directional = pow(saturate(directional), 1.75);
                }

                return lerp(1.0, saturate(directional), directionAmount);
            }

            float ResolveDepthJump(float centerDepth, float neighborDepth, float centerMask, float neighborMask, float sensitivity)
            {
                float sharedCoverage = centerMask * neighborMask;
                if (sharedCoverage <= 0.0001)
                {
                    return 0.0;
                }

                float relativeDepthDelta = abs(centerDepth - neighborDepth) / max(max(centerDepth, neighborDepth), 0.01);
                float threshold = lerp(0.08, 0.004, sensitivity);
                float softness = max(threshold * 2.0, 0.002);
                return smoothstep(threshold, threshold + softness, relativeDepthDelta) * sharedCoverage;
            }

            void AccumulateBoundarySample(
                inout BoundaryInfo boundary,
                float2 uv,
                float2 texel,
                float2 offset,
                float centerDepth,
                float centerMask,
                float sensitivity)
            {
                half4 neighborNormalDepth;
                float neighborMask = ResolveNeighborMask(uv + texel * offset, neighborNormalDepth);
                float missingEdge = saturate(centerMask - neighborMask);
                float depthEdge = ResolveDepthJump(centerDepth, neighborNormalDepth.a, centerMask, neighborMask, sensitivity);
                float sampleEdge = max(missingEdge, depthEdge);
                boundary.edge = max(boundary.edge, sampleEdge);
                boundary.direction += offset * sampleEdge;
            }

            BoundaryInfo ResolveDepthBoundary(float2 uv, half4 normalDepth, float centerMask, float sensitivity)
            {
                BoundaryInfo boundary;
                boundary.edge = 0.0;
                boundary.direction = float2(0.0, 0.0);

                if (centerMask <= 0.0001)
                {
                    return boundary;
                }

                float radiusPx = lerp(1.0, 4.0, saturate(_LayerParams0.x));
                float2 texel = _lilHoAovNormalDepthTexture_TexelSize.xy * radiusPx;
                float centerDepth = normalDepth.a;

                AccumulateBoundarySample(boundary, uv, texel, float2( 1.0,  0.0), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2(-1.0,  0.0), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2( 0.0,  1.0), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2( 0.0, -1.0), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2( 0.7071,  0.7071), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2(-0.7071,  0.7071), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2( 0.7071, -0.7071), centerDepth, centerMask, sensitivity);
                AccumulateBoundarySample(boundary, uv, texel, float2(-0.7071, -0.7071), centerDepth, centerMask, sensitivity);
                return boundary;
            }

            void AccumulateOuterSample(
                inout BoundaryInfo outer,
                float2 uv,
                float2 texel,
                float2 offset,
                float centerMask)
            {
                half4 neighborNormalDepth;
                float neighborMask = ResolveNeighborMask(uv + texel * offset, neighborNormalDepth);
                float sampleOuter = saturate(neighborMask - centerMask);
                outer.edge = max(outer.edge, sampleOuter);
                outer.direction -= offset * sampleOuter;
            }

            float ResolveOuterMask(float2 uv, float centerMask, int mode, float directionAmount)
            {
                float radiusPx = max(_LayerParams1.z, 0.0);
                float outerAmount = saturate(_LayerParams1.w);
                if (radiusPx <= 0.0001 || outerAmount <= 0.0001)
                {
                    return 0.0;
                }

                float2 texel = _lilHoAovNormalDepthTexture_TexelSize.xy * radiusPx;
                BoundaryInfo outer;
                outer.edge = 0.0;
                outer.direction = float2(0.0, 0.0);
                AccumulateOuterSample(outer, uv, texel, float2( 1.0,  0.0), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2(-1.0,  0.0), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2( 0.0,  1.0), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2( 0.0, -1.0), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2( 0.7071,  0.7071), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2(-0.7071,  0.7071), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2( 0.7071, -0.7071), centerMask);
                AccumulateOuterSample(outer, uv, texel, float2(-0.7071, -0.7071), centerMask);

                return outer.edge * outerAmount * ResolveDirectionalMask(outer.direction, mode, directionAmount);
            }

            float ApplyContrast(float value, float contrast)
            {
                float slope = lerp(1.0, 6.0, saturate(contrast));
                return saturate((value - 0.5) * slope + 0.5);
            }

            float ResolveSurfaceRim(float3 normalWS, int mode, float surfaceWeight, float directionAmount)
            {
                if (dot(normalWS, normalWS) <= 0.0001)
                {
                    return 0.0;
                }

                float3 normalVS = normalize(TransformWorldToViewDir(normalWS, true));
                float normalRim = 1.0 - saturate(abs(normalVS.z));

                float size = saturate(_LayerParams0.x);
                float edge0 = saturate(1.0 - max(size, 0.0001));
                float rim = normalRim * ResolveDirectionalMask(normalVS.xy, mode, directionAmount);
                rim = smoothstep(edge0, 1.0, rim);
                return rim * surfaceWeight;
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
                float subjectMask = ResolveEdgeMask(uv, normalDepth);
                if (LilHoPostShouldOutputAovDebug())
                {
                    return half4(subjectMask, subjectMask, subjectMask, source.a);
                }

                float3 normalWS = LilHoGeometryBufferWorldNormalOrZero(normalDepth);
                int mode = ResolveMode();
                float4 tuning = ResolveRimTuning();
                float surfaceRim = ResolveSurfaceRim(normalWS, mode, tuning.x, tuning.w) * subjectMask;
                BoundaryInfo depthBoundary = ResolveDepthBoundary(uv, normalDepth, subjectMask, tuning.z);
                float depthRim = depthBoundary.edge * tuning.y * ResolveDirectionalMask(depthBoundary.direction, mode, tuning.w);
                float rim = saturate(surfaceRim + depthRim);
                if (mode == 2 || mode == 3)
                {
                    rim = pow(saturate(rim), 2.0);
                }

                rim = ApplyContrast(rim, _LayerParams0.z);
                rim = saturate(rim + ResolveOuterMask(uv, subjectMask, mode, tuning.w));

                float amount = rim * saturate(_Intensity) * saturate(_LayerParams0.w);
                if (amount <= 0.0001)
                {
                    return source;
                }

                half3 lightColor = (half3)_LayerColor.rgb * max(_LayerParams0.y, 0.0);
                half3 blended = ApplyBlend(source.rgb, lightColor, _LayerBlendMode);
                return half4(lerp(source.rgb, blended, amount), source.a);
            }
            ENDHLSL
        }
    }
}
