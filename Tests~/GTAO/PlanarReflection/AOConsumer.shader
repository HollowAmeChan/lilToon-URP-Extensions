Shader "Hidden/Ho-Validation/PLRAOConsumer"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_HoAOTexture); SAMPLER(sampler_HoAOTexture);
            float _HoAOAvailable;
            struct A { float3 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; };
            V Vert(A a) { V v; v.positionCS=TransformObjectToHClip(a.positionOS); return v; }
            half4 Frag(V v):SV_Target
            {
                float visibility=_HoAOAvailable>.5?SAMPLE_TEXTURE2D(_HoAOTexture,sampler_HoAOTexture,GetNormalizedScreenSpaceUV(v.positionCS)).r:1;
                return half4(visibility.xxx,1);
            }
            ENDHLSL
        }
    }
}
