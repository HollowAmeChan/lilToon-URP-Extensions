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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;
            float4 _LayerParams0; // x size, y brightness, z contrast, w opacity
            float4 _LayerParams1; // x angle degrees, y mode, z outer width px, w outer amount

            float HasNormal(float2 uv)
            {
                float3 normalWS = SampleSceneNormals(uv);
                return step(0.0001, dot(normalWS, normalWS));
            }

            float ResolveOuterMask(float2 uv, float centerMask)
            {
                float radiusPx = max(_LayerParams1.z, 0.0);
                float outerAmount = saturate(_LayerParams1.w);
                if (radiusPx <= 0.0001 || outerAmount <= 0.0001)
                {
                    return 0.0;
                }

                float2 texel = _CameraNormalsTexture_TexelSize.xy * radiusPx;
                float neighborMask = 0.0;
                neighborMask = max(neighborMask, HasNormal(uv + float2( texel.x, 0.0)));
                neighborMask = max(neighborMask, HasNormal(uv + float2(-texel.x, 0.0)));
                neighborMask = max(neighborMask, HasNormal(uv + float2(0.0,  texel.y)));
                neighborMask = max(neighborMask, HasNormal(uv + float2(0.0, -texel.y)));
                neighborMask = max(neighborMask, HasNormal(uv + float2( texel.x,  texel.y)));
                neighborMask = max(neighborMask, HasNormal(uv + float2(-texel.x,  texel.y)));
                neighborMask = max(neighborMask, HasNormal(uv + float2( texel.x, -texel.y)));
                neighborMask = max(neighborMask, HasNormal(uv + float2(-texel.x, -texel.y)));

                return saturate(neighborMask - centerMask) * outerAmount;
            }

            float ApplyContrast(float value, float contrast)
            {
                float slope = lerp(1.0, 6.0, saturate(contrast));
                return saturate((value - 0.5) * slope + 0.5);
            }

            float ResolveRim(float3 normalWS)
            {
                if (dot(normalWS, normalWS) <= 0.0001)
                {
                    return 0.0;
                }

                float3 normalVS = normalize(TransformWorldToViewDir(normalWS, true));
                float normalRim = 1.0 - saturate(abs(normalVS.z));

                float2 normalXY = normalVS.xy;
                float normalXYLength = max(length(normalXY), 0.0001);
                float angleRadians = radians(_LayerParams1.x);
                float2 direction = float2(cos(angleRadians), sin(angleRadians));
                float directional = dot(normalXY / normalXYLength, direction);

                int mode = (int)clamp(round(_LayerParams1.y), 0.0, 3.0);
                float directionalMask = mode == 1 || mode == 3 ? abs(directional) : saturate(directional);
                float rim = normalRim * directionalMask;

                float size = saturate(_LayerParams0.x);
                float edge0 = saturate(1.0 - size);
                rim = smoothstep(edge0, 1.0, rim);

                if (mode == 2 || mode == 3)
                {
                    rim = pow(saturate(rim), 2.5);
                }

                return ApplyContrast(rim, _LayerParams0.z);
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

                float3 normalWS = SampleSceneNormals(uv);
                float subjectMask = step(0.0001, dot(normalWS, normalWS));
                float rim = ResolveRim(normalWS) * subjectMask;
                rim = saturate(rim + ResolveOuterMask(uv, subjectMask));

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
