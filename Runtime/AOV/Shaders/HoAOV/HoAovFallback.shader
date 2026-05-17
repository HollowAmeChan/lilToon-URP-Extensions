Shader "Hidden/lilToon-HoAOV/URP/Fallback"
{
    Properties
    {
        [HideInInspector] _HoAovMaskWeight ("HoAOV Mask Weight", Float) = 1
        [HideInInspector] _lilHoAovSystemChannelMask ("HoAOV Feature System Channel Mask", Float) = 4095
        [HideInInspector] _HoAovSystemWriteMask ("HoAOV System Write Mask", Float) = 1119
        [HideInInspector] _HoAovCustomWriteMask ("HoAOV Custom Write Mask", Float) = 0
        [HideInInspector] _HoAovGroupId ("HoAOV Group Id", Float) = 0
        [HideInInspector] _HoAovObjectId ("HoAOV Object Id", Float) = 0
        [HideInInspector] _HoAovMaterialClass ("HoAOV Material Class", Float) = 0
        [HideInInspector] _HoAovFlags ("HoAOV Flags", Float) = 1
        [HideInInspector] _HoAovThickness ("HoAOV Thickness", Float) = 0
        [HideInInspector] _HoAovCurvature ("HoAOV Curvature", Float) = 0
        [HideInInspector] _HoAovUtility ("HoAOV Utility", Float) = 0
        [HideInInspector] _HoAovDebugColor ("HoAOV Debug Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _HoAovCustomValues0 ("HoAOV Custom 0-3", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "HoAOV Fallback"
            Tags { "LightMode" = "HoAOV" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _HoAovMaskWeight;
            float _lilHoAovSystemChannelMask;
            float _HoAovSystemWriteMask;
            float _HoAovCustomWriteMask;
            float _HoAovGroupId;
            float _HoAovObjectId;
            float _HoAovMaterialClass;
            float _HoAovFlags;
            float _HoAovThickness;
            float _HoAovCurvature;
            float _HoAovUtility;
            float4 _HoAovDebugColor;
            float4 _HoAovCustomValues0;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float objectSeed : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct FragmentOutput
            {
                half4 maskId : SV_Target0;
                half4 normalDepth : SV_Target1;
                half4 tangentNormal : SV_Target2;
                half4 surfaceData : SV_Target3;
                half4 custom0 : SV_Target4;
                half4 custom1 : SV_Target5;
                half4 custom2 : SV_Target6;
            };

            float HasBit(float value, float bitValue)
            {
                return step(0.5, fmod(floor(value / bitValue), 2.0));
            }

            float HasSystemChannel(float bitValue)
            {
                return HasBit(_HoAovSystemWriteMask, bitValue);
            }

            float EncodeScalar(float value)
            {
                return frac(abs(value) * 0.61803398875);
            }

            float4 ApplyCustomWriteMask(float4 values, float startBit)
            {
                return float4(
                    values.x * HasBit(_HoAovCustomWriteMask, exp2(startBit)),
                    values.y * HasBit(_HoAovCustomWriteMask, exp2(startBit + 1.0)),
                    values.z * HasBit(_HoAovCustomWriteMask, exp2(startBit + 2.0)),
                    values.w * HasBit(_HoAovCustomWriteMask, exp2(startBit + 3.0)));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 objectPositionWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                output.objectSeed = dot(objectPositionWS, float3(0.13, 0.31, 0.73));
                return output;
            }

            FragmentOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float maskEnabled = HasSystemChannel(1.0);
                float idEnabled = HasSystemChannel(2.0);
                float flagsEnabled = HasSystemChannel(4.0);
                float linearDepthEnabled = HasSystemChannel(8.0);
                float worldNormalEnabled = HasSystemChannel(16.0);
                float tangentNormalEnabled = HasSystemChannel(64.0);
                float thicknessEnabled = HasSystemChannel(256.0);
                float curvatureEnabled = HasSystemChannel(512.0);
                float materialEnabled = HasSystemChannel(1024.0);
                float utilityEnabled = HasSystemChannel(2048.0);
                float subjectCoverage = saturate(_HoAovMaskWeight);
                float subjectValid = step(0.0001, subjectCoverage);

                float3 normalWS = normalize(input.normalWS);
                float linearDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float effectiveObjectId = lerp(input.objectSeed * 1000.0, _HoAovObjectId, step(0.5, abs(_HoAovObjectId)));

                FragmentOutput output;
                output.maskId = half4(
                    subjectCoverage * maskEnabled,
                    EncodeScalar(_HoAovGroupId) * idEnabled * subjectValid,
                    EncodeScalar(effectiveObjectId) * idEnabled * subjectValid,
                    EncodeScalar(_HoAovFlags) * flagsEnabled * subjectValid);
                output.normalDepth = half4((normalWS * 0.5 + 0.5) * worldNormalEnabled * subjectValid, linearDepth * linearDepthEnabled * subjectValid);
                output.tangentNormal = half4(float3(0.5, 0.5, 1.0) * tangentNormalEnabled * subjectValid, tangentNormalEnabled * subjectValid);
                output.surfaceData = half4(
                    saturate(_HoAovThickness) * thicknessEnabled * subjectValid,
                    saturate(abs(_HoAovCurvature)) * curvatureEnabled * subjectValid,
                    EncodeScalar(_HoAovMaterialClass) * materialEnabled * subjectValid,
                    saturate(_HoAovUtility) * utilityEnabled * subjectValid);
                output.custom0 = half4(ApplyCustomWriteMask(_HoAovCustomValues0, 0.0) * subjectValid);
                output.custom1 = half4(0.0, 0.0, 0.0, 0.0);
                output.custom2 = half4(0.0, 0.0, 0.0, 0.0);
                return output;
            }
            ENDHLSL
        }

    }

    Fallback Off
}
