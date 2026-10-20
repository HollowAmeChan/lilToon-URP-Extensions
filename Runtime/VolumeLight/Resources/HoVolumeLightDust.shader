Shader "Hidden/lilToon/URP/VolumeLight/Dust"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Shared Dust"
            ZWrite Off ZTest Always Cull Off
            Blend One One, Zero One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DustVert
            #pragma fragment DustFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "HoVolumeLightSolarCommon.hlsl"
            StructuredBuffer<float4> _HoAtmosphereDustSeeds;
            TEXTURE2D(_HoAtmosphereDustPattern); SAMPLER(sampler_HoAtmosphereDustPattern);
            float4 _HoAtmosphereDustTexture; // grain density
            float4 _HoAtmosphereDustShape; // card size, texture world size, flutter, visible radius
            float4 _HoAtmosphereDustLighting; // brightness, time, direction override, effect intensity
            struct DustVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
                float2 patternUV : TEXCOORD3;
            };
            DustVaryings DustVert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                float4 seed = _HoAtmosphereDustSeeds[instance];
                float time = _HoAtmosphereDustLighting.y;
                float phase = seed.w * 6.2831853 + time * .35;
                float3 flutter = sin(float3(phase * 1.17, phase * .83 + 1.1, phase + 2.4)) * _HoAtmosphereDustShape.z;
                float3 center = seed.xyz + _HoAtmosphereMediumWind.xyz * time + flutter;
                float size = _HoAtmosphereDustShape.x;
                float3 right = float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20);
                float3 up = float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21);
                const float2 corners[6] = { float2(-1,-1), float2(-1,1), float2(1,1), float2(-1,-1), float2(1,1), float2(1,-1) };
                DustVaryings output;
                output.uv = corners[vertex]; output.centerWS = center;
                output.positionWS = center + (right * output.uv.x + up * output.uv.y) * size * .5;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                float angle = seed.w * 37.13, s = sin(angle), c = cos(angle);
                output.patternUV = mul(float2x2(c, -s, s, c), output.uv) * size / _HoAtmosphereDustShape.y * .5 + frac(seed.w * float2(173.3, 97.7));
                return output;
            }
            float4 DustFrag(DustVaryings input) : SV_Target
            {
                // Reject empty texels before density, depth and light-space shadow reads.
                // A finite threshold also fades distant mip levels before they become fog.
                float grain = SAMPLE_TEXTURE2D(_HoAtmosphereDustPattern, sampler_HoAtmosphereDustPattern, input.patternUV).a;
                float threshold = lerp(.65, .08, _HoAtmosphereDustTexture.x);
                grain = max(0, grain - threshold) / (1 - threshold);
                clip(grain - .005);
                float radius = max(abs(input.uv.x), abs(input.uv.y));
                float envelope = 1 - smoothstep(.55, 1, radius);
                clip(envelope - .005);
                float3 p = input.positionWS;
                HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _HoAtmosphereMediumTime);
                float activation = medium.activation;
                if (activation <= 0 || medium.density <= 0) return 0;
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUV);
                float3 surfaceWS = ComputeWorldSpacePosition(screenUV, VolumeDeviceDepth(rawDepth), UNITY_MATRIX_I_VP);
                float surfaceEye = -TransformWorldToView(surfaceWS).z;
                float particleEye = -TransformWorldToView(input.positionWS).z;
                float softIntersection = saturate((surfaceEye - particleEye) / .15);
                float distanceToCamera = distance(_WorldSpaceCameraPos, input.centerWS);
                float fade = 1 - smoothstep(_HoAtmosphereDustShape.w * .8, _HoAtmosphereDustShape.w, distanceToCamera);
                fade *= smoothstep(_HoAtmosphereDustShape.x * .15, _HoAtmosphereDustShape.x * .5, distanceToCamera);
                float shape = grain * envelope * softIntersection * fade;
                if (shape <= 0) return 0;
                Light sun = GetMainLight();
                float3 viewToCamera = GetWorldSpaceNormalizeViewDir(p);
                float phase = HoAtmospherePhase(dot(sun.direction, -viewToCamera), _HoAtmosphereDustLighting.z);
                float3 radiance = medium.density * medium.tint * _HoAtmosphereMediumAlbedo.rgb * activation;
                if (_HoAtmosphereMediumOptics.y > .5)
                {
                    VolumeRay ray = VolumeRayAt(screenUV, input.positionCS.z, UNITY_MATRIX_I_VP, _WorldSpaceCameraPos, unity_OrthoParams.w > .5);
                    if (VolumeInterval(ray))
                    {
                        float delta = (ray.end - ray.start) / 8, depth = 0;
                        [unroll] for (int step = 0; step < 8; ++step)
                        {
                            HoAtmosphereMediumSample m = HoAtmosphereSampleMedium(ray.origin + ray.direction * (ray.start + (step + .5) * delta), _HoAtmosphereMediumTime);
                            depth += m.activation * m.density * delta;
                        }
                        radiance *= exp(-depth);
                    }
                }
                radiance *= sun.color * phase * VolumeShadow(p) * _HoAtmosphereDustLighting.x * _HoAtmosphereDustLighting.w;
                return float4(max(0, radiance * shape), 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
