Shader "Hidden/Ho-CS/FilteringEdgeAB"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ HO_CS_FILTER_FORCE_FALLBACK
            #pragma multi_compile_local _ HO_CS_REFERENCE_IMPLEMENTATION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(HO_CS_FILTER_FORCE_FALLBACK)
                #undef PLATFORM_SUPPORT_GATHER
            #endif
            // The isolated project's baseline header is intentionally left unchanged.
            #if defined(HO_CS_REFERENCE_IMPLEMENTATION)
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSamplingBaseline.hlsl"
            #else
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl"
            #endif
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            float _Mode, _Pattern, _Radius, _CropTexels, _ShiftTexels, _PlaneSlope;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float atlasSize = _HoCSAtlasSize.z;
                const float tileBoundary = 1.0 / 3.0;
                if (_Mode < 0)
                {
                    float depth = input.uv.x - 0.5 < (input.uv.y - 0.5) * 0.4 + 0.37 / atlasSize ? 0.7 : 0.3;
                    // A 6144 atlas places this boundary exactly at texel 2048.
                    if (_Pattern == 2) depth = input.uv.x < tileBoundary ? 0.3 : 0.7;
                    if (_Pattern == 4)
                        depth = fmod(floor(input.uv.x * atlasSize) + 3 * floor(input.uv.y * atlasSize), 5) < 2 ? 0.7 : 0.3;
                    if (_Pattern == 9)
                    {
                        bool thinCaster = abs(input.uv.x - 0.5) < 0.6 / atlasSize;
                        #if UNITY_REVERSED_Z
                            depth = thinCaster ? 0.55 : 0.3;
                        #else
                            depth = thinCaster ? 0.45 : 0.7;
                        #endif
                    }
                    #if UNITY_REVERSED_Z
                        if (_Pattern == 5) depth = 0.51;
                    #else
                        if (_Pattern == 5) depth = 0.49;
                    #endif
                    return depth.xxxx;
                }

                float2 uv = 0.5 + ((input.uv - 0.5) * _CropTexels + float2(_ShiftTexels, 0)) * _HoCSAtlasSize.xy;
                float depth = 0.5;
                if (_Pattern == 5) depth += (uv.x - 0.5) * _PlaneSlope;
                #if UNITY_REVERSED_Z
                    depth += 0.1 / atlasSize;
                #else
                    depth -= 0.1 / atlasSize;
                #endif
                float3 shadowDx = ddx(float3(uv, depth)), shadowDy = ddy(float3(uv, depth));
                float3 receiverPlane = float3(HoCSReceiverPlaneGradient(shadowDx, shadowDy), 0.001);
                float2 lo = 0.5 * _HoCSAtlasSize.xy, hi = 1 - lo;
                if (_Pattern == 2)
                {
                    hi.x = tileBoundary - 0.5 / atlasSize;
                    uv.x = hi.x - input.uv.x * 2 / atlasSize;
                }

                int filters = clamp((int)round(_HoCSPcssParams2.z), 1, HO_CS_MAX_PCSS_FILTER_SAMPLES);
                float visibility;
                if (_Mode == 2)
                    visibility = HoCSSampleAtlasPcss(uv, lo, hi, depth, 0.0005, 2, receiverPlane, 0);
                else
                    visibility = HoCSSampleDisk(uv, lo, hi, depth, _Radius, filters, receiverPlane);
                return float4(visibility.xxx, 1);
            }
            ENDHLSL
        }
    }
}
