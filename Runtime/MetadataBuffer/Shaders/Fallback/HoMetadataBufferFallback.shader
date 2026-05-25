Shader "Hidden/lilToon/URP/MetadataBuffer/Fallback"
{
    Properties
    {
        [HideInInspector] _HoAovMaskWeight ("HoAOV Mask Weight", Float) = 1
        [HideInInspector] _lilHoAovSystemChannelMask ("HoAOV Feature System Channel Mask", Float) = 4031
        [HideInInspector] _HoAovSystemWriteMask ("HoAOV System Write Mask", Float) = 1055
        [HideInInspector] _HoAovCustomWriteMask ("HoAOV Custom Write Mask", Float) = 0
        [HideInInspector] _HoMetadataBufferGroupId ("HoAOV Group Id", Float) = 0
        [HideInInspector] _HoAovObjectId ("HoAOV Object Id", Float) = 0
        [HideInInspector] _HoAovMaterialClass ("HoAOV Material Class", Float) = 0
        [HideInInspector] _HoAovFlags ("HoAOV Flags", Float) = 0
        [HideInInspector] _HoAovThickness ("HoAOV Thickness", Float) = 0
        [HideInInspector] _HoAovCurvature ("HoAOV Curvature", Float) = 0
        [HideInInspector] _HoAovTransmittanceHint ("HoAOV Transmittance Hint", Float) = 0
        [HideInInspector] _HoAovDebugColor ("HoAOV Debug Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _HoAovCustomValues0 ("HoAOV Custom 0-3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoAovObjectCustomMask ("HoAOV Object Custom Mask", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite On
        ZTest Less
        Cull Off

        Pass
        {
            Name "MetadataBuffer Fallback"
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
            float _HoMetadataBufferGroupId;
            float _HoAovObjectId;
            float _HoAovMaterialClass;
            float _HoAovFlags;
            float _HoAovThickness;
            float _HoAovCurvature;
            float _HoAovTransmittanceHint;
            float4 _HoAovDebugColor;
            float4 _HoAovCustomValues0;
            float _HoAovObjectCustomMask;

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct FragmentOutput
            {
                half4 maskId : SV_Target0;
                half4 normalDepth : SV_Target1;
                half4 surfaceData : SV_Target2;
                half4 custom0 : SV_Target3;
                half4 objectCustom0 : SV_Target4;
                half4 objectCustom1 : SV_Target5;
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

            float EncodeByte(float value)
            {
                return saturate(round(clamp(value, 0.0, 255.0)) / 255.0);
            }

            float4 ApplyCustomWriteMask(float4 values, float startBit)
            {
                return float4(
                    values.x * HasBit(_HoAovCustomWriteMask, exp2(startBit)),
                    values.y * HasBit(_HoAovCustomWriteMask, exp2(startBit + 1.0)),
                    values.z * HasBit(_HoAovCustomWriteMask, exp2(startBit + 2.0)),
                    values.w * HasBit(_HoAovCustomWriteMask, exp2(startBit + 3.0)));
            }

            float ByteToFloat(uint value, uint shift)
            {
                return (float)((value >> shift) & 255u);
            }

            float HasObjectCustomBit(uint mask, uint bitIndex)
            {
                return (float)((mask >> bitIndex) & 1u);
            }

            float4 DecodeObjectCustom0(uint mask)
            {
                return float4(
                    HasObjectCustomBit(mask, 0u),
                    HasObjectCustomBit(mask, 1u),
                    HasObjectCustomBit(mask, 2u),
                    HasObjectCustomBit(mask, 3u));
            }

            float4 DecodeObjectCustom1(uint mask)
            {
                return float4(
                    HasObjectCustomBit(mask, 4u),
                    HasObjectCustomBit(mask, 5u),
                    HasObjectCustomBit(mask, 6u),
                    HasObjectCustomBit(mask, 7u));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
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
                float thicknessEnabled = HasSystemChannel(256.0);
                float curvatureEnabled = HasSystemChannel(512.0);
                float materialEnabled = HasSystemChannel(1024.0);
                float transmittanceHintEnabled = HasSystemChannel(2048.0);
                float subjectCoverage = saturate(_HoAovMaskWeight);
                float subjectValid = step(0.0001, subjectCoverage);

                float3 normalWS = normalize(input.normalWS);
                float linearDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                uint rendererUserValue = unity_RendererUserValue;
                bool hasRendererUserValue = rendererUserValue != 0u;
                uint objectCustomMask = hasRendererUserValue ? (rendererUserValue & 255u) : (uint)round(saturate(_HoAovObjectCustomMask / 255.0) * 255.0);
                float effectiveGroupId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 8u) : _HoMetadataBufferGroupId;
                float effectiveObjectId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 16u) : _HoAovObjectId;
                float effectiveFlags = hasRendererUserValue ? ByteToFloat(rendererUserValue, 24u) : _HoAovFlags;

                FragmentOutput output;
                output.maskId = half4(
                    subjectCoverage * maskEnabled,
                    EncodeByte(effectiveGroupId) * idEnabled * subjectValid,
                    EncodeByte(effectiveObjectId) * idEnabled * subjectValid,
                    EncodeByte(effectiveFlags) * flagsEnabled * subjectValid);
                output.normalDepth = half4((normalWS * 0.5 + 0.5) * worldNormalEnabled * subjectValid, linearDepth * linearDepthEnabled * subjectValid);
                output.surfaceData = half4(
                    saturate(_HoAovThickness) * thicknessEnabled * subjectValid,
                    saturate(abs(_HoAovCurvature)) * curvatureEnabled * subjectValid,
                    EncodeScalar(_HoAovMaterialClass) * materialEnabled * subjectValid,
                    saturate(_HoAovTransmittanceHint) * transmittanceHintEnabled * subjectValid);
                output.custom0 = half4(ApplyCustomWriteMask(_HoAovCustomValues0, 0.0) * subjectValid);
                output.objectCustom0 = half4(DecodeObjectCustom0(objectCustomMask) * subjectValid);
                output.objectCustom1 = half4(DecodeObjectCustom1(objectCustomMask) * subjectValid);
                return output;
            }
            ENDHLSL
        }

    }

    Fallback Off
}
