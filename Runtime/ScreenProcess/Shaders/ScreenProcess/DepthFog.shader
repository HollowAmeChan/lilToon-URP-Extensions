Shader "Hidden/lilToon/URP/ScreenProcess/DepthFog"
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
            Name "ScreenProcess Depth Fog"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ScreenProcess/Shaders/ScreenProcess/ScreenProcessRuleMask.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ImageProcess/Shaders/ImageProcess/ImageProcessBlend.hlsl"

            // A compositing fog layer with two independent slots: a depth (distance) fog and a height
            // fog. Either slot can be switched off, and both can run in one pass - the user stacks
            // them inside this effect instead of stacking ScreenProcess layers.
            //
            // The maths here mirrors Runtime/ScreenProcess/ScreenProcessFogMath.cs one to one, so the
            // editor UI, the presets and the shader cannot disagree about what a mode means.
            float _Intensity;
            float _LayerBlendMode;
            float4 _LayerColor;   // rgb: depth slot's near colour (thin end)
            float4 _LayerParams0; // x depth slot on, y depth mode (0 linear, 1 exp, 2 exp2), z start distance, w far distance
            float4 _LayerParams1; // x density, y depth slot max opacity, z far colour mix, w desaturate
            float4 _LayerParams2; // xyz far colour, w height slot on
            float4 _LayerParams3; // x height mode (0 window-below, 1 window-above, 2 falloff-below, 3 falloff-above), y height reference (0 world, 1 camera), z height A, w height B
            float4 _LayerParams4; // x height hardness, y height slot max opacity, z height colour r, w height colour g
            float4 _LayerParams5; // x height colour b, y sky mode (0 skip, 1 include, 2 tint), z sky strength, w output dither (8-bit LSB)

            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);
            // Declared per shader (not by any include), exactly like DepthOfField/Outline do.
            float _HoGeometryBufferValid;

            // ---- ScreenProcessFogMath mirrors -------------------------------------------------

            float FogDepthAlpha(int mode, float distanceValue, float start, float farDistance, float density)
            {
                float d = max(distanceValue, 0.0);
                if (mode == 0)
                {
                    float span = farDistance - start;
                    if (span <= 0.00001)
                    {
                        return d >= farDistance ? 1.0 : 0.0;
                    }

                    return saturate((d - start) / span);
                }

                float scaled = density * max(d - start, 0.0);
                if (mode == 2)
                {
                    scaled *= scaled;
                }

                return 1.0 - exp(-scaled);
            }

            float FogApplyHardness(float value, float hardness)
            {
                float exponent = max(hardness, 0.0001);
                float a = pow(saturate(value), exponent);
                float b = pow(saturate(1.0 - value), exponent);
                return a / max(a + b, 0.0001);
            }

            float FogHeightAlpha(bool windowMode, bool reverse, float height, float a, float b, float hardness)
            {
                if (windowMode)
                {
                    float low = min(a, b);
                    float high = max(a, b);
                    float span = high - low;
                    float t = span <= 0.00001 ? (height >= high ? 1.0 : 0.0) : saturate((height - low) / span);
                    t = t * t * (3.0 - 2.0 * t);
                    t = FogApplyHardness(t, hardness);
                    return reverse ? t : 1.0 - t;
                }

                // Dense at/below the base and decaying upwards; reversed = dense above (clouds).
                float distanceFromBase = reverse ? max(a - height, 0.0) : max(height - a, 0.0);
                return exp(-max(b, 0.0) * distanceFromBase);
            }

            // Inverse of Unity's LinearEyeDepth(depth, _ZBufferParams): the GeometryBuffer stores the
            // linear eye depth while ComputeWorldSpacePosition needs the device depth.
            float FogDeviceDepthFromLinearEye(float linearEyeDepth, float zBufferParamZ, float zBufferParamW)
            {
                float linearDepth = max(linearEyeDepth, 0.000001);
                if (abs(zBufferParamZ) <= 0.000000001)
                {
                    return 0.0;
                }

                return (1.0 / linearDepth - zBufferParamW) / zBufferParamZ;
            }

            half3 FogComposite(half3 baseColor, half3 fogColor, float alpha, float blendMode)
            {
                half3 blended = ApplyLayerBlend(baseColor, fogColor, blendMode);
                return lerp(baseColor, blended, saturate(alpha));
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float intensity = saturate(_Intensity);
                bool depthSlotOn = _LayerParams0.x > 0.5;
                bool heightSlotOn = _LayerParams2.w > 0.5;
                if (intensity <= 0.0001 || (!depthSlotOn && !heightSlotOn))
                {
                    return source;
                }

                bool hasGeometryBuffer = _HoGeometryBufferValid > 0.5;
                bool isOrthographic = unity_OrthoParams.w > 0.5;

                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                float coverage = hasGeometryBuffer ? LilHoGeometryBufferCoverage(normalDepth) : 1.0;
                bool isSky = hasGeometryBuffer ? coverage < 0.5 : false;

                // Linear eye depth does not work for orthographic projections, so orthographic cameras
                // always take the device-depth path (URP's own deferred fog also skips ortho).
                float deviceDepth = (hasGeometryBuffer && !isOrthographic)
                    ? FogDeviceDepthFromLinearEye(normalDepth.a, _ZBufferParams.z, _ZBufferParams.w)
                    : SampleSceneDepth(uv);

                if (!hasGeometryBuffer)
                {
                    // Without the GeometryBuffer the far plane stands in for "sky".
                    float farDeviceDepth = UNITY_REVERSED_Z ? 0.0 : 1.0;
                    isSky = abs(deviceDepth - farDeviceDepth) <= 0.00001;
                }

                int skyMode = (int)round(_LayerParams5.y);
                if (isSky && skyMode == 0)
                {
                    return source;
                }

                float3 positionWS = ComputeWorldSpacePosition(uv * 2.0 - 1.0, deviceDepth, UNITY_MATRIX_I_VP);
                float3 positionVS = mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).xyz;

                float layerMask = 1.0;
                if (_LayerRuleMaskEnabled > 0.5)
                {
                    layerMask = saturate(LilScreenProcessResolveRequiredRuleMask(uv));
                }

                float amount = intensity * layerMask;
                float blendMode = _LayerBlendMode;
                half3 result = source.rgb;

                // ---- slot 1: depth (distance) fog -------------------------------------------------
                if (depthSlotOn)
                {
                    float distanceValue = _LayerParams1.w > 0.5 ? length(positionVS) : -positionVS.z;
                    float depthAlpha = FogDepthAlpha(
                        (int)round(_LayerParams0.y),
                        distanceValue,
                        _LayerParams0.z,
                        _LayerParams0.w,
                        _LayerParams1.x);
                    float alpha = saturate(depthAlpha) * saturate(_LayerParams1.y);

                    if (isSky)
                    {
                        // Sky pixels have no meaningful distance: "include" keeps the far-plane result,
                        // "tint" uses the sky strength with the far colour.
                        alpha = skyMode == 2 ? saturate(_LayerParams5.z) : alpha;
                    }

                    if (alpha > 0.0001)
                    {
                        half luminance = dot(result, half3(0.2126, 0.7152, 0.0722));
                        half3 base = lerp(result, luminance.xxx, saturate(alpha * _LayerParams1.w));
                        half3 fogColor = lerp(
                            _LayerColor.rgb,
                            _LayerParams2.rgb,
                            saturate(alpha * _LayerParams1.z));
                        result = FogComposite(base, fogColor, alpha * amount, blendMode);
                    }
                }

                // ---- slot 2: height fog -----------------------------------------------------------
                if (heightSlotOn && !(isSky && skyMode == 2))
                {
                    int heightSlotMode = (int)clamp(round(_LayerParams3.x), 0.0, 3.0);
                    bool windowMode = heightSlotMode < 2;
                    bool reverse = heightSlotMode == 1 || heightSlotMode == 3;
                    float height = _LayerParams3.y > 0.5 ? positionVS.y : positionWS.y;
                    float heightAlpha = FogHeightAlpha(
                        windowMode,
                        reverse,
                        height,
                        _LayerParams3.z,
                        _LayerParams3.w,
                        _LayerParams4.x);

                    // On sky pixels the height band is meaningless, so "include" drops the band instead
                    // of letting an arbitrary far-plane height decide.
                    if (isSky)
                    {
                        heightAlpha = 1.0;
                    }

                    float alpha = saturate(heightAlpha) * saturate(_LayerParams4.y);
                    if (alpha > 0.0001)
                    {
                        half3 heightColor = half3(_LayerParams4.z, _LayerParams4.w, _LayerParams5.x);
                        result = FogComposite(result, heightColor, alpha * amount, blendMode);
                    }
                }

                float ditherStrength = max(_LayerParams5.w, 0.0);
                if (ditherStrength > 0.0001)
                {
                    float2 pixel = floor(uv * _ScreenParams.xy);
                    float3 noise = float3(
                        Hash12(pixel + float2(0.5, 0.5)),
                        Hash12(pixel + float2(37.7, 11.3)),
                        Hash12(pixel + float2(91.3, 73.1)));
                    noise = noise * 2.0 - 1.0;
                    noise = sign(noise) * (1.0 - sqrt(max(1.0 - abs(noise), 0.0)));
                    result += half3(noise * (ditherStrength * 0.5 / 255.0));
                }

                return half4(result, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
