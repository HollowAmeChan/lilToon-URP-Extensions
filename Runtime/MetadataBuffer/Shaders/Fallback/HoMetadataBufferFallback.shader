Shader "Hidden/lilToon/URP/MetadataBuffer/Fallback"
{
    Properties
    {
        [HideInInspector] _HoMetadataBufferMaskWeight ("MetadataBuffer Mask Weight", Float) = 1
        [HideInInspector] _HoMetadataBufferSystemChannelMask ("MetadataBuffer Feature System Channel Mask", Float) = 3847
        [HideInInspector] _HoMetadataBufferSystemWriteMask ("MetadataBuffer System Write Mask", Float) = 3847
        [HideInInspector] _HoMetadataBufferCustomWriteMask ("MetadataBuffer Custom Write Mask", Float) = 0
        [HideInInspector] _HoMetadataBufferGroupId ("MetadataBuffer Group Id", Float) = 0
        [HideInInspector] _HoMetadataBufferObjectId ("MetadataBuffer Object Id", Float) = 0
        [HideInInspector] _HoMetadataBufferMaterialClass ("MetadataBuffer Material Class", Float) = 0
        [HideInInspector] _HoMetadataBufferFlags ("MetadataBuffer Flags", Float) = 0
        [HideInInspector] _HoMetadataBufferThickness ("MetadataBuffer Thickness", Float) = 0
        [HideInInspector] _HoMetadataBufferCurvature ("MetadataBuffer Curvature", Float) = 0
        [HideInInspector] _HoMetadataBufferTransmittanceHint ("MetadataBuffer Transmittance Hint", Float) = 0
        [HideInInspector] _HoMetadataBufferDebugColor ("MetadataBuffer Debug Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _HoMetadataBufferCustomValues0 ("MetadataBuffer Custom 0-3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HoMetadataBufferObjectCustomMask ("MetadataBuffer Object Custom Mask", Float) = 0
        [HideInInspector] _HoMetadataBufferRsuvAssigned ("MetadataBuffer RSUV Assigned", Float) = 0
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
            Tags { "LightMode" = "HoMetadataBuffer" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _HoMetadataBufferMaskWeight;
            float _HoMetadataBufferSystemChannelMask;
            float _HoMetadataBufferSystemWriteMask;
            float _HoMetadataBufferCustomWriteMask;
            float _HoMetadataBufferGroupId;
            float _HoMetadataBufferObjectId;
            float _HoMetadataBufferMaterialClass;
            float _HoMetadataBufferFlags;
            float _HoMetadataBufferThickness;
            float _HoMetadataBufferCurvature;
            float _HoMetadataBufferTransmittanceHint;
            float4 _HoMetadataBufferDebugColor;
            float4 _HoMetadataBufferCustomValues0;
            float _HoMetadataBufferObjectCustomMask;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct FragmentOutput
            {
                half4 maskId : SV_Target0;
                half4 surfaceData : SV_Target1;
                half4 custom0 : SV_Target2;
                half4 objectCustom0 : SV_Target3;
                half4 objectCustom1 : SV_Target4;
            };

            float HasBit(float value, float bitValue)
            {
                return step(0.5, fmod(floor(value / bitValue), 2.0));
            }

            float HasSystemChannel(float bitValue)
            {
                return HasBit(_HoMetadataBufferSystemWriteMask, bitValue);
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
                    values.x * HasBit(_HoMetadataBufferCustomWriteMask, exp2(startBit)),
                    values.y * HasBit(_HoMetadataBufferCustomWriteMask, exp2(startBit + 1.0)),
                    values.z * HasBit(_HoMetadataBufferCustomWriteMask, exp2(startBit + 2.0)),
                    values.w * HasBit(_HoMetadataBufferCustomWriteMask, exp2(startBit + 3.0)));
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
                return output;
            }

            FragmentOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float maskEnabled = HasSystemChannel(1.0);
                float idEnabled = HasSystemChannel(2.0);
                float flagsEnabled = HasSystemChannel(4.0);
                float thicknessEnabled = HasSystemChannel(256.0);
                float curvatureEnabled = HasSystemChannel(512.0);
                float materialEnabled = HasSystemChannel(1024.0);
                float transmittanceHintEnabled = HasSystemChannel(2048.0);
                float subjectCoverage = saturate(_HoMetadataBufferMaskWeight);
                float subjectValid = step(0.0001, subjectCoverage);

                uint rendererUserValue = unity_RendererUserValue;
                bool hasRendererUserValue = rendererUserValue != 0u;
                uint objectCustomMask = hasRendererUserValue ? (rendererUserValue & 255u) : (uint)round(saturate(_HoMetadataBufferObjectCustomMask / 255.0) * 255.0);
                float effectiveGroupId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 8u) : _HoMetadataBufferGroupId;
                float effectiveObjectId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 16u) : _HoMetadataBufferObjectId;
                float effectiveFlags = hasRendererUserValue ? ByteToFloat(rendererUserValue, 24u) : _HoMetadataBufferFlags;

                FragmentOutput output;
                output.maskId = half4(
                    subjectCoverage * maskEnabled,
                    EncodeByte(effectiveGroupId) * idEnabled * subjectValid,
                    EncodeByte(effectiveObjectId) * idEnabled * subjectValid,
                    EncodeByte(effectiveFlags) * flagsEnabled * subjectValid);
                output.surfaceData = half4(
                    saturate(_HoMetadataBufferThickness) * thicknessEnabled * subjectValid,
                    saturate(abs(_HoMetadataBufferCurvature)) * curvatureEnabled * subjectValid,
                    EncodeScalar(_HoMetadataBufferMaterialClass) * materialEnabled * subjectValid,
                    saturate(_HoMetadataBufferTransmittanceHint) * transmittanceHintEnabled * subjectValid);
                output.custom0 = half4(ApplyCustomWriteMask(_HoMetadataBufferCustomValues0, 0.0) * subjectValid);
                output.objectCustom0 = half4(DecodeObjectCustom0(objectCustomMask) * subjectValid);
                output.objectCustom1 = half4(DecodeObjectCustom1(objectCustomMask) * subjectValid);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "MetadataBuffer RSUV Solid"
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragRsuvSolid

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _HoMetadataBufferMaskWeight;
            float _HoMetadataBufferSystemChannelMask;
            float _HoMetadataBufferSystemWriteMask;
            float _HoMetadataBufferGroupId;
            float _HoMetadataBufferObjectId;
            float _HoMetadataBufferFlags;
            float _HoMetadataBufferObjectCustomMask;
            float _HoMetadataBufferRsuvAssigned;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct RsuvSolidOutput
            {
                half4 maskId : SV_Target0;
                half4 objectCustom0 : SV_Target1;
                half4 objectCustom1 : SV_Target2;
            };

            float HasBit(float value, float bitValue)
            {
                return step(0.5, fmod(floor(value / bitValue), 2.0));
            }

            float HasSystemChannel(float bitValue)
            {
                return HasBit(_HoMetadataBufferSystemWriteMask, bitValue);
            }

            float EncodeByte(float value)
            {
                return saturate(round(clamp(value, 0.0, 255.0)) / 255.0);
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
                return output;
            }

            RsuvSolidOutput FragRsuvSolid(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                uint rendererUserValue = unity_RendererUserValue;
                bool hasRendererUserValue = rendererUserValue != 0u;
                uint objectCustomMask = hasRendererUserValue ? (rendererUserValue & 255u) : (uint)round(saturate(_HoMetadataBufferObjectCustomMask / 255.0) * 255.0);
                float effectiveGroupId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 8u) : _HoMetadataBufferGroupId;
                float effectiveObjectId = hasRendererUserValue ? ByteToFloat(rendererUserValue, 16u) : _HoMetadataBufferObjectId;
                float effectiveFlags = hasRendererUserValue ? ByteToFloat(rendererUserValue, 24u) : _HoMetadataBufferFlags;
                float subjectValid = step(0.0001, saturate(_HoMetadataBufferMaskWeight));
                float hasId = step(0.5, max(max(effectiveGroupId, effectiveObjectId), effectiveFlags));
                float hasRsuv = max(max((objectCustomMask != 0u) ? 1.0 : 0.0, hasId), step(0.5, _HoMetadataBufferRsuvAssigned));
                clip((hasRsuv > 0.5 && subjectValid > 0.5) ? 1.0 : -1.0);

                float maskEnabled = HasSystemChannel(1.0);
                float idEnabled = HasSystemChannel(2.0);
                float flagsEnabled = HasSystemChannel(4.0);

                RsuvSolidOutput output;
                output.maskId = half4(
                    subjectValid * maskEnabled,
                    EncodeByte(effectiveGroupId) * idEnabled * subjectValid,
                    EncodeByte(effectiveObjectId) * idEnabled * subjectValid,
                    EncodeByte(effectiveFlags) * flagsEnabled * subjectValid);
                output.objectCustom0 = half4(DecodeObjectCustom0(objectCustomMask) * subjectValid);
                output.objectCustom1 = half4(DecodeObjectCustom1(objectCustomMask) * subjectValid);
                return output;
            }
            ENDHLSL
        }

    }

    Fallback Off
}
