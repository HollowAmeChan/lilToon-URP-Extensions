Shader "Hidden/HoCapsuleAO/Fixture"
{
    SubShader
    {
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4x4 _FixtureVP, _FixtureView;
        TEXTURE2D(_HoAOTexture);
        struct V { float4 positionCS:SV_Position; float eye:TEXCOORD0; };
        V Vert(float3 p:POSITION)
        {
            V o; float3 w=TransformObjectToWorld(p);
            o.positionCS=mul(_FixtureVP,float4(w,1));
            o.eye=-mul(_FixtureView,float4(w,1)).z; return o;
        }
        float4 Depth(V i):SV_Target { return i.positionCS.z; }
        float4 Normal(V i):SV_Target { return float4(.5,1,.5,i.eye); }
        V Full(uint id:SV_VertexID) { V o; o.positionCS=GetFullScreenTriangleVertexPosition(id);o.eye=0;return o; }
        float4 Read(V i):SV_Target { return LOAD_TEXTURE2D(_HoAOTexture,int2(i.positionCS.xy)).rrrr; }
        ENDHLSL
        Pass { Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            ENDHLSL
        }
        Pass { Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Normal
            ENDHLSL
        }
        Pass { Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Full
            #pragma fragment Read
            ENDHLSL
        }
    }
}
