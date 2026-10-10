Shader "Hidden/lilToon/URP/Atmosphere/Debug"
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
            #include "HoAtmosphereSampling.hlsl"
            #include "HoAtmosphereMedium.hlsl"
            float _HoAtmosphereDebugMode;
            float4 Frag(Varyings input) : SV_Target
            {
                // Equirectangular preview: centre +Z, right +X, top +Y.
                float longitude = (input.texcoord.x - 0.5) * TWO_PI;
                float latitude = (input.texcoord.y - 0.5) * PI;
                float3 direction = float3(sin(longitude) * cos(latitude), sin(latitude), cos(longitude) * cos(latitude));
                int mode = (int)_HoAtmosphereDebugMode;
                if (mode == 4 || mode == 5)
                {
                    float3 p = float3((input.texcoord.xy - .5) * 2 * _HoAtmosphereMediumHalfSize.xy, 0);
                    float3 worldPosition = mul(_HoAtmosphereRegionToWorld, float4(p, 1)).xyz;
                    HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(worldPosition, _HoAtmosphereMediumTime);
                    float value = medium.activation * (mode == 4 ? medium.density : 1);
                    return float4(value.xxx, 1);
                }
                if (mode == 2) return float4(direction * 0.5 + 0.5, 1);
                if (mode == 3) return HoAtmosphereAvailable() ? float4(0, 1, 0, 1) : float4(1, 0, 1, 1);
                return float4(HoAtmosphereSampleRadiance(direction), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
