Shader "Hidden/lilToon/URP/Atmosphere/Validation"
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
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/Atmosphere/Shaders/HoAtmosphereSampling.hlsl"
            static const float3 TestDirections[16] = {
                float3(1,0,0), float3(-1,0,0), float3(0,1,0), float3(0,-1,0), float3(0,0,1), float3(0,0,-1),
                float3(1,.3,.8), float3(-1,.6,-.7), float3(.3,1,-.8), float3(-.4,-1,.7), float3(.8,-.6,1), float3(-.7,-.4,-1),
                float3(1,1,1), float3(-1,-1,-1), float3(0,0,0), float3(0,0,1) };
            float4 Frag(Varyings input) : SV_Target
            {
                int index = min((int)input.positionCS.x, 15);
                return float4(HoAtmosphereSampleRadiance(TestDirections[index]), HoAtmosphereAvailable() ? 1 : 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
