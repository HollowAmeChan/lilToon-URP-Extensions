Shader "Hidden/Ho-CS/OptimizedPerformanceProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HO_BENCH_BASELINE HO_BENCH_POINT
            #pragma multi_compile_local _ HO_CS_REFERENCE_IMPLEMENTATION
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HO_BENCH_BASELINE HO_BENCH_POINT
            #pragma multi_compile_local _ HO_CS_REFERENCE_IMPLEMENTATION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(HO_CS_REFERENCE_IMPLEMENTATION)
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSamplingBaseline.hlsl"
            #else
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl"
            #endif
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            float _BenchmarkMode;
            float2 _ReceiverGradient;
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
                // The flat case preserves the original fixture. The sloped case uses
                // an exactly linear receiver, with its matching atlas-UV depth gradient.
                // Cross the atlas's 0.3 depth in the sloped case so plane-corrected
                // comparisons can differ from raw comparisons near that receiver contour.
                float receiverBaseDepth = any(_ReceiverGradient != 0) ? 0.3 : 0.5;
                float receiverDepth = receiverBaseDepth + dot(_ReceiverGradient, uv - 0.5);
                float visibility = HoCSSampleAtlasPcss(uv,lo,hi,receiverDepth,texelWorld,2,
                    float3(_ReceiverGradient,0.001),0.5*max(length(dx),length(dy)));
                return float4(visibility.xxx,1);
                #endif
                #endif
            }
            ENDHLSL
        }
    }
}
