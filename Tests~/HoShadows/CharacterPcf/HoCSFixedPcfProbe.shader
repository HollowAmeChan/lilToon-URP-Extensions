Shader "Hidden/Ho-CS/P1bFixedPcfProbe"
{
    SubShader
    {
        Pass
        {
            Name "Probe"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ HO_CS_PROBE_SOFT HO_CS_PROBE_RUNTIME_MODE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Shadows/Resources/HoCharacterShadowFixedPcf.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            float4 _WorldRect;
            float _StartDepth, _DepthSlope, _UseDerivatives, _Owner, _Radius, _ProbeSoft;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 positionXY = _WorldRect.xy + input.uv * _WorldRect.zw;
                float3 positionWS = float3(positionXY, _StartDepth + (positionXY.x - 0.5) * _DepthSlope);
                float3 positionDx = ddx(positionWS) * _UseDerivatives;
                float3 positionDy = ddy(positionWS) * _UseDerivatives;
            #if defined(HO_CS_PROBE_RUNTIME_MODE)
                bool soft = _ProbeSoft > 0.5;
            #elif defined(HO_CS_PROBE_SOFT)
                bool soft = true;
            #else
                bool soft = false;
            #endif
                HoCharacterShadowLocalSample result = HoCSFixedEvaluate(positionWS, positionDx, positionDy,
                    (uint)_Owner, soft, _Radius);
                return float4(result.localVisibility, result.blend, result.valid, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
