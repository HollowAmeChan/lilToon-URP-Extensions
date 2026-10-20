Shader "Hidden/lilToon/URP/VolumeLight/DustPointReference"
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
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/VolumeLight/Resources/HoVolumeLightSolarCommon.hlsl"
            StructuredBuffer<float4> _HoAtmosphereDustSeeds;
            float4 _HoAtmosphereDustShape; // min size, max size, flutter, max distance
            float4 _HoAtmosphereDustLighting; // brightness, time, direction override, effect intensity
            struct DustVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
                float energy : TEXCOORD3;
            };
            DustVaryings DustVert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                float4 seed = _HoAtmosphereDustSeeds[instance];
                float time = _HoAtmosphereDustLighting.y;
                float phase = seed.w * 6.2831853 + time * .35;
                float3 flutter = sin(float3(phase * 1.17, phase * .83 + 1.1, phase + 2.4)) * _HoAtmosphereDustShape.z;
                float3 center = seed.xyz + _HoAtmosphereMediumWind.xyz * time + flutter;
                float size = lerp(_HoAtmosphereDustShape.x, _HoAtmosphereDustShape.y, frac(seed.w * 17.31));
                float3 right = float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20);
                float3 up = float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21);
                float4 centerCS = TransformWorldToHClip(center);
                float pixelsPerMetre = abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y * .5 / max(.001, unity_OrthoParams.w > .5 ? 1 : centerCS.w);
                float pixelSize = size * pixelsPerMetre;
                // A 1px disc can miss every pixel centre at a grid corner. A compact
                // 2.5px footprint keeps coverage while conserving subpixel energy.
                float drawSize = max(size, 2.5 / max(1, pixelsPerMetre));
                const float2 corners[6] = { float2(-1,-1), float2(-1,1), float2(1,1), float2(-1,-1), float2(1,1), float2(1,-1) };
                DustVaryings output;
                output.uv = corners[vertex]; output.centerWS = center;
                output.positionWS = center + (right * output.uv.x + up * output.uv.y) * drawSize * .5;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.energy = min(1, (pixelSize * pixelSize) / (2.5 * 2.5));
                return output;
            }
            float4 DustFrag(DustVaryings input) : SV_Target
            {
                float radius = dot(input.uv, input.uv);
                clip(1 - radius);
                float3 p = input.centerWS;
                HoAtmosphereMediumSample medium = HoAtmosphereSampleMedium(p, _HoAtmosphereMediumTime);
                float activation = min(medium.activation, HoAtmosphereMediumActivation(input.positionWS));
                if (activation <= 0 || medium.density <= 0) return 0;
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUV);
                float3 surfaceWS = ComputeWorldSpacePosition(screenUV, VolumeDeviceDepth(rawDepth), UNITY_MATRIX_I_VP);
                float surfaceEye = -TransformWorldToView(surfaceWS).z;
                float particleEye = -TransformWorldToView(input.positionWS).z;
                float softIntersection = saturate((surfaceEye - particleEye) / max(.005, _HoAtmosphereDustShape.y));
                float distanceToCamera = distance(_WorldSpaceCameraPos, p);
                float fade = 1 - smoothstep(_HoAtmosphereDustShape.w * .8, _HoAtmosphereDustShape.w, distanceToCamera);
                float shape = pow(saturate(1 - radius), 2) * input.energy * softIntersection * fade;
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
