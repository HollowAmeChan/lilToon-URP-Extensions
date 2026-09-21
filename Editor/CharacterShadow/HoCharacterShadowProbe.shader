// Editor-only validation receiver. Does not add any passes to lilToon.
Shader "Hidden/Ho-CharacterShadow/ValidationProbe"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ HO_CS_PROBE_DEBUG
            #pragma instancing_options renderinglayer
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl"
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float visibility = HoCSResolveMainCast(input.positionWS, 1);
                #if defined(HO_CS_PROBE_DEBUG)
                    // r = slice valid, g = box edge (x2), b = slice depth, a = visibility.
                    // Lets a distance sweep show which stage rejects the receiver.
                    uint identity = HoRendererIdentity();
                    uint group = HoRendererIdentityGroup(identity);
                    int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
                    bool valid = slice >= 0 && slice < min(_HoCSCount, HO_CS_MAX_SLICES);
                    int safe = max(slice, 0);
                    float3 local = mul(_HoCSWorldToBounds[safe], float4(input.positionWS, 1)).xyz;
                    float edge = 0.5 - max(abs(local.x), max(abs(local.y), abs(local.z)));
                    float3 shadow = mul(_HoCSWorldToShadow[safe], float4(input.positionWS, 1)).xyz;
                    return half4(valid ? 1 : 0, saturate(edge * 2), saturate(shadow.z), visibility);
                #else
                    return half4(visibility.xxx, 1);
                #endif
            }
            ENDHLSL
        }
    }
}
