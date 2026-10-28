Shader "Hidden/lilToon/URP/RTShadow/Debug"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_HoRTShadowDebugRaw);
            TEXTURE2D(_HoRTShadowDebugVisibility);
            float4 _HoRTShadowDebugParams; // available, blocker display distance
            float _HoRTShadowDebugMode;
            float3 StatusColor(uint status)
            {
                if(status==0)return float3(.04,.04,.04);
                if(status==1)return float3(0,1,0);
                if(status==2)return float3(1,0,0);
                if(status==3)return float3(0,.4,1);
                if(status==4)return float3(1,.5,0);
                return float3(1,0,1);
            }
            float4 Frag(Varyings input):SV_Target
            {
                if(_HoRTShadowDebugParams.x<.5)return float4(1,0,1,1);
                float4 raw=SAMPLE_TEXTURE2D(_HoRTShadowDebugRaw,sampler_PointClamp,input.texcoord);
                float4 visibility=SAMPLE_TEXTURE2D(_HoRTShadowDebugVisibility,sampler_PointClamp,input.texcoord);
                uint status=(uint)round(raw.b),mode=(uint)round(_HoRTShadowDebugMode);
                if(mode==6)return float4(StatusColor(status),1);
                if(mode==5)
                {
                    uint id=(uint)round(raw.a);
                    float3 color=frac(float3(.618033989,.381966011,.754877666)*id);
                    return float4(id>0?(.2+.8*color):float3(0,0,0),1);
                }
                if(status!=1)return float4(StatusColor(status)*.4,1);
                if(mode==4)return raw.g>=65504?float4(0,.3,1,1):float4(saturate(raw.g/_HoRTShadowDebugParams.y).xxx,1);
                if(mode==3)return float4(abs(visibility.r-raw.r).xxx,1);
                if(mode==2)return float4(raw.rrr,1);
                return float4(visibility.rrr,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
