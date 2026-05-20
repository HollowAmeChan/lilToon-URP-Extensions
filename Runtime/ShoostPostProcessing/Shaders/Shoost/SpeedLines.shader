Shader "Hidden/lilToon-Shoost/URP/Shoost/SpeedLines"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "Shoost Speed Lines"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSpeedLines

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerColor;
            float4 _LayerParams0; // x center x, y center y, z center blank, w coverage
            float4 _LayerParams1; // x density, y width, z length variation, w softness
            float4 _LayerParams2; // x speed, y rotation degrees, z flicker, w seed
            float4 _LayerParams3; // x layer amount, y background darken, z step fps, w broken amount

            float ShoostSpeedHash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453123);
            }

            float ShoostSpeedLineMask(float2 local, float radial, float density, float rotation, float seed, float frame, float widthScale, float lengthScale, float coverage, float centerBlank, float lineWidth, float lengthVariation, float softness, float flicker, float brokenAmount)
            {
                float angle = atan2(local.y, local.x) + rotation;
                angle = frac(angle / 6.28318530718) * 6.28318530718;

                float angularCell = angle / 6.28318530718 * max(density, 1.0);
                float lineIndex = floor(angularCell);
                float lineCenterDistance = abs(frac(angularCell) - 0.5) * 2.0;
                float animatedSeed = seed + lineIndex * 11.17 + frame * 5.31;
                float rnd = ShoostSpeedHash11(animatedSeed + 0.13);
                float rndWidth = ShoostSpeedHash11(animatedSeed + 3.71);
                float rndPresence = ShoostSpeedHash11(animatedSeed + 8.97);
                float rndBreak = ShoostSpeedHash11(animatedSeed + 15.43);

                float presence = step(1.0 - saturate(coverage), rndPresence);
                float available = max(1.0 - centerBlank, 0.0001);
                float inner = saturate(centerBlank + rnd * lengthVariation * available * 0.82 * lengthScale);
                float radialSoft = lerp(0.002, 0.09, softness);
                float radialMask = smoothstep(inner, inner + radialSoft, radial);
                float tipProgress = saturate((radial - inner) / max(1.0 - inner, 0.0001));
                float tipGrow = pow(tipProgress, lerp(1.9, 0.85, lineWidth));

                float width = lerp(0.012, 0.42, lineWidth) * widthScale * lerp(0.62, 1.48, rndWidth) * tipGrow;
                float angularSoft = max(0.001, lerp(0.004, 0.16, softness));
                float angularMask = 1.0 - smoothstep(width, min(1.0, width + angularSoft), lineCenterDistance);
                float broken = lerp(1.0, step(brokenAmount * 0.72, rndBreak), saturate(brokenAmount));
                float frameFlicker = lerp(1.0, lerp(0.72, 1.15, ShoostSpeedHash11(animatedSeed + 22.0)), flicker);

                return saturate(radialMask * angularMask * presence * broken * frameFlicker);
            }

            half4 FragSpeedLines(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 center = float2(0.5, 0.5);
                float centerBlank = saturate(_LayerParams0.z);
                float coverage = saturate(_LayerParams0.w);
                float density = max(_LayerParams1.x, 1.0);
                float lineWidth = saturate(_LayerParams1.y);
                float lengthVariation = saturate(_LayerParams1.z);
                float softness = saturate(_LayerParams1.w);
                float speed = _LayerParams2.x;
                float flicker = saturate(_LayerParams2.z);
                float seed = _LayerParams2.w;
                float layerAmount = saturate(_LayerParams3.x);
                float backgroundDarken = saturate(_LayerParams3.y);
                float stepFps = max(_LayerParams3.z, 1.0);
                float brokenAmount = saturate(_LayerParams3.w);

                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 localRaw = input.texcoord - center;
                float2 local = localRaw;
                local.x *= aspect;

                float2 absRaw = max(abs(localRaw), 0.00001);
                float edgeX = localRaw.x >= 0.0 ? (1.0 - center.x) / absRaw.x : center.x / absRaw.x;
                float edgeY = localRaw.y >= 0.0 ? (1.0 - center.y) / absRaw.y : center.y / absRaw.y;
                float radial = saturate(1.0 / max(min(edgeX, edgeY), 0.0001));

                float frame = floor(_Time.y * stepFps * max(speed, 0.0) * 0.25);
                float mainMask = ShoostSpeedLineMask(local, radial, density, 0.0, seed, frame, 1.0, 1.0, coverage, centerBlank, lineWidth, lengthVariation, softness, flicker, brokenAmount);
                float middleMask = ShoostSpeedLineMask(local, radial, density * 0.58 + 5.0, 0.021, seed + 37.0, frame, 0.48, 0.72, coverage * 0.58, centerBlank, lineWidth, lengthVariation, softness, flicker, brokenAmount * 0.75);
                float fineMask = ShoostSpeedLineMask(local, radial, density * 1.72 + 11.0, -0.014, seed + 71.0, frame, 0.22, 0.56, coverage * 0.42, centerBlank, lineWidth, lengthVariation, softness, flicker, brokenAmount * 0.55);
                float lineMask = saturate(max(mainMask, middleMask * layerAmount) + fineMask * layerAmount * 0.68);
                float edgeMask = smoothstep(centerBlank, min(1.0, centerBlank + max(1.0 - centerBlank, 0.0001)), radial);
                float opacity = saturate(lineMask * intensity * _LayerColor.a);
                float darken = edgeMask * intensity * backgroundDarken * 0.72;

                half3 result = source.rgb * (1.0 - darken);
                result = lerp(result, _LayerColor.rgb, opacity);
                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
