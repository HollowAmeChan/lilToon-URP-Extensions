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
            // octa / owner 的编解码与生产者（lilToon 的 SB 材质 pass）共用同一份实现。
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SurfaceBuffer/Shaders/HoSurfaceBufferCommon.hlsl"

            TEXTURE2D_X(_HoSurfaceBufferColorTexture);
            TEXTURE2D_X(_HoSurfaceBufferNormalTexture);
            TEXTURE2D_X(_HoSurfaceBufferMaterialTexture);
            TEXTURE2D_X(_HoSurfaceBufferReflectionTexture);
            TEXTURE2D_X(_HoSurfaceBufferClassificationTexture);
            TEXTURE2D_X(_HoSurfaceBufferOwnerTexture);
            // 语义 lane（单采样，逐像素）：owner 用于对齐诊断，4 张 lane 图各装两条 `(SemanticId, value)`。
            TEXTURE2D_X(_HoSurfaceSemanticOwnerTexture);
            TEXTURE2D_X(_HoSurfaceSemanticLane0Texture);
            TEXTURE2D_X(_HoSurfaceSemanticLane1Texture);
            TEXTURE2D_X(_HoSurfaceSemanticLane2Texture);
            TEXTURE2D_X(_HoSurfaceSemanticLane3Texture);
            TEXTURE2D_X(_HoObjectBufferId0Texture);
            float _HoSurfaceBufferDebugMode;
            float _HoSurfaceBufferActive;
            float _HoSurfaceSemanticActive;
            float _HoObjectBufferActive;

            // 数值视图一律**直出原值**（不按 owner 上底纹）：否则"没画上"和"画上了但 owner 是 0"
            // 会显示成同一个颜色，而这正是排查时最需要分开的两件事。
            // 想确认"到底有没有写"，看 Owner 视图：绿 = 写了且与 OB 层 0 一致 / 橙 = 写了但对不上 /
            // 红 = 没人写（这一格没有 SB 值）/ 洋红 = OB 没产出。
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                uint mode = (uint)round(_HoSurfaceBufferDebugMode);

                if (_HoSurfaceBufferActive <= 0.5)
                {
                    // 整条链没跑（feature 不在 renderer / 被关掉）：亮红。与下面的"没人写"分开。
                    return half4(0.35, 0.0, 0.0, 1.0);
                }

                if (mode == 1)
                {
                    return half4(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferColorTexture, sampler_PointClamp, uv).rgb, 1.0);
                }

                if (mode == 2)
                {
                    float2 octa = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferNormalTexture, sampler_PointClamp, uv).rg;
                    return half4((half3)(HoSurfaceOctDecode(octa) * 0.5 + 0.5), 1.0);
                }

                if (mode == 3)
                {
                    // R = 1 - perceptualRoughness（越白越光滑），G = metallic，B = thickness。
                    float4 material = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferMaterialTexture, sampler_PointClamp, uv);
                    return half4(1.0 - material.r, material.g, material.b, 1.0);
                }

                if (mode == 4)
                {
                    float4 reflection = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferReflectionTexture, sampler_PointClamp, uv);
                    return half4(reflection.r, reflection.g, 0.0, 1.0);
                }

                if (mode == 5)
                {
                    // profile 是 byte ID：/8 让常见的 0..8 铺满 0..1（原值铺过去基本是黑的）。
                    // 三通道全 0 是**合法结果**：profile 要开 SSS，curvature / transmittance 默认 0。
                    float4 classification = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
                    return half4(saturate(classification.r * 8.0), classification.g, classification.b, 1.0);
                }

                if (mode == 6)
                {
                    if (_HoObjectBufferActive <= 0.5)
                    {
                        // OB 没产出：先解决身份（刷新全场景 RSUV），SB 的 owner 永远对不上。
                        return half4(1.0, 0.0, 1.0, 1.0);
                    }

                    float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                    uint obLayer0 = ((uint)round(saturate(id0.r) * 255.0) << 8) | (uint)round(saturate(id0.g) * 255.0);
                    uint owner = HoSurfaceOwnerDecode(SAMPLE_TEXTURE2D_X(_HoSurfaceBufferOwnerTexture, sampler_PointClamp, uv).rg);
                    if (owner == 0u)
                    {
                        // 没人写：SB 这一趟没画到这个像素（队列被过滤 / draw 被丢 / 材质没这个 pass）。
                        return half4(0.8, 0.15, 0.15, 1.0);
                    }

                    return owner == obLayer0 ? half4(0.1, 0.8, 0.2, 1.0) : half4(0.95, 0.6, 0.1, 1.0);
                }

                if (mode == 7)
                {
                    // materialClass 也是 byte ID：/32 让 0..32 铺满 0..1。
                    float4 classification = SAMPLE_TEXTURE2D_X(_HoSurfaceBufferClassificationTexture, sampler_PointClamp, uv);
                    half value = (half)saturate(classification.a * 32.0);
                    return half4(value, 0.0, 0.0, 1.0);
                }

                if (mode >= 8 && _HoSurfaceSemanticActive <= 0.5)
                {
                    // 语义 lane 这趟没跑（feature 关掉 / MRT 不够）：暗红 = 这一批视图没有内容，与"值是 0"分开。
                    return half4(0.35, 0.0, 0.0, 1.0);
                }

                if (mode == 8)
                {
                    // 语义 lane 的 owner（与数值面同一个身份）：对齐诊断用。
                    if (_HoObjectBufferActive <= 0.5)
                    {
                        return half4(1.0, 0.0, 1.0, 1.0);
                    }

                    float4 id0 = SAMPLE_TEXTURE2D_X(_HoObjectBufferId0Texture, sampler_PointClamp, uv);
                    uint obLayer0 = (((uint)round(saturate(id0.r) * 255.0)) << 8) | (uint)round(saturate(id0.g) * 255.0);
                    uint owner = HoSurfaceOwnerDecode(SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticOwnerTexture, sampler_PointClamp, uv).rg);
                    if (owner == 0u)
                    {
                        return half4(0.8, 0.15, 0.15, 1.0);
                    }

                    return owner == obLayer0 ? half4(0.1, 0.8, 0.2, 1.0) : half4(0.95, 0.6, 0.1, 1.0);
                }

                if (mode == 9)
                {
                    // 8 条语义 lane 铺成 4×2 网格：每格一个 lane，通道 = (SemanticId ÷ 255, value, 写了没有)。
                    // 未写画暗红（与其它视图同一约定：暗红 = 这一格没内容）。
                    uint col = (uint)min(3.0, floor(uv.x * 4.0));
                    uint row = (uint)min(1.0, floor(uv.y * 2.0));
                    uint lane = row * 4u + col;
                    float4 packed;
                    if (lane < 2u)
                    {
                        packed = SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane0Texture, sampler_PointClamp, uv);
                    }
                    else if (lane < 4u)
                    {
                        packed = SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane1Texture, sampler_PointClamp, uv);
                    }
                    else if (lane < 6u)
                    {
                        packed = SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane2Texture, sampler_PointClamp, uv);
                    }
                    else
                    {
                        packed = SAMPLE_TEXTURE2D_X(_HoSurfaceSemanticLane3Texture, sampler_PointClamp, uv);
                    }

                    bool even = (lane & 1u) == 0u;
                    uint laneId = (uint)round(saturate(even ? packed.r : packed.b) * 255.0);
                    float laneValue = saturate(even ? packed.g : packed.a);
                    if (laneId == 0u)
                    {
                        return half4(0.35, 0.0, 0.0, 1.0);
                    }

                    return half4((half)((float)laneId / 255.0), (half)laneValue, 1.0h, 1.0h);
                }

                return half4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
