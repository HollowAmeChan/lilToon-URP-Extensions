Shader "Hidden/HoShadows/TransparentResolve"
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
            #include "HoCharacterShadowFixedPcf.hlsl"

            Texture2D<uint2> _HoTransparentShadowCandidate;
            TEXTURE2D_SHADOW(_HoTransparentMainShadowAtlas);
            float4 _HoTransparentShadowConfiguration;
            float4 _HoTransparentShadowRasterSize;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float3 WorldPosition(int2 pixel, float depth)
            {
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                return ComputeWorldSpacePosition((pixel + 0.5) * _HoTransparentShadowRasterSize.zw, depth, UNITY_MATRIX_I_VP);
            }

            float EyeDepth(float raw)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
            }

            float3 SurfaceDelta(int2 pixel, uint owner, float eyeDepth, float3 world)
            {
                if (any(pixel >= (int2)_HoTransparentShadowRasterSize.xy))
                    return 0.0;
                uint2 other = LOAD_TEXTURE2D(_HoTransparentShadowCandidate, pixel);
                float raw = asfloat(other.x);
                if ((other.y & 65535u) != owner || abs(EyeDepth(raw) - eyeDepth) > max(0.005, abs(eyeDepth) * 0.01))
                    return 0.0;
                return WorldPosition(pixel, raw) - world;
            }

            float4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                int2 pixel = (int2)positionCS.xy;
                uint2 candidate = LOAD_TEXTURE2D(_HoTransparentShadowCandidate, pixel);
                uint owner = candidate.y & 65535u;
                if (owner < 256u)
                    return float4(1, 1, 0, 0);

                float raw = asfloat(candidate.x);
                float3 world = WorldPosition(pixel, raw);
                float4 result = float4(1, 1, 0, 0);
                if (_HoTransparentShadowConfiguration.x > 0)
                {
                    int cascade = _HoTransparentShadowConfiguration.x > 1.0 ? (int)ComputeCascadeIndex(world) : 0;
                    float4 coords = float4(mul(_MainLightWorldToShadow[cascade], float4(world, 1.0)).xyz, 0.0);
                    result.r = SampleShadowmap(TEXTURE2D_ARGS(_HoTransparentMainShadowAtlas, sampler_LinearClampCompare),
                        coords, GetMainLightShadowSamplingData(), GetMainLightShadowParams(), false);
                    result.a = 1.0;
                }
                if (_HoTransparentShadowConfiguration.y > 0)
                {
                    float eyeDepth = EyeDepth(raw);
                    float3 dx = SurfaceDelta(pixel + int2(1, 0), owner, eyeDepth, world);
                    float3 dy = SurfaceDelta(pixel + int2(0, 1), owner, eyeDepth, world);
                    bool soft = _HoTransparentShadowConfiguration.z > 0;
                    HoCharacterShadowLocalSample local = HoCSFixedEvaluate(world, dx, dy, owner, soft, soft ? 1.5 : 1.0);
                    result.gb = float2(local.localVisibility, local.blend);
                }
                return result;
            }
            ENDHLSL
        }
    }
}
