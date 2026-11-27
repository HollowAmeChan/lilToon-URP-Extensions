Shader "Hidden/HoShadows/CharacterResolve"
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../../../ShaderLibrary/HoPass/Encoding/HoBufferEncoding.hlsl"
            #include "HoCharacterShadowFixedPcf.hlsl"

            TEXTURE2D_FLOAT(_HoCharacterScreenCameraDepth);
            TEXTURE2D_FLOAT(_HoCharacterScreenSurfaceDepth);
            TEXTURE2D(_HoCharacterScreenOwner);
            float4 _HoCharacterScreenRasterSize;
            float _HoCharacterScreenSoft;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                return output;
            }

            bool HasGeometry(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth > 0.0;
                #else
                    return rawDepth < 1.0;
                #endif
            }

            float EyeDepth(float rawDepth)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float3 WorldPosition(int2 pixel, float depth)
            {
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                float2 uv = (pixel + 0.5) * _HoCharacterScreenRasterSize.zw;
                return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
            }

            bool MatchSurface(int2 pixel, out uint owner, out float rawDepth, out float eyeDepth)
            {
                rawDepth = LOAD_TEXTURE2D(_HoCharacterScreenCameraDepth, pixel).r;
                float surfaceDepth = LOAD_TEXTURE2D(_HoCharacterScreenSurfaceDepth, pixel).r;
                eyeDepth = EyeDepth(rawDepth);
                owner = HoSurfaceOwnerDecode(LOAD_TEXTURE2D(_HoCharacterScreenOwner, pixel).rg);
                float tolerance = max(0.0001, abs(eyeDepth) * 0.00001);
                return owner >= 256u && HasGeometry(rawDepth) && HasGeometry(surfaceDepth)
                    && abs(eyeDepth - EyeDepth(surfaceDepth)) <= tolerance;
            }

            float3 SurfaceDelta(int2 pixel, int2 stepPixel, uint owner, float eyeDepth, float3 positionWS)
            {
                int2 limit = (int2)_HoCharacterScreenRasterSize.xy - 1;
                int2 samplePixel = pixel + stepPixel;
                if (any(samplePixel > limit))
                    return 0.0;
                uint neighborOwner;
                float neighborRawDepth, neighborEyeDepth;
                if (!MatchSurface(samplePixel, neighborOwner, neighborRawDepth, neighborEyeDepth)
                    || neighborOwner != owner || abs(neighborEyeDepth - eyeDepth) > max(0.005, abs(eyeDepth) * 0.01))
                    return 0.0;
                return WorldPosition(samplePixel, neighborRawDepth) - positionWS;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int2 pixel = (int2)input.positionCS.xy;
                uint owner;
                float rawDepth, eyeDepth;
                if (!MatchSurface(pixel, owner, rawDepth, eyeDepth))
                    return float4(1, 0, 0, 0);

                float3 positionWS = WorldPosition(pixel, rawDepth);
                float3 dx = SurfaceDelta(pixel, int2(1, 0), owner, eyeDepth, positionWS);
                float3 dy = SurfaceDelta(pixel, int2(0, 1), owner, eyeDepth, positionWS);
                uint group = owner >> 8u;
                int slice = (int)_HoCSGroupSlices[group / 4u][group % 4u] - 1;
                if (slice < 0 || slice >= min(_HoCSCount, HO_CS_FIXED_MAX_SLICES))
                    return float4(1, 0, 0, 0);
                float radiusTexels = _HoCharacterScreenSoft > 0.5 ? 1.5 : 1.0;
                HoCharacterShadowLocalSample result = HoCSFixedEvaluate(positionWS, dx, dy, owner,
                    _HoCharacterScreenSoft > 0.5, radiusTexels);
                return result.valid != 0u ? float4(result.localVisibility, result.blend, group, owner & 255u) : float4(1, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
