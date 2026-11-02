Shader "Hidden/HoRP/Tests/ShadowModeProbe"
{
    Properties { _HoRPShadowMode ("Mode", Float) = 1 }
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ShadowCast/Shaders/HoShadowCastSampling.hlsl"
            float _HoRPShadowMode;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                return float4(HoShadowCastAttenuation(float3(0, 0, 0), _HoRPShadowMode).xxx, 1);
            }
            ENDHLSL
        }
    }
}
