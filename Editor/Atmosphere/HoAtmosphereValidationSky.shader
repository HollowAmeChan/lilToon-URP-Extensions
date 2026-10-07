Shader "Hidden/lilToon/URP/Atmosphere/ValidationSky"
{
    Properties { _TestGain ("Gain", Float) = 8 }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Pass
        {
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex SkyVert
            #pragma fragment SkyFrag
            #include "UnityCG.cginc"
            float _TestGain;
            struct v2f { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            v2f SkyVert(float4 vertex : POSITION)
            {
                v2f o; o.positionCS = UnityObjectToClipPos(vertex); o.direction = vertex.xyz; return o;
            }
            float4 SkyFrag(v2f i) : SV_Target { return float4((normalize(i.direction) * 0.5 + 0.5) * _TestGain, 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
