Shader "Hidden/lilToon/URP/VolumeLight/SolarHD"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE

// Volume points always use the light-space atlas, including screen-space-shadow variants.
#define _SURFACE_TYPE_TRANSPARENT 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "../../Atmosphere/Shaders/HoAtmosphereMedium.hlsl"
float4 _HoVolumeLightParams; // intensity, complete working distance, steps, anisotropy
float4 _HoVolumeLightShadow; // cascades, atlas available
int _HoVolumeLightDebug;

float VolumeShadow(float3 positionWS)
{
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    if (_HoVolumeLightShadow.y < .5 || _HoVolumeLightDebug == 2) return 1;
    half cascade = _HoVolumeLightShadow.x > .5 ? ComputeCascadeIndex(positionWS) : 0;
    float4 coord = float4(mul(_MainLightWorldToShadow[cascade], float4(positionWS, 1)).xyz, 0);
    return lerp(MainLightRealtimeShadow(coord), 1, GetMainLightShadowFade(positionWS));
    #else
    return 1;
    #endif
}
float4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 uv = input.texcoord;
    #if UNITY_REVERSED_Z
    float nearDepth = 1, farDepth = 0;
    #else
    float nearDepth = UNITY_NEAR_CLIP_VALUE, farDepth = 1;
    #endif
    float3 nearWS = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
    float3 farWS = ComputeWorldSpacePosition(uv, farDepth, UNITY_MATRIX_I_VP);
    float3 origin = unity_OrthoParams.w > .5 ? nearWS : _WorldSpaceCameraPos;
    float3 direction = normalize(farWS - origin);
    float depth = SampleSceneDepth(uv);
    #if !UNITY_REVERSED_Z
    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
    #endif
    float3 surfaceWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
    float start = max(0, dot(nearWS - origin, direction));
    float end = min(_HoVolumeLightParams.y, max(0, dot(surfaceWS - origin, direction)));
    int count = (int)_HoVolumeLightParams.z;
    float stepLength = max(0, end - start) / count;
    Light sun = GetMainLight();
    float g = _HoVolumeLightParams.w;
    float phase = (1 - g * g) / (4 * PI * pow(max(1e-4, 1 + g * g - 2 * g * dot(sun.direction, direction)), 1.5));
    float3 result = 0;
    [loop] for (int step = 0; step < count; step++)
    {
        float3 p = origin + direction * (start + (step + .5) * stepLength);
        HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _HoAtmosphereMediumTime);
        if (medium.activation <= 0) continue;
        // Region only controls display participation; it never changes the light/shadow view.
        result += medium.activation * medium.density * medium.tint * VolumeShadow(p) * stepLength;
    }
    return float4(max(0, result * sun.color * phase * _HoVolumeLightParams.x), 0);
}

        ENDHLSL
        Pass
        {
            Name "SolarHD"
            ZWrite Off ZTest Always Cull Off
            Blend One One, Zero One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
        Pass
        {
            Name "Debug"
            ZWrite Off ZTest Always Cull Off
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
    }
    Fallback Off
}
