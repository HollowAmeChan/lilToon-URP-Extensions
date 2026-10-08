Shader "Hidden/lilToon/URP/SurfaceBuffer/AssociationValidation"
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
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/SurfaceBuffer/Shaders/HoSurfaceAssociation.hlsl"
            float _TestOutOfBounds;
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = _TestOutOfBounds > .5 ? float2(-.1, .5) : input.texcoord;
                float depth;
                uint status = HoSurfaceAssociationStatus(uv, depth);
                float writer = _HoSurfaceAssociationState.x > .5
                    ? SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationWriter, sampler_PointClamp, uv, 0).a : 0;
                float rawDepth = _HoSurfaceAssociationState.x > .5
                    ? SAMPLE_TEXTURE2D_X_LOD(_HoSurfaceAssociationSurfaceDepth, sampler_PointClamp, uv, 0).r : 0;
                return float4(status, depth, writer, rawDepth);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
