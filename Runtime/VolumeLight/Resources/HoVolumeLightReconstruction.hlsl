#ifndef HO_VOLUME_LIGHT_RECONSTRUCTION
#define HO_VOLUME_LIGHT_RECONSTRUCTION
TEXTURE2D(_HoVolumeLightCurrent);
TEXTURE2D(_HoVolumeLightPrevious);
TEXTURE2D(_HoVolumeLightPreviousDepth);
float4x4 _HoVolumeLightPreviousVP, _HoVolumeLightPreviousInverseVP;
float4 _HoVolumeLightPreviousPosition; // xyz, orthographic
float4 _HoVolumeLightHistory; // valid, maximum weight, width, height

float VolumeEyeDepth(float raw)
{
    return unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
}
struct VolumeResolved { float4 color : SV_Target0; float depth : SV_Target1; };
VolumeResolved VolumeResolve(Varyings input)
{
    float2 uv = input.texcoord, size = _HoVolumeLightSampling.zw;
    int2 pixel = clamp((int2)input.positionCS.xy, 0, (int2)size - 1);
    float4 center = LOAD_TEXTURE2D(_HoVolumeLightCurrent, pixel);
    VolumeResolved output;
    output.depth = SampleSceneDepth(uv); output.color = 0;
    // No filtering across a pixel with an empty visible medium interval.
    if (center.a <= 0) return output;
    float3 sum = 0, mean = 0, square = 0, lo = center.rgb, hi = center.rgb;
    float total = 0, n = 0;
    float centerEye = VolumeEyeDepth(output.depth);
    [unroll] for (int y = -1; y <= 1; ++y)
    [unroll] for (int x = -1; x <= 1; ++x)
    {
        int2 p = clamp(pixel + int2(x, y), 0, (int2)size - 1);
        float4 value = LOAD_TEXTURE2D(_HoVolumeLightCurrent, p);
        if (value.a <= 0) continue;
        float eye = VolumeEyeDepth(SampleSceneDepth((p + .5) / size));
        float w = exp2(-abs(centerEye - eye) / max(.02, .01 * centerEye)) * ((x == 0 ? 2 : 1) * (y == 0 ? 2 : 1));
        sum += value.rgb * w; total += w;
        if (w > .1) { mean += value.rgb; square += value.rgb * value.rgb; n += 1; lo = min(lo, value.rgb); hi = max(hi, value.rgb); }
    }
    float3 current = sum / max(total, 1e-6);
    float age = 1;
    if (_HoVolumeLightHistory.x > .5)
    {
        VolumeRay ray = VolumeRayAt(uv, output.depth, UNITY_MATRIX_I_VP, _WorldSpaceCameraPos, unity_OrthoParams.w > .5);
        if (VolumeInterval(ray))
        {
            float3 p = ray.origin + ray.direction * center.a;
            float4 prevClip = mul(_HoVolumeLightPreviousVP, float4(p, 1));
            float2 prevUV = ComputeNormalizedDeviceCoordinates(p, _HoVolumeLightPreviousVP);
            float2 halfTexel = .5 / size;
            if (prevClip.w > 0 && all(prevUV >= halfTexel) && all(prevUV <= 1 - halfTexel))
            {
                int2 previousPixel = clamp((int2)(prevUV * size), 0, (int2)size - 1);
                float rawPrevious = LOAD_TEXTURE2D(_HoVolumeLightPreviousDepth, previousPixel).r;
                float2 previousUV = (previousPixel + .5) / size;
                VolumeRay oldRay = VolumeRayAt(previousUV, rawPrevious, _HoVolumeLightPreviousInverseVP, _HoVolumeLightPreviousPosition.xyz, _HoVolumeLightPreviousPosition.w > .5);
                if (VolumeInterval(oldRay))
                {
                    // Integrated rays have depth-dependent parallax. Reject mismatched endpoints.
                    float2 a = ComputeNormalizedDeviceCoordinates(ray.origin + ray.direction * ray.start, _HoVolumeLightPreviousVP);
                    float2 b = ComputeNormalizedDeviceCoordinates(ray.origin + ray.direction * ray.end, _HoVolumeLightPreviousVP);
                    float span = max(length((a - prevUV) * size), length((b - prevUV) * size));
                    float tolerance = max(.05, (ray.end - ray.start) * .04);
                    float intervalMatch = max(distance(oldRay.origin + oldRay.direction * oldRay.start, ray.origin + ray.direction * ray.start), distance(oldRay.origin + oldRay.direction * oldRay.end, ray.origin + ray.direction * ray.end));
                    float confidence = saturate(1 - span / 1.5) * saturate(1 - intervalMatch / tolerance);
                    float4 history = LOAD_TEXTURE2D(_HoVolumeLightPrevious, previousPixel);
                    mean /= max(1, n);
                    float3 sigma = sqrt(max(0, square / max(1, n) - mean * mean));
                    // React to changed occluders using this frame's local distribution.
                    float3 oldColor = clamp(history.rgb, max(lo, mean - 1.5 * sigma), min(hi, mean + 1.5 * sigma));
                    float weight = min(_HoVolumeLightHistory.y, history.a / (history.a + 1)) * confidence;
                    // Accumulate unbiased current samples. The spatial estimate is a fallback
                    // for newly exposed rays, rather than a blur baked into every history frame.
                    current = lerp(current, lerp(center.rgb, oldColor, weight), confidence);
                    age = min(32, 1 + history.a * confidence);
                }
            }
        }
    }
    output.color = float4(max(0, current), age);
    return output;
}
float4 VolumeComposite(Varyings input) : SV_Target
{
    return float4(SAMPLE_TEXTURE2D_LOD(_HoVolumeLightCurrent, sampler_PointClamp, input.texcoord, 0).rgb, 0);
}
float4 VolumePresent(Varyings input) : SV_Target
{
    return float4(SAMPLE_TEXTURE2D_LOD(_HoVolumeLightCurrent, sampler_PointClamp, input.texcoord, 0).rgb, 1);
}
#endif
