Shader "Hidden/lilToon/URP/Validation/InvisibleSurface"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_HoGeometryBufferNormalDepthTexture);TEXTURE2D(_HoACGeometryNormalDepthTexture);
            TEXTURE2D(_HoSurfaceBufferColorTexture);TEXTURE2D(_HoObjectBufferId0Texture);
            TEXTURE2D(_HoSurfaceBufferOwnerTexture);TEXTURE2D(_HoSurfaceSemanticOwnerTexture);
            TEXTURE2D(_HoSurfaceSemanticLane0Texture);
            TEXTURE2D(_HoGeometryBufferOutlineNormalDepthTexture);TEXTURE2D(_HoACOutlineNormalDepthTexture);
            float _HoSurfaceBufferActive,_HoACInputsPublished;
            struct Output {float4 depth:SV_Target0;float4 color:SV_Target1;float4 identity:SV_Target2;float4 owner:SV_Target3;float4 semantic:SV_Target4;float4 outline:SV_Target5;};
            Output Frag(Varyings input)
            {
                float2 uv=input.texcoord;Output o;
                o.depth=float4(SAMPLE_TEXTURE2D(_HoGeometryBufferNormalDepthTexture,sampler_PointClamp,uv).a,SAMPLE_TEXTURE2D(_HoACGeometryNormalDepthTexture,sampler_PointClamp,uv).a,_HoSurfaceBufferActive,_HoACInputsPublished);
                o.color=SAMPLE_TEXTURE2D(_HoSurfaceBufferColorTexture,sampler_PointClamp,uv);
                o.identity=SAMPLE_TEXTURE2D(_HoObjectBufferId0Texture,sampler_PointClamp,uv);
                o.owner=float4(SAMPLE_TEXTURE2D(_HoSurfaceBufferOwnerTexture,sampler_PointClamp,uv).rg,SAMPLE_TEXTURE2D(_HoSurfaceSemanticOwnerTexture,sampler_PointClamp,uv).rg);
                o.semantic=SAMPLE_TEXTURE2D(_HoSurfaceSemanticLane0Texture,sampler_PointClamp,uv);
                o.outline=float4(SAMPLE_TEXTURE2D(_HoGeometryBufferOutlineNormalDepthTexture,sampler_PointClamp,uv).a,SAMPLE_TEXTURE2D(_HoACOutlineNormalDepthTexture,sampler_PointClamp,uv).a,0,1);
                return o;
            }
            ENDHLSL
        }
    }
}
