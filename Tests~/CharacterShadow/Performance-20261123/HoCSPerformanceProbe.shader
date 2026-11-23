Shader "Hidden/Ho-CS/PerformanceProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HO_BENCH_BASELINE HO_BENCH_POINT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V Vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            float4 Frag(V i) : SV_Target
            {
                float depth = i.uv.x < 0.5 + 0.1 * sin(i.uv.y * 80) ? 0.7 : 0.3;
                return depth.xxxx;
            }
            ENDHLSL
        }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HO_BENCH_BASELINE HO_BENCH_POINT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl"
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            float _BenchmarkMode;
            V Vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            float4 Frag(V i) : SV_Target
            {
                #if defined(HO_BENCH_BASELINE)
                return float4(0.5,0.5,0.5,1);
                #else
                float2 uv = 0.05 + i.uv * 0.9;
                #if defined(HO_BENCH_POINT)
                return HoCSSampleRawDepth(uv).xxxx;
                #else
                float2 lo = 0.5 * _HoCSAtlasSize.xy, hi = 1 - lo;
                float texelWorld = 2.0 / _HoCSAtlasSize.z;
                float2 dx = ddx(uv) * _HoCSAtlasSize.zw, dy = ddy(uv) * _HoCSAtlasSize.zw;
                float visibility = HoCSSampleAtlasPcss(uv,lo,hi,0.5,texelWorld,2,float3(0,0,0.001),0.5*max(length(dx),length(dy)));
                return float4(visibility.xxx,1);
                #endif
                #endif
            }
            ENDHLSL
        }
    }
}
