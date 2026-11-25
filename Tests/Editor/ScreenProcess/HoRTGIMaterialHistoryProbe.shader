Shader "Hidden/HoRP/Tests/MaterialHistoryProbe"
{
    Properties
    {
        _UseEmission ("Emission", Float) = 0
        _EmissionBlink ("Blink", Vector) = (0,0,0,0)
        _EmissionMap_ScrollRotate ("Emission Scroll", Vector) = (0,0,0,0)
        _EmissionBlendMask_ScrollRotate ("Mask Scroll", Vector) = (0,0,0,0)
        _EmissionUseGrad ("Gradation", Float) = 0
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            struct Attributes { float4 vertex : POSITION; };
            float4 Vertex(Attributes input) : SV_POSITION { return input.vertex; }
            float4 Fragment() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
