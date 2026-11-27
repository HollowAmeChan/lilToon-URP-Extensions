Shader "Hidden/HoShadows/CharacterCaptureResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "../../Shadows/Resources/HoCharacterShadowFixedPcf.hlsl"

            Texture2D<float4> _HoCaptureShadowResolveInput;
            TEXTURE2D_SHADOW(_HoCaptureShadowResolveAtlas);
            float4 _HoCaptureShadowResolveConfiguration;
            float4 _HoCaptureShadowResolveSize;

            float4 Vert(uint vertex : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(vertex);
            }

            uint SurfaceOwner(float4 surface)
            {
                return ((uint)surface.g << 8u) | (uint)surface.b;
            }

            float3 WorldPosition(int2 pixel, float raw)
            {
                #if !UNITY_REVERSED_Z
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
                #endif
                float2 uv = (pixel + 0.5) * _HoCaptureShadowResolveSize.zw;
                return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            }

            float EyeDepth(float raw)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
            }

            float3 SurfaceDelta(int2 pixel, uint owner, float eyeDepth, float3 world)
            {
                if (any(pixel >= (int2)_HoCaptureShadowResolveSize.xy))
                    return 0.0;
                float4 surface = LOAD_TEXTURE2D(_HoCaptureShadowResolveInput, pixel);
                if (surface.a != 1.0 || SurfaceOwner(surface) != owner
                    || abs(EyeDepth(surface.r) - eyeDepth) > max(0.005, abs(eyeDepth) * 0.01))
                    return 0.0;
                return WorldPosition(pixel, surface.r) - world;
            }

            float4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                int2 pixel = (int2)positionCS.xy;
                float4 surface = LOAD_TEXTURE2D(_HoCaptureShadowResolveInput, pixel);
                uint owner = SurfaceOwner(surface);
                float4 result = float4(1, 1, 0, 0);
                if (surface.a != 1.0 || owner < 256u)
                    return result;

                float3 world = WorldPosition(pixel, surface.r);
                if (_HoCaptureShadowResolveConfiguration.x > 0)
                {
                    int cascade = _HoCaptureShadowResolveConfiguration.x > 1.0 ? (int)ComputeCascadeIndex(world) : 0;
                    float4 coordinates = float4(mul(_MainLightWorldToShadow[cascade], float4(world, 1.0)).xyz, 0.0);
                    result.r = SampleShadowmap(TEXTURE2D_ARGS(_HoCaptureShadowResolveAtlas, sampler_LinearClampCompare),
                        coordinates, GetMainLightShadowSamplingData(), GetMainLightShadowParams(), false);
                    result.a = 1.0;
                }

                if (_HoCaptureShadowResolveConfiguration.y > 0)
                {
                    float eyeDepth = EyeDepth(surface.r);
                    float3 dx = SurfaceDelta(pixel + int2(1, 0), owner, eyeDepth, world);
                    float3 dy = SurfaceDelta(pixel + int2(0, 1), owner, eyeDepth, world);
                    bool soft = _HoCaptureShadowResolveConfiguration.z > 0;
                    HoCharacterShadowLocalSample local = HoCSFixedEvaluate(world, dx, dy, owner, soft, soft ? 1.5 : 1.0);
                    result.gb = float2(local.localVisibility, local.blend);
                }
                return result;
            }
            ENDHLSL
        }
    }
}
