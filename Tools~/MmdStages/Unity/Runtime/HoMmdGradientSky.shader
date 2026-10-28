Shader "Ho/MMD Stage Gradient Sky"
{
    Properties
    {
        _Top("天空上部", Color)=(.015,.02,.06,1)
        _Bottom("天空下部", Color)=(.07,.08,.16,1)
        _Exposure("亮度", Float)=1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Top,_Bottom;
            float _Exposure;
            struct v2f { float4 position:SV_POSITION; float3 ray:TEXCOORD0; };
            v2f vert(float4 vertex:POSITION) {v2f o;o.position=UnityObjectToClipPos(vertex);o.ray=vertex.xyz;return o;}
            float4 frag(v2f i):SV_Target
            {
                float t=smoothstep(-.5,.75,normalize(i.ray).y);
                return float4(lerp(_Bottom.rgb,_Top.rgb,t)*_Exposure,1);
            }
            ENDHLSL
        }
    }
}
