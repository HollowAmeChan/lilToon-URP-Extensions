Shader "Hidden/HoShadows/CharacterResolveReadback"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_HoCharacterResolveProbeTexture);
            float _HoCharacterResolveProbeValid;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                if (_HoCharacterResolveProbeValid < 0.5)
                    return float4(1, 0, 0, 0);
                return LOAD_TEXTURE2D_X(_HoCharacterResolveProbeTexture, (uint2)positionCS.xy);
            }
            ENDHLSL
        }
    }
}
