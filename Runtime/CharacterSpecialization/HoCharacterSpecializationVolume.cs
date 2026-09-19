using System;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>把整块效果参数当成一个 Volume 参数承载（内含普通字段，Inspector 上是普通控件）。</summary>
    [Serializable]
    public sealed class HoCharacterSpecializationEffectsParameter : VolumeParameter<HoCharacterSpecializationEffects>
    {
        public HoCharacterSpecializationEffectsParameter(HoCharacterSpecializationEffects value, bool overrideState = false)
            : base(value, overrideState)
        {
        }
    }

    [Serializable]
    public sealed class HoCharacterLayerMaskParameter : VolumeParameter<LayerMask>
    {
        public HoCharacterLayerMaskParameter(LayerMask value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(LayerMask from, LayerMask to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterRenderPassEventParameter : VolumeParameter<RenderPassEvent>
    {
        public HoCharacterRenderPassEventParameter(RenderPassEvent value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(RenderPassEvent from, RenderPassEvent to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterRenderScaleParameter : VolumeParameter<HoCharacterRenderScale>
    {
        public HoCharacterRenderScaleParameter(HoCharacterRenderScale value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterRenderScale from, HoCharacterRenderScale to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterShadowBlendModeParameter : VolumeParameter<HoCharacterShadowBlendMode>
    {
        public HoCharacterShadowBlendModeParameter(HoCharacterShadowBlendMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterShadowBlendMode from, HoCharacterShadowBlendMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterFaceHairDiffuseBlendModeParameter : VolumeParameter<HoCharacterFaceHairDiffuseBlendMode>
    {
        public HoCharacterFaceHairDiffuseBlendModeParameter(HoCharacterFaceHairDiffuseBlendMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterFaceHairDiffuseBlendMode from, HoCharacterFaceHairDiffuseBlendMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterSubjectOutlineFillModeParameter : VolumeParameter<HoCharacterSubjectOutlineFillMode>
    {
        public HoCharacterSubjectOutlineFillModeParameter(HoCharacterSubjectOutlineFillMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterSubjectOutlineFillMode from, HoCharacterSubjectOutlineFillMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterSubjectOutlineHeightFadeModeParameter : VolumeParameter<HoCharacterSubjectOutlineHeightFadeMode>
    {
        public HoCharacterSubjectOutlineHeightFadeModeParameter(HoCharacterSubjectOutlineHeightFadeMode value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterSubjectOutlineHeightFadeMode from, HoCharacterSubjectOutlineHeightFadeMode to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

    [Serializable]
    public sealed class HoCharacterObjectCustomChannelParameter : VolumeParameter<HoCharacterObjectCustomChannel>
    {
        public HoCharacterObjectCustomChannelParameter(HoCharacterObjectCustomChannel value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(HoCharacterObjectCustomChannel from, HoCharacterObjectCustomChannel to, float t)
        {
            value = t > 0.0f ? to : from;
        }
    }

#if UNITY_2023_1_OR_NEWER
    [VolumeComponentMenu("Post-processing/Ho-CharacterSpecialization/角色特化"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
    [VolumeComponentMenuForRenderPipeline("Post-processing/Ho-CharacterSpecialization/角色特化", typeof(UniversalRenderPipeline))]
#endif
#if UNITY_2023_3_OR_NEWER
    [VolumeRequiresRendererFeatures(typeof(HoCharacterSpecializationRendererFeature))]
#endif
    [Serializable]
    public sealed class HoCharacterSpecializationVolume : VolumeComponent, IPostProcessComponent
    {
        public HoCharacterSpecializationVolume()
        {
#if !UNITY_6000_3_OR_NEWER
            displayName = "Ho-CharacterSpecialization 角色特化";
#endif
        }

        [Tooltip("效果参数。Volume 是唯一来源：没有 Settings 兜底值，也没有逐参数覆盖；要关掉某个效果就关它自己的开关。")]
        public HoCharacterSpecializationEffectsParameter Effects = new HoCharacterSpecializationEffectsParameter(new HoCharacterSpecializationEffects());

        public bool IsActive()
        {
            return active;
        }

        public bool IsActiveForCamera(CameraType cameraType)
        {
            if (!IsActive())
            {
                return false;
            }

            if (cameraType == CameraType.SceneView)
            {
                return true;
            }

            return cameraType == CameraType.Game;
        }

        public bool IsTileCompatible()
        {
            return false;
        }

        public void CopyEffectsTo(HoCharacterSpecializationSettings target)
        {
            if (target == null)
            {
                return;
            }

            target.eyeRevealEnabled = Effects.value.eyeRevealEnabled;
            target.semanticMaskBlurRadiusPixels = Effects.value.semanticMaskBlurRadiusPixels;
            target.semanticMaskBlurHairShadow = Effects.value.semanticMaskBlurHairShadow;
            target.semanticMaskBlurFaceHairDiffuse = Effects.value.semanticMaskBlurFaceHairDiffuse;
            target.semanticMaskBlurEyeReveal = Effects.value.semanticMaskBlurEyeReveal;
            target.semanticMaskBlurSubjectOutline = Effects.value.semanticMaskBlurSubjectOutline;
            target.semanticMaskBlurEnhancedOutline = Effects.value.semanticMaskBlurEnhancedOutline;
            target.eyeRevealStrength = Effects.value.eyeRevealStrength;
            target.eyeRevealFeatherPixels = Effects.value.eyeRevealFeatherPixels;
            target.eyeRevealDilationPixels = Effects.value.eyeRevealDilationPixels;
            target.eyeRevealDepthBias = Effects.value.eyeRevealDepthBias;
            target.useEyeRevealArea = Effects.value.useEyeRevealArea;
            target.sameCharacterOnly = Effects.value.sameCharacterOnly;
            target.eyeRevealAngleEnabled = Effects.value.eyeRevealAngleEnabled;
            target.eyeRevealAngleStrength = Effects.value.eyeRevealAngleStrength;
            target.eyeRevealAngleYawRangeDegrees = Effects.value.eyeRevealAngleYawRangeDegrees;
            target.eyeRevealAnglePitchRangeDegrees = Effects.value.eyeRevealAnglePitchRangeDegrees;
            target.eyeRevealAngleSoftnessDegrees = Effects.value.eyeRevealAngleSoftnessDegrees;
            target.hairDropShadowEnabled = Effects.value.hairDropShadowEnabled;
            target.hairShadowColor = Effects.value.hairShadowColor;
            target.hairShadowOpacity = Effects.value.hairShadowOpacity;
            target.hairShadowDistancePixels = Effects.value.hairShadowDistancePixels;
            target.hairShadowDistancePerspectiveStrength = Effects.value.hairShadowDistancePerspectiveStrength;
            target.hairShadowDistanceReferenceDepth = Effects.value.hairShadowDistanceReferenceDepth;
            target.hairShadowDistanceMinScale = Effects.value.hairShadowDistanceMinScale;
            target.hairShadowAngleDegrees = Effects.value.hairShadowAngleDegrees;
            target.hairShadowSoftnessPixels = Effects.value.hairShadowSoftnessPixels;
            target.hairShadowSpreadPixels = Effects.value.hairShadowSpreadPixels;
            target.hairShadowBlendMode = Effects.value.hairShadowBlendMode;
            target.faceHairDiffuseEnabled = Effects.value.faceHairDiffuseEnabled;
            target.faceHairDiffuseStrength = Effects.value.faceHairDiffuseStrength;
            target.faceHairDiffuseRadiusPixels = Effects.value.faceHairDiffuseRadiusPixels;
            target.faceHairDiffuseDepthTolerance = Effects.value.faceHairDiffuseDepthTolerance;
            target.faceHairDiffuseLevelBlack = Effects.value.faceHairDiffuseLevelBlack;
            target.faceHairDiffuseLevelWhite = Effects.value.faceHairDiffuseLevelWhite;
            target.faceHairDiffuseTintColor = Effects.value.faceHairDiffuseTintColor;
            target.faceHairDiffuseBlendMode = Effects.value.faceHairDiffuseBlendMode;
            target.subjectOutlineEnabled = Effects.value.subjectOutlineEnabled;
            target.subjectOutlineStrength = Effects.value.subjectOutlineStrength;
            target.subjectOutlineRadiusPixels = Effects.value.subjectOutlineRadiusPixels;
            target.subjectOutlineLevelBlack = Effects.value.subjectOutlineLevelBlack;
            target.subjectOutlineLevelWhite = Effects.value.subjectOutlineLevelWhite;
            target.subjectOutlineColor = Effects.value.subjectOutlineColor;
            target.subjectOutlineFillMode = Effects.value.subjectOutlineFillMode;
            target.subjectOutlineNormalRotationDegrees = Effects.value.subjectOutlineNormalRotationDegrees;
            target.subjectOutlineNormalFlowDegreesPerSecond = Effects.value.subjectOutlineNormalFlowDegreesPerSecond;
            target.subjectOutlineFogColor = Effects.value.subjectOutlineFogColor;
            target.subjectOutlineFogHueShiftDegrees = Effects.value.subjectOutlineFogHueShiftDegrees;
            target.subjectOutlineFogSaturation = Effects.value.subjectOutlineFogSaturation;
            target.subjectOutlineFogValue = Effects.value.subjectOutlineFogValue;
            target.subjectOutlineFogSoftness = Effects.value.subjectOutlineFogSoftness;
            target.subjectOutlineHeightFadeMode = Effects.value.subjectOutlineHeightFadeMode;
            target.subjectOutlineHeightFadeGroundY = Effects.value.subjectOutlineHeightFadeGroundY;
            target.subjectOutlineHeightFadeStart = Effects.value.subjectOutlineHeightFadeStart;
            target.subjectOutlineHeightFadeEnd = Effects.value.subjectOutlineHeightFadeEnd;
            target.subjectOutlineHeightFadeHardness = Effects.value.subjectOutlineHeightFadeHardness;
            target.enhancedOutlineEnabled = Effects.value.enhancedOutlineEnabled;
            target.enhancedOutlineSourceChannel = Effects.value.enhancedOutlineSourceChannel;
            target.enhancedOutlineStrength = Effects.value.enhancedOutlineStrength;
            target.enhancedOutlineRadiusPixels = Effects.value.enhancedOutlineRadiusPixels;
            target.enhancedOutlineFogColor = Effects.value.enhancedOutlineFogColor;
            target.enhancedOutlineFogHueShiftDegrees = Effects.value.enhancedOutlineFogHueShiftDegrees;
            target.enhancedOutlineFogSaturation = Effects.value.enhancedOutlineFogSaturation;
            target.enhancedOutlineFogValue = Effects.value.enhancedOutlineFogValue;
            target.enhancedOutlineFogSoftness = Effects.value.enhancedOutlineFogSoftness;
            target.enhancedOutlineHeightFadeMode = Effects.value.enhancedOutlineHeightFadeMode;
            target.enhancedOutlineHeightFadeGroundY = Effects.value.enhancedOutlineHeightFadeGroundY;
            target.enhancedOutlineHeightFadeStart = Effects.value.enhancedOutlineHeightFadeStart;
            target.enhancedOutlineHeightFadeEnd = Effects.value.enhancedOutlineHeightFadeEnd;
            target.enhancedOutlineHeightFadeHardness = Effects.value.enhancedOutlineHeightFadeHardness;
        }
    }
}
