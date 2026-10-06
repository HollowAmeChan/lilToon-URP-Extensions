#ifndef LIL_SCREEN_PROCESS_DEPTH_INPUTS_INCLUDED
#define LIL_SCREEN_PROCESS_DEPTH_INPUTS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"

TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture);
float _HoGeometryBufferValid;
// SP publishes these for each layer, together with its explicit texture read dependencies.
float _lilHoSPCameraDepthValid;
float _lilHoSPCameraNormalsValid;
float _lilHoSPOutlineDepthValid;

bool LilScreenProcessHasVisualDepth()
{
    return _HoGeometryBufferValid > 0.5 || _lilHoSPCameraDepthValid > 0.5;
}

float LilScreenProcessEyeDepth(float2 uv)
{
    if (_HoGeometryBufferValid > 0.5)
    {
        half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
        return LilHoGeometryBufferLinearDepthOrFar(normalDepth, _ProjectionParams.z);
    }

    if (_lilHoSPCameraDepthValid > 0.5)
    {
        float depth = SampleSceneDepth(uv);
        return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(depth) : LinearEyeDepth(depth, _ZBufferParams);
    }
    return _ProjectionParams.z;
}

// A decorative shell has visual depth, independently of physical geometry and object identity.
// Any nonzero outline coverage must keep that depth, including 1/4 and 1/2 MSAA edges.
// Never interpolate shell/background depths, and never turn missing outline data into a stale read.
float LilScreenProcessVisualEyeDepth(float2 uv)
{
    if (_HoGeometryBufferValid > 0.5 && _lilHoSPOutlineDepthValid > 0.5)
    {
        half4 outline = SAMPLE_TEXTURE2D_X(_HoGeometryBufferOutlineNormalDepthTexture, sampler_PointClamp, uv);
        if (LilHoGeometryBufferOutlineCoverageAt(uv, outline) > 0.0001 &&
            LilHoGeometryBufferCoverage(outline) > 0.5)
            return outline.a;
    }
    return LilScreenProcessEyeDepth(uv);
}

#endif
