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
            // octa / owner 的编解码与生产者（lilToon 的 SB 材质 pass）共用同一份实现，
            // 免得两边的约定各自漂移。
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SurfaceBuffer/Shaders/HoSurfaceBufferCommon.hlsl"

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

            // 没有 writer 的像素（owner = 0）一律画暗红：**"没产出"必须和"值是 0"分开**，
            // 否则一个全 0 的通道看起来和"整条链没跑"一模一样 —— 而 Classification 默认就是全 0
            // （profile 要开 SSS，curvature / transmittance / class 是新的材质属性、默认 0）。
            static const half3 NoWriterColor = half3(0.25, 0.05, 0.05);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                uint mode = (uint)round(_HoSurfaceBufferDebugMode);

                if (_HoSurfaceBufferActive <= 0.5)
                {
                    // 整条链没跑（feature 不在 renderer / 被关掉）：比"没人写"更亮的红，两者要能分开。
                    return half4(0.35, 0.0, 0.0, 1.0);
                }

                uint owner = HoSurfaceOwnerDecode(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferOwnerTexture, sampler_PointClamp, uv).r);
                bool hasWriter = owner != 0u;

                if (mode == 1)
                {
                    half3 color = (half3)SAMPLE_TEXTURE2D_X(_HoSurfaceBufferColorTexture, sampler_PointClamp, uv).rgb;
                    return half4(hasWriter ? color : NoWriterColor, 1.0);
                }

                if (mode == 2)
                {
                    float2 octa = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferNormalTexture, sampler_PointClamp, uv).rg;
                    half3 normal = (half3)(HoSurfaceOctDecode(octa) * 0.5 + 0.5);
                    return half4(hasWriter ? normal : NoWriterColor, 1.0);
                }

                if (mode == 3)
                {
                    // R = 1 - perceptualRoughness（越白越光滑），G = metallic，B = thickness。
                    float4 material = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferMaterialTexture, sampler_PointClamp, uv);
                    half3 value = half3(1.0 - material.r, material.g, material.b);
                    return half4(hasWriter ? value : NoWriterColor, 1.0);
                }

                if (mode == 4)
                {
                    float4 reflection = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferReflectionTexture, sampler_PointClamp, uv);
                    half3 value = half3(reflection.r, reflection.g, 0.0);
                    return half4(hasWriter ? value : NoWriterColor, 1.0);
                }

                if (mode == 5)
                {
                    // profile 是 byte ID：/8 让常见的 0..8 直接铺满 0..1（原值铺过去基本是黑的）。
                    float4 classification = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
                    half3 value = half3(saturate(classification.r * 8.0), classification.g, classification.b);
                    return half4(hasWriter ? value : NoWriterColor, 1.0);
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
                    bool aligned = hasWriter && owner == obLayer0;
                    return aligned ? half4(0.1, 0.8, 0.2, 1.0) : half4(0.8, 0.15, 0.15, 1.0);
                }

                if (mode == 7)
                {
                    // materialClass 也是 byte ID：/32 让 0..32 铺满 0..1。
                    float4 classification = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
                    half value = (half)saturate(classification.a * 32.0);
                    return half4(hasWriter ? half3(value, 0.0, 0.0) : NoWriterColor, 1.0);
                }

                return half4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
