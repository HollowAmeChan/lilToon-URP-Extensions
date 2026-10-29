Shader "Hidden/lilToon/URP/VolumeLight/Local"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Local Volume"
            ZWrite Off ZTest Always Cull Off
            Blend One One, Zero One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment LocalVolume
            #include "HoVolumeLightSolarCommon.hlsl"
            TEXTURE2D_SHADOW(_HoLocalShadow);
            float4x4 _HoLocalShadowMatrices[6], _HoLocalWorldToLight;
            float4 _HoLocalPositionRange, _HoLocalDirectionType, _HoLocalColor, _HoLocalAttenuation, _HoLocalShape, _HoLocalShadowParams;
            float LocalShadow(float3 p, float3 direction)
            {
                if (_HoLocalShadowParams.x <= 0) return 1;
                int face = _HoLocalDirectionType.w > .5 ? CubeMapFaceID(-direction) : 0;
                float4 clipPosition = mul(_HoLocalShadowMatrices[face], float4(p, 1));
                float3 uv = clipPosition.xyz / clipPosition.w;
                float2 tile = _HoLocalDirectionType.w > .5 ? float2(1.0/3, .5) : 1;
                float2 minimum = _HoLocalDirectionType.w > .5 ? float2(face % 3, face / 3) * tile : 0;
                if (any(uv.xy < minimum) || any(uv.xy > minimum + tile) || uv.z <= 0 || uv.z >= 1) return 1;
                float visibility = 0;
                if (_HoLocalShadowParams.w > .5)
                {
                    [unroll] for (int y = -1; y <= 1; ++y) [unroll] for (int x = -1; x <= 1; ++x)
                    {
                        float2 sampleUV = clamp(uv.xy + float2(x, y) * _HoLocalShadowParams.yz, minimum + _HoLocalShadowParams.yz * .5, minimum + tile - _HoLocalShadowParams.yz * .5);
                        visibility += SAMPLE_TEXTURE2D_SHADOW(_HoLocalShadow, sampler_LinearClampCompare, float3(sampleUV, uv.z));
                    }
                    visibility /= 9;
                }
                else visibility = SAMPLE_TEXTURE2D_SHADOW(_HoLocalShadow, sampler_LinearClampCompare, uv);
                return lerp(1, visibility, _HoLocalShadowParams.x);
            }
            float4 LocalVolume(Varyings input) : SV_Target
            {
                VolumeRay ray = VolumeRayAt(input.texcoord, SampleSceneDepth(input.texcoord), UNITY_MATRIX_I_VP, _WorldSpaceCameraPos, unity_OrthoParams.w > .5);
                if (!VolumeInterval(ray)) return 0;
                float3 delta = ray.origin - _HoLocalPositionRange.xyz;
                float b = dot(delta, ray.direction), c = dot(delta, delta) - _HoLocalPositionRange.w * _HoLocalPositionRange.w;
                float discriminant = b*b-c;
                if (discriminant <= 0) return 0;
                ray.start = max(ray.start, -b-sqrt(discriminant)); ray.end = min(ray.end, -b+sqrt(discriminant));
                if (_HoLocalDirectionType.w < .5)
                {
                    float3 o = mul(_HoLocalWorldToLight, float4(ray.origin, 1)).xyz;
                    float3 d = mul((float3x3)_HoLocalWorldToLight, ray.direction);
                    // Conservative cone frustum bounds; exact circular cone and the
                    // Light's inner/outer angles are evaluated at every sample.
                    const float3 planes[5] = {float3(0,0,-1),float3(1,0,0),float3(-1,0,0),float3(0,1,0),float3(0,-1,0)};
                    [unroll] for (int plane = 0; plane < 5; ++plane)
                    {
                        float3 n = planes[plane]; if (plane > 0) n.z = -_HoLocalShape.z;
                        float a = dot(n, o), slope = dot(n, d);
                        if (abs(slope) < 1e-8) { if (a > 0) return 0; }
                        else if (slope > 0) ray.end = min(ray.end, -a/slope); else ray.start = max(ray.start, -a/slope);
                    }
                }
                if (ray.end <= ray.start) return 0;
                int count = (int)_HoVolumeLightParams.z;
                float stepLength = (ray.end-ray.start)/count;
                float offset = .5;
                if (_HoLocalShape.w > .5) offset = LOAD_TEXTURE2D(_HoVolumeLightBlueNoise, (uint2)input.positionCS.xy % 64).r * (255.0/256.0) + .5/256.0;
                float3 result = 0; float transmission = 1;
                [loop] for (int step = 0; step < count; ++step)
                {
                    float3 p = ray.origin + ray.direction * (ray.start + (step+offset)*stepLength);
                    HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _HoAtmosphereMediumTime);
                    float3 toLight = _HoLocalPositionRange.xyz-p;
                    float distanceSquared = max(dot(toLight, toLight), 1e-5);
                    float3 direction = toLight * rsqrt(distanceSquared);
                    float attenuation = DistanceAttenuation(distanceSquared, _HoLocalAttenuation.xy);
                    attenuation *= distanceSquared / max(distanceSquared, _HoLocalShape.x * _HoLocalShape.x);
                    if (_HoLocalDirectionType.w < .5) attenuation *= AngleAttenuation(_HoLocalDirectionType.xyz, direction, _HoLocalAttenuation.zw);
                    attenuation *= saturate((_HoLocalPositionRange.w-sqrt(distanceSquared)) / max(.001, _HoLocalPositionRange.w*_HoLocalShape.y));
                    float opticalDepth = medium.activation * medium.density * stepLength;
                    float amount = opticalDepth;
                    if (_HoAtmosphereMediumOptics.y > .5) { amount = transmission * (1-exp(-opticalDepth)); transmission *= exp(-opticalDepth); }
                    if (amount <= 0 || attenuation <= 0) continue;
                    result += amount * medium.tint * _HoAtmosphereMediumAlbedo.rgb * attenuation * LocalShadow(p, direction) * HoAtmospherePhase(dot(direction, ray.direction), _HoVolumeLightParams.w);
                }
                return float4(max(0, result * _HoLocalColor.rgb * _HoLocalColor.w * _HoVolumeLightParams.x), 0);
            }
            ENDHLSL
        }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment DebugShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D(_HoDebugShadow);
            float4 DebugShadow(Varyings input) : SV_Target
            {
                uint width, height; _HoDebugShadow.GetDimensions(width, height);
                float depth = LOAD_TEXTURE2D(_HoDebugShadow, min(uint2(input.texcoord*float2(width,height)), uint2(width-1,height-1))).r;
                return float4(depth,depth,depth,1);
            }
            ENDHLSL
        }
    }
}
