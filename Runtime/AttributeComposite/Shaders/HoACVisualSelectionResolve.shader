Shader "Hidden/lilToon/URP/AttributeComposite/VisualSelectionResolve"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"
            #include "HoACOutlineInheritance.hlsl"
            struct HoACLaneData { uint semanticId; uint objectTagBit; uint sourceMode; uint reserved; };
            StructuredBuffer<HoACLaneData> _HoACLanes;
            float _HoACOutlineSurfaceAvailable;
            TEXTURE2D_X(_HoACRawSelection0Texture);
            TEXTURE2D_X(_HoACRawSelection1Texture);
            TEXTURE2D_X(_HoACRawSelection2Texture);
            TEXTURE2D_X(_HoACRawSelection3Texture);
            struct Output { half4 a : SV_Target0; half4 b : SV_Target1; half4 c : SV_Target2; half4 d : SV_Target3; };
            half4 Inherit(float4 raw, uint lane, float4 shell, uint tags)
            {
                float a = HoAC_ApplyOutlineSemantic(raw.g,_HoACLanes[lane].sourceMode,_HoACLanes[lane].objectTagBit,shell,tags,_HoACOutlineSurfaceAvailable > .5);
                float b = HoAC_ApplyOutlineSemantic(raw.a,_HoACLanes[lane+1u].sourceMode,_HoACLanes[lane+1u].objectTagBit,shell,tags,_HoACOutlineSurfaceAvailable > .5);
                return half4(raw.r,a,raw.b,b);
            }
            Output Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord; float4 shell = HoAC_ReadOutlineOwner(uv);
                uint owner = HoAC_OutlineOriginalOwner(shell); uint tags = owner != 0u ? HoObjectBufferLoadPart(owner).tags : 0u;
                Output output;
                output.a = Inherit(SAMPLE_TEXTURE2D_X(_HoACRawSelection0Texture,sampler_PointClamp,uv),0u,shell,tags);
                output.b = Inherit(SAMPLE_TEXTURE2D_X(_HoACRawSelection1Texture,sampler_PointClamp,uv),2u,shell,tags);
                output.c = Inherit(SAMPLE_TEXTURE2D_X(_HoACRawSelection2Texture,sampler_PointClamp,uv),4u,shell,tags);
                output.d = Inherit(SAMPLE_TEXTURE2D_X(_HoACRawSelection3Texture,sampler_PointClamp,uv),6u,shell,tags);
                return output;
            }
            ENDHLSL
        }
    }
}
