#ifndef LIL_HO_GTAO_COMMON_INCLUDED
#define LIL_HO_GTAO_COMMON_INCLUDED

// ---------------------------------------------------------------
// Ho-GTAO fragment 轨道核心数学（由 HTrace HRenderGTAO.compute 逐行移植）
// 约定：所有向量运算在右手化视空间（× (1,-1,-1)），march 为每像素逐点，
//       输出 0..1 可见度（1=无遮挡）。
// ---------------------------------------------------------------

float4x4 _HoGTAOViewMatrix;
float4x4 _HoGTAOProjMatrix;
float4x4 _HoGTAOInvProjMatrix;

// 视空间位置重建（与 HTrace ComputeViewSpacePosition 一致）。
// GeometryBuffer 存的是线性眼深，不是 device depth，因此先反解 device depth，
// 再用逆投影矩阵重建射线。最后的 (1,-1,-1) 与 HTrace 的右手化约定一致。
float3 HoGTAOViewPosition(float2 uv, float linearDepth)
{
    float deviceDepth = (rcp(max(linearDepth, 1.0e-5)) - _ZBufferParams.w) / max(_ZBufferParams.z, 1.0e-6);
    // This is the same conversion used by HTrace's ComputeViewSpacePosition:
    // Core.hlsl handles the platform clip-space convention, then HTrace
    // right-handedizes the result with (1,-1,-1).
    return ComputeViewSpacePosition(uv, deviceDepth, _HoGTAOInvProjMatrix)
        * float3(1.0, -1.0, -1.0);
}

// 法线世界→右手化视空间（自定矩阵）
float3 HoGTAOToViewNormal(float3 normalWS)
{
    return mul(_HoGTAOViewMatrix, float4(normalWS, 0.0)).xyz * float3(1.0, -1.0, -1.0);
}

// Interleaved Gradient Noise（HTrace HCommonAO Version A 保持帧无关）
float HoGTAOInterleavedGradientNoise(float2 pixCoord)
{
    const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

// ------------- HTrace HMath.hlsl 逐字移植 ------------
#define HO_GTAO_PI 3.141592653589793
#define HO_GTAO_PI_HALF 1.570796326794897

float HoGTAOFastSqrt(float x)
{
    return asfloat(0x1FBD1DF5 + (asint(x) >> 1));
}

float HoGTAOFastACos(float Input)
{
    float X = abs(Input);
    float Result = -0.156583 * X + HO_GTAO_PI_HALF;
    Result *= HoGTAOFastSqrt(1.0 - X);
    return Input >= 0 ? Result : HO_GTAO_PI - Result;
}

float2 HoGTAOFastACos2(float2 Input)
{
    float2 X = abs(Input);
    float2 Result = -0.156583 * X + HO_GTAO_PI_HALF;
    Result *= HoGTAOFastSqrt(1.0 - X);
    return Input >= 0 ? Result : HO_GTAO_PI - Result;
}

// HTrace HRenderGTAO.compute:68-76. The shared buffer is sampled at LOD0,
// so this remains the exact visibility-bitmask operation without the former
// ad-hoc 1/2-bin widening.
void HoGTAOUpdateBitmask(inout uint bitmask, float2 horizonSamples)
{
    uint2 horizonInt = uint2(round(horizonSamples * 32.0));
    uint horizonMin = horizonInt.x < 32u ? 0xFFFFFFFFu << horizonInt.x : 0u;
    uint horizonMax = horizonInt.y != 0u ? 0xFFFFFFFFu >> (32u - horizonInt.y) : 0u;
    bitmask |= horizonMin & horizonMax;
}

// 单个采样点：读屏幕几何（法线+线性深度）→ 重建视空间位置
// 返回 false = 该采样点无覆盖（天空/未写），跳过（视为远处无遮挡）
bool HoGTAOSampleGeometry(float2 uv, out float3 posVS)
{
    half4 normalDepth = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
    if (normalDepth.a < LIL_HO_GEOMETRY_BUFFER_DEPTH_EPSILON)
    {
        posVS = 0.0;
        return false;
    }

    posVS = HoGTAOViewPosition(uv, normalDepth.a);
    return true;
}

#endif
