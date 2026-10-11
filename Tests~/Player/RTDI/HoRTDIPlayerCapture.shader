Shader "Hidden/HoRTDIPlayerCapture"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _HoRTDIDiffuseTexture, _HoRTDIRawDiffuseTexture, _HoRTDISpecularTexture, _HoRTDIRawSpecularTexture, _MotionVectorTexture;
        float4 Filtered(v2f_img i):SV_Target {return tex2D(_HoRTDIDiffuseTexture,i.uv);}
        float4 Raw(v2f_img i):SV_Target {return tex2D(_HoRTDIRawDiffuseTexture,i.uv);}
        float4 Specular(v2f_img i):SV_Target {return tex2D(_HoRTDISpecularTexture,i.uv);}
        float4 RawSpecular(v2f_img i):SV_Target {return tex2D(_HoRTDIRawSpecularTexture,i.uv);}
        float4 Motion(v2f_img i):SV_Target {return float4(tex2D(_MotionVectorTexture,i.uv).xy,0,1);}
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Filtered
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Raw
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Specular
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment RawSpecular
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Motion
            ENDHLSL
        }
    }
}
