Shader "Hidden/Ho-CharacterShadow/FilteringProbe"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(HO_CS_FILTER_FORCE_FALLBACK)
                #undef PLATFORM_SUPPORT_GATHER
            #endif
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            float _Mode, _Pattern, _Radius, _CropTexels, _ShiftTexels, _PlaneSlope, _EdgeAngle;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            float ComparePoint(float2 uv, float depth)
            {
                return HoCSCompareDepth(HoCSSampleRawDepth(uv).xxxx, depth.xxxx).x;
            }
            float ReferenceBilinear(float2 uv, float depth, float2 gradient)
            {
                float2 p = uv * _HoCSAtlasSize.zw - 0.5, f = frac(p);
                float2 a = (floor(p) + 0.5) * _HoCSAtlasSize.xy;
                float2 b = a + float2(_HoCSAtlasSize.x, 0);
                float2 c = a + float2(0, _HoCSAtlasSize.y), d = a + _HoCSAtlasSize.xy;
                return lerp(lerp(ComparePoint(a, depth + dot(a - uv, gradient)), ComparePoint(b, depth + dot(b - uv, gradient)), f.x),
                    lerp(ComparePoint(c, depth + dot(c - uv, gradient)), ComparePoint(d, depth + dot(d - uv, gradient)), f.x), f.y);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                if (_Mode < 0)
                {
                    float depth = input.uv.x - 0.5 < (input.uv.y - 0.5) * 0.4 + 0.37 / 4096.0 ? 0.7 : 0.3;
                    if (_Pattern == 1 || _Pattern == 3)
                    {
                        depth = 0.5 + (input.uv.x - 0.5) * _PlaneSlope;
                        #if UNITY_REVERSED_Z
                            if (_Pattern == 3) depth += 0.01;
                        #else
                            if (_Pattern == 3) depth -= 0.01;
                        #endif
                    }
                    if (_Pattern == 2) depth = input.uv.x < 0.5 ? 0.3 : 0.7;
                    if (_Pattern == 4) depth = fmod(floor(input.uv.x * 4096) + 3 * floor(input.uv.y * 4096), 5) < 2 ? 0.7 : 0.3;
                    if (_Pattern == 8)
                        depth = dot(input.uv - 0.5, float2(cos(_EdgeAngle), sin(_EdgeAngle))) < 0.37 / 4096.0 ? 0.7 : 0.3;
                    if (_Pattern == 9)
                    {
                        bool thinCaster = abs(input.uv.x - 0.5) < 0.6 / 4096.0;
                        #if UNITY_REVERSED_Z
                            depth = thinCaster ? 0.55 : 0.3;
                        #else
                            depth = thinCaster ? 0.45 : 0.7;
                        #endif
                    }
                    #if UNITY_REVERSED_Z
                        if (_Pattern == 5) depth = 0.51;
                        if (_Pattern == 6) depth = 0.49;
                        if (_Pattern == 7) depth = input.uv.x < 0.5 ? 0.501 : 0.55;
                    #else
                        if (_Pattern == 5) depth = 0.49;
                        if (_Pattern == 6) depth = 0.51;
                        if (_Pattern == 7) depth = input.uv.x < 0.5 ? 0.499 : 0.45;
                    #endif
                    return depth.xxxx;
                }
                float2 uv = 0.5 + ((input.uv - 0.5) * _CropTexels + float2(_ShiftTexels, 0)) / 4096.0;
                float depth = 0.5;
                if (_Pattern == 1 || _Pattern == 3 || _Pattern == 5 || _Pattern == 6) depth += (uv.x - 0.5) * _PlaneSlope;
                #if UNITY_REVERSED_Z
                    depth += 0.1 / 4096.0;
                #else
                    depth -= 0.1 / 4096.0;
                #endif
                float3 shadowDx = ddx(float3(uv, depth)), shadowDy = ddy(float3(uv, depth));
                float2 gradient = HoCSReceiverPlaneGradient(shadowDx, shadowDy);
                float3 receiverPlane = float3(gradient, 0.001);
                float2 lo = 0.5 / 4096.0, hi = 1 - lo;
                if (_Pattern == 2)
                {
                    hi.x = 0.5 - 0.5 / 4096.0;
                    uv.x = hi.x - input.uv.x * 2 / 4096.0;
                }
                if (_Mode == 4)
                {
                    float a = HoCSDirectionalPenumbra(0.1, 10);
                    float b = HoCSDirectionalPenumbra(0.2, 5);
                    return float4(a, b, HoCSRadiusTexels(0.008, 0.0005), 1);
                }
                if (_Mode == 3)
                {
                    float error = abs(HoCSCompareBilinear(uv, lo, hi, uv, depth, receiverPlane).x - ReferenceBilinear(uv, depth, gradient));
                    return float4(error.xxx, 1);
                }
                if (_Mode == 5)
                {
                    float2 blockers = HoCSSearchBlockers(uv, lo, hi, depth, receiverPlane, 12, 32);
                    return float4(blockers.x / max(blockers.y, 1e-8), blockers.y / 32.0, 0, 1);
                }
                if (_Mode == 8)
                {
                    float2 blockers = HoCSSearchBlockers(uv, lo, hi, depth, receiverPlane, 12, 32);
                    float radius = blockers.y > 0 ? HoCSRadiusTexels(HoCSDirectionalPenumbra(blockers.x / blockers.y, 2), 0.0005) : 0;
                    radius *= smoothstep(0, 1, blockers.y);
                    return float4(radius, blockers.y, 0, 1);
                }
                if (_Mode == 7)
                {
                    // Previous point/blocker-count implementation, retained only as a motion control.
                    float gapSum = 0, count = 0;
                    [loop] for (int i = 0; i < 32; i++)
                    {
                        float k = (float)max(i - 1, 0), pair = floor(k * 0.5);
                        float radius = sqrt((pair + 0.5) / 16.0);
                        float a = pair * 2.39996323;
                        float2 offset = i == 0 ? float2(0, 0) : float2(cos(a), sin(a)) * radius * (((i - 1) & 1) == 0 ? 1 : -1) * 12;
                        float2 q = clamp(uv + offset * _HoCSAtlasSize.xy, lo, hi);
                        q = clamp((floor(q * _HoCSAtlasSize.zw) + 0.5) * _HoCSAtlasSize.xy, lo, hi);
                        float rawDepth = HoCSSampleRawDepth(q);
                        #if UNITY_REVERSED_Z
                            float gap = rawDepth - depth;
                        #else
                            float gap = depth - rawDepth;
                        #endif
                        if (gap > _HoCSPcssParams2.x) { gapSum += gap; count++; }
                    }
                    return float4(gapSum / max(count, 1), count / 32.0, 0, 1);
                }
                if (_Mode == 6)
                {
                    // Independent per-cell reference; exercises paired-fetch packing at every phase.
                    float support = sqrt(2.25 + 1.5 * _Radius * _Radius);
                    float2 position = uv * _HoCSAtlasSize.zw, first = floor(position - support);
                    float reference = 0;
                    [loop] for (int y = 0; y < 8; y++)
                        [loop] for (int x = 0; x < 8; x++)
                        {
                            float2 start = first + float2(x, y) - position;
                            float wx = HoCSTentIntegral(start.x + 1, support) - HoCSTentIntegral(start.x, support);
                            float wy = HoCSTentIntegral(start.y + 1, support) - HoCSTentIntegral(start.y, support);
                            reference += wx * wy * ComparePoint((first + float2(x, y) + 0.5) * _HoCSAtlasSize.xy, depth);
                        }
                    float error = abs(HoCSResolveFilter(HoCSSampleDenseTent(uv, lo, hi, depth, receiverPlane, _Radius)) - reference);
                    return float4(error.xxx, 1);
                }
                float visibility;
                if (_Mode == 0) visibility = ComparePoint(uv, depth);
                else if (_Mode == 2)
                    visibility = HoCSSampleAtlasPcss(uv, lo, hi, depth, 0.0005, 2, receiverPlane, 0);
                else visibility = HoCSSampleDisk(uv, lo, hi, depth, _Radius, 64, receiverPlane);
                return float4(visibility.xxx, 1);
            }
            ENDHLSL
        }
    }
}
