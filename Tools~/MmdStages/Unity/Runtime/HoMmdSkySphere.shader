Shader "Ho/MMD Stage Sky Sphere"
{
    Properties
    {
        _Top("天顶",Color)=(.02,.03,.08,1)
        _Horizon("地平线",Color)=(.1,.12,.2,1)
        [HDR] _SunColor("太阳或月亮",Color)=(1,1,1,1)
        _SunDirection("太阳方向",Vector)=(0,1,0,0)
        _SunSize("圆盘半径",Float)=.01
        _Stars("星空",Range(0,1))=1
    }
    SubShader
    {
        Tags{"Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline"}
        Cull Front ZWrite Off ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes{float4 positionOS:POSITION;};
            struct Varyings{float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
            CBUFFER_START(UnityPerMaterial)
            float4 _Top,_Horizon,_SunColor,_SunDirection;
            float _Stars,_SunSize;
            CBUFFER_END
            Varyings Vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.direction=v.positionOS.xyz;return o;}
            float Hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
            half4 Frag(Varyings i):SV_Target
            {
                float3 d=normalize(i.direction);
                float3 color=lerp(_Horizon.rgb,_Top.rgb,smoothstep(-.12,.8,d.y));
                float2 uv=float2(atan2(d.x,d.z)*.15915494+.5,asin(d.y)*.31830989+.5);
                float2 cell=uv*float2(1800,900);
                float h=Hash(float3(floor(cell),17));
                float distanceToStar=length(frac(cell)-.5);
                float aa=max(length(fwidth(cell)),.1);
                float star=(1-smoothstep(.05,.05+aa*.5,distanceToStar))*step(.998,h);
                color+=star*_Stars*smoothstep(-.03,.2,d.y)*.9;
                float angle=acos(clamp(dot(d,normalize(_SunDirection.xyz)),-1,1));
                float disk=1-smoothstep(_SunSize,_SunSize+max(fwidth(angle),.0003),angle);
                color+=_SunColor.rgb*(disk+exp(-angle*24)*.04);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
