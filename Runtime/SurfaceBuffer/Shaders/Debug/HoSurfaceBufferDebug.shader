Shader "Hidden/lilToon/URP/SurfaceBuffer/DebugView"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HoSurfaceBuffer DebugView"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_HoSurfaceBufferColorTexture);
            TEXTURE2D_X(_HoSurfaceBufferNormalTexture);
            TEXTURE2D_X(_HoSurfaceBufferMaterialTexture);
            TEXTURE2D_X(_HoSurfaceBufferReflectionTexture);
            TEXTURE2D_X(_HoSurfaceBufferClassificationTexture);
            TEXTURE2D_X(_HoSurfaceBufferOwnerTexture);
            TEXTURE2D_X(_HoObjectBufferId0Texture);
            float _HoSurfaceBufferDebugMode;
            float _HoSurfaceBufferActive;
            float _HoObjectBufferActive;

            // owner 与 OB 层 0 的 16-bit 身份都按 `round(v * 65535)` 还原（R16_UNorm 对整数是逐值精确的）。
            uint OwnerId(float encoded)
            {
                return (uint)round(saturate(encoded) * 65535.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                uint mode = (uint)round(_HoSurfaceBufferDebugMode);

                if (_HoSurfaceBufferActive <= 0.5)
                {
                    // 没产出时给一个明确的信号色，而不是静默黑屏（与 OB / AC 同一个约定）。
                    return half4(0.35, 0.0, 0.0, 1.0);
                }

                if (mode == 1)
                {
                    return half4(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferColorTexture, sampler_PointClamp, uv).rgb, 1.0);
                }

                if (mode == 2)
                {
                    float2 octa = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferNormalTexture, sampler_PointClamp, uv).rg;
                    float3 n = PackNormalOctQuadDecode(octa * 2.0 - 1.0) * 0.5 + 0.5;
                    return half4(n, 1.0);
                }

                if (mode == 3)
                {
                    // R = 1 - perceptualRoughness（越白越光滑，看着顺），G = metallic，B = thickness。
                    float4 material = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferMaterialTexture, sampler_PointClamp, uv);
                    return half4(1.0 - material.r, material.g, material.b, 1.0);
                }

                if (mode == 4)
                {
                    return half4(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferReflectionTexture, sampler_PointClamp, uv).rg, 0.0, 1.0);
                }

                if (mode == 5)
                {
                    float4 classification = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
                    return half4(classification.r, classification.g, classification.b, 1.0);
                }

                if (mode == 6)
                {
                    // 绿 = owner 与 OB 层 0 一致；红 = 不一致或没人写；没 OB 产出时整屏洋红，跟"全不一致"分开。
                    if (_HoObjectBufferActive <= 0.5)
                    {
                        return half4(1.0, 0.0, 1.0, 1.0);
                    }

                    float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                    uint obLayer0 = ((uint)round(saturate(id0.r) * 255.0) << 8) | (uint)round(saturate(id0.g) * 255.0);
                    uint owner = OwnerId(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferOwnerTexture, sampler_PointClamp, uv).r);
                    bool aligned = owner != 0u && owner == obLayer0;
                    return aligned ? half4(0.1, 0.8, 0.2, 1.0) : half4(0.8, 0.15, 0.15, 1.0);
                }

                return half4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
