Shader "Hidden/lilToon/URP/Tests/OutlineStencilCopyProbe"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct V { float4 positionCS : SV_POSITION; };
        V Vert(uint id : SV_VertexID) { V o; o.positionCS = GetFullScreenTriangleVertexPosition(id); return o; }
        half4 Frag(V input) : SV_Target { return half4(1,0,0,1); }
        ENDHLSL
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Stencil { Ref 63 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Stencil { Ref 63 Comp Equal Pass Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
