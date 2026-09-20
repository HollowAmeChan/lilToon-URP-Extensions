Shader "Hidden/lilToon/URP/ObjectBuffer/Fallback"
{
    // 非 lilToon 材质（以及临时验证）用的 ID pass：身份只从 RSUV 来。
    // 注意：拿不到 RSUV 的 renderer 类型会写成 0（= 背景），组件会在编辑器里就此告警。
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite On
        ZTest Less
        Cull Off

        // Pass 0：非 MSAA（N = 1）。直接写 4 层结果——一层一票，覆盖率是 1 或 0。
        // 选择层在这里恒为 0：跳过 ID pass 的材质本来也不参与选择写入。
        Pass
        {
            Name "ObjectBuffer IdLayers"
            Tags { "LightMode" = "HoObjectBuffer" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct LayerOutput
            {
                float4 id0 : SV_Target0;
                float4 id1 : SV_Target1;
                float4 coverage : SV_Target2;
                float4 selection0 : SV_Target3;
                float4 selection1 : SV_Target4;
            };

            uint HoObjectBufferPartIdFromRsuv()
            {
                // RSUV 只当索引用：低 16 bit = 角色 8 + 槽位 8（决策 13）。
                return (uint)unity_RendererUserValue & 0xFFFFu;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            LayerOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                uint partId = HoObjectBufferPartIdFromRsuv();
                float coverage = partId != 0u ? 1.0 : 0.0;

                LayerOutput output;
                output.id0 = HoObjectBufferPackIdRow(partId, 0u);
                output.id1 = float4(0.0, 0.0, 0.0, 0.0);
                output.coverage = float4(coverage, 0.0, 0.0, 0.0);
                output.selection0 = float4(0.0, 0.0, 0.0, 0.0);
                output.selection1 = float4(0.0, 0.0, 0.0, 0.0);
                return output;
            }
            ENDHLSL
        }

        // Pass 1：MSAA，整数 ID 目标（R16_UInt）。逐样本只写一个 16 bit ID。
        // 注意：这一版把整数目标（target0）和 UNorm 选择目标（target1/2）放在同一个 MRT 里。
        // 若某个平台/后端对"混合整型与非整型 RT"有意见，症状会是这个 pass 不产出——第一个该查的地方就是这里。
        Pass
        {
            Name "ObjectBuffer IdMsaaInt"
            Tags { "LightMode" = "HoObjectBuffer" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct MsaaOutput
            {
                uint sampleId : SV_Target0;
                float4 selection0 : SV_Target1;
                float4 selection1 : SV_Target2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            MsaaOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                MsaaOutput output;
                output.sampleId = (uint)unity_RendererUserValue & 0xFFFFu;
                output.selection0 = float4(0.0, 0.0, 0.0, 0.0);
                output.selection1 = float4(0.0, 0.0, 0.0, 0.0);
                return output;
            }
            ENDHLSL
        }

        // Pass 2：MSAA，但 ID 目标退化成 R16_UNorm（平台不支持整数 MSAA 目标时）。
        // 消费端用 round(v * 65535) 还原整数——与 GeometryBuffer 用 UNORM8 存身份同法。
        Pass
        {
            Name "ObjectBuffer IdMsaaUnorm"
            Tags { "LightMode" = "HoObjectBuffer" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct MsaaOutput
            {
                float sampleId : SV_Target0;
                float4 selection0 : SV_Target1;
                float4 selection1 : SV_Target2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            MsaaOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                MsaaOutput output;
                output.sampleId = (float)((uint)unity_RendererUserValue & 0xFFFFu) / 65535.0;
                output.selection0 = float4(0.0, 0.0, 0.0, 0.0);
                output.selection1 = float4(0.0, 0.0, 0.0, 0.0);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
