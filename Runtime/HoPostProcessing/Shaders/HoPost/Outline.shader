Shader "Hidden/lilToon-HoPost/URP/HoPost/Outline"
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
            Name "HoPost Outline"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x thickness px, y depth weight, z normal weight, w threshold
            float4 _LayerParams1; // x softness, y depth scale, z normal scale, w opacity

            float SampleLinearDepth01(float2 uv)
            {
                return Linear01Depth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float3 SampleNormalSafe(float2 uv)
            {
                float3 normalWS = SampleSceneNormals(uv);
                return dot(normalWS, normalWS) > 0.0001 ? normalize(normalWS) : float3(0.0, 0.0, 0.0);
            }

            float SobelDepth(float2 uv, float2 texel)
            {
                float d00 = SampleLinearDepth01(uv + texel * float2(-1.0, -1.0));
                float d10 = SampleLinearDepth01(uv + texel * float2( 0.0, -1.0));
                float d20 = SampleLinearDepth01(uv + texel * float2( 1.0, -1.0));
                float d01 = SampleLinearDepth01(uv + texel * float2(-1.0,  0.0));
                float d21 = SampleLinearDepth01(uv + texel * float2( 1.0,  0.0));
                float d02 = SampleLinearDepth01(uv + texel * float2(-1.0,  1.0));
                float d12 = SampleLinearDepth01(uv + texel * float2( 0.0,  1.0));
                float d22 = SampleLinearDepth01(uv + texel * float2( 1.0,  1.0));

                float gx = d20 + 2.0 * d21 + d22 - d00 - 2.0 * d01 - d02;
                float gy = d02 + 2.0 * d12 + d22 - d00 - 2.0 * d10 - d20;
                return (abs(gx) + abs(gy)) * max(_LayerParams1.y, 0.0);
            }

            float SobelNormal(float2 uv, float2 texel)
            {
                float3 n00 = SampleNormalSafe(uv + texel * float2(-1.0, -1.0));
                float3 n10 = SampleNormalSafe(uv + texel * float2( 0.0, -1.0));
                float3 n20 = SampleNormalSafe(uv + texel * float2( 1.0, -1.0));
                float3 n01 = SampleNormalSafe(uv + texel * float2(-1.0,  0.0));
                float3 n21 = SampleNormalSafe(uv + texel * float2( 1.0,  0.0));
                float3 n02 = SampleNormalSafe(uv + texel * float2(-1.0,  1.0));
                float3 n12 = SampleNormalSafe(uv + texel * float2( 0.0,  1.0));
                float3 n22 = SampleNormalSafe(uv + texel * float2( 1.0,  1.0));

                float3 gx = n20 + 2.0 * n21 + n22 - n00 - 2.0 * n01 - n02;
                float3 gy = n02 + 2.0 * n12 + n22 - n00 - 2.0 * n10 - n20;
                return (length(gx) + length(gy)) * 0.5 * max(_LayerParams1.z, 0.0);
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
                if (LilHoPostShouldOutputAovDebug())
                {
                    return LilHoPostAovDebugColor(uv, false, source.a);
                }

                float thickness = max(_LayerParams0.x, 0.0);
                if (thickness <= 0.0001)
                {
                    return source;
                }

                float2 texel = _BlitTexture_TexelSize.xy * thickness;
                float depthEdge = SobelDepth(uv, texel) * max(_LayerParams0.y, 0.0);
                float normalEdge = SobelNormal(uv, texel) * max(_LayerParams0.z, 0.0);
                float edge = depthEdge + normalEdge;

                float threshold = max(_LayerParams0.w, 0.0);
                float softness = max(_LayerParams1.x, 0.0001);
                float mask = smoothstep(threshold, threshold + softness, edge);
                float amount = mask * saturate(_Intensity) * saturate(_LayerParams1.w) * _LayerColor.a * LilHoPostResolveAovLayerMask(uv);
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
