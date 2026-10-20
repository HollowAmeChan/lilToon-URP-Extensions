Shader "Hidden/lilToon/URP/VolumeLight/Validation"
{
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Atmosphere/Shaders/HoAtmosphereMedium.hlsl"
            float4 _ProbePoints[16];
            float _ProbeTime, _ProbeMode, _ProbeCascades;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(uint id : SV_VertexID) { Varyings o; o.positionCS = GetFullScreenTriangleVertexPosition(id); return o; }
            float4 Frag(Varyings input) : SV_Target
            {
                float3 p = _ProbePoints[min(15, (int)input.positionCS.x)].xyz;
                HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _ProbeTime);
                if (_ProbeMode < .5) return float4(medium.activation, medium.density, medium.tint.r, _HoAtmosphereMediumDensity.w);
                if (_ProbeMode > 1.5 && _ProbeMode < 2.5) return float4(_HoAtmosphereMediumAlbedo.rgb, _HoAtmosphereMediumOptics.x);
                if (_ProbeMode > 2.5) return HoAtmospherePhase(p.x, 2).xxxx;
                half shadow = 1;
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                int cascade = _ProbeCascades > .5 ? (int)ComputeCascadeIndex(p) : 0;
                float4 shadowCoord = float4(mul(_MainLightWorldToShadow[cascade], float4(p, 1)).xyz, 0);
                // Independent surface API reference includes the standard shadow-distance fade.
                shadow = MainLightShadow(shadowCoord, p, half4(1, 1, 1, 1), half4(0, 0, 0, 0));
                #endif
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                return float4(shadow, medium.activation, medium.density, 2);
                #else
                return float4(shadow, medium.activation, medium.density, 1);
                #endif
            }
            ENDHLSL
        }
    }
}
