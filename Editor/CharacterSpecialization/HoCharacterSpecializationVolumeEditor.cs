using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterSpecializationVolume))]
    internal sealed class HoCharacterSpecializationVolumeEditor : VolumeComponentEditor
    {
        private SerializedDataParameter eyeRevealEnabled;
        private SerializedDataParameter eyeRevealStrength;
        private SerializedDataParameter eyeRevealFeatherPixels;
        private SerializedDataParameter eyeRevealDilationPixels;
        private SerializedDataParameter eyeRevealDepthBias;
        private SerializedDataParameter useEyeRevealArea;
        private SerializedDataParameter sameCharacterOnly;
        private SerializedDataParameter eyeRevealAngleEnabled;
        private SerializedDataParameter eyeRevealAngleStrength;
        private SerializedDataParameter eyeRevealAngleYawRangeDegrees;
        private SerializedDataParameter eyeRevealAnglePitchRangeDegrees;
        private SerializedDataParameter eyeRevealAngleSoftnessDegrees;
        private SerializedDataParameter hairDropShadowEnabled;
        private SerializedDataParameter hairShadowColor;
        private SerializedDataParameter hairShadowOpacity;
        private SerializedDataParameter hairShadowDistancePixels;
        private SerializedDataParameter hairShadowDistancePerspectiveStrength;
        private SerializedDataParameter hairShadowDistanceReferenceDepth;
        private SerializedDataParameter hairShadowDistanceMinScale;
        private SerializedDataParameter hairShadowAngleDegrees;
        private SerializedDataParameter hairShadowSoftnessPixels;
        private SerializedDataParameter hairShadowSpreadPixels;
        private SerializedDataParameter hairShadowKeepOffHair;
        private SerializedDataParameter hairShadowBlendMode;
        private SerializedDataParameter faceHairDiffuseEnabled;
        private SerializedDataParameter faceHairDiffuseStrength;
        private SerializedDataParameter faceHairDiffuseRadiusPixels;
        private SerializedDataParameter faceHairDiffuseDepthTolerance;
        private SerializedDataParameter faceHairDiffuseLevelBlack;
        private SerializedDataParameter faceHairDiffuseLevelWhite;
        private SerializedDataParameter faceHairDiffuseTintColor;
        private SerializedDataParameter faceHairDiffuseBlendMode;
        private SerializedDataParameter subjectOutlineEnabled;
        private SerializedDataParameter subjectOutlineStrength;
        private SerializedDataParameter subjectOutlineRadiusPixels;
        private SerializedDataParameter subjectOutlineLevelBlack;
        private SerializedDataParameter subjectOutlineLevelWhite;
        private SerializedDataParameter subjectOutlineColor;
        private SerializedDataParameter subjectOutlineFillMode;
        private SerializedDataParameter subjectOutlineNormalRotationDegrees;
        private SerializedDataParameter subjectOutlineNormalFlowDegreesPerSecond;
        private SerializedDataParameter subjectOutlineFogColor;
        private SerializedDataParameter subjectOutlineFogHueShiftDegrees;
        private SerializedDataParameter subjectOutlineFogSaturation;
        private SerializedDataParameter subjectOutlineFogValue;
        private SerializedDataParameter subjectOutlineFogSoftness;
        private SerializedDataParameter subjectOutlineHeightFadeMode;
        private SerializedDataParameter subjectOutlineHeightFadeGroundY;
        private SerializedDataParameter subjectOutlineHeightFadeStart;
        private SerializedDataParameter subjectOutlineHeightFadeEnd;
        private SerializedDataParameter subjectOutlineHeightFadeHardness;
        private SerializedDataParameter enhancedOutlineEnabled;
        private SerializedDataParameter enhancedOutlineSourceChannel;
        private SerializedDataParameter enhancedOutlineStrength;
        private SerializedDataParameter enhancedOutlineRadiusPixels;
        private SerializedDataParameter enhancedOutlineFogColor;
        private SerializedDataParameter enhancedOutlineFogHueShiftDegrees;
        private SerializedDataParameter enhancedOutlineFogSaturation;
        private SerializedDataParameter enhancedOutlineFogValue;
        private SerializedDataParameter enhancedOutlineFogSoftness;
        private SerializedDataParameter enhancedOutlineHeightFadeMode;
        private SerializedDataParameter enhancedOutlineHeightFadeGroundY;
        private SerializedDataParameter enhancedOutlineHeightFadeStart;
        private SerializedDataParameter enhancedOutlineHeightFadeEnd;
        private SerializedDataParameter enhancedOutlineHeightFadeHardness;

        public override void OnEnable()
        {
            PropertyFetcher<HoCharacterSpecializationVolume> fetcher = new PropertyFetcher<HoCharacterSpecializationVolume>(serializedObject);
            eyeRevealEnabled = Unpack(fetcher.Find(x => x.EyeRevealEnabled));
            eyeRevealStrength = Unpack(fetcher.Find(x => x.EyeRevealStrength));
            eyeRevealFeatherPixels = Unpack(fetcher.Find(x => x.EyeRevealFeatherPixels));
            eyeRevealDilationPixels = Unpack(fetcher.Find(x => x.EyeRevealDilationPixels));
            eyeRevealDepthBias = Unpack(fetcher.Find(x => x.EyeRevealDepthBias));
            useEyeRevealArea = Unpack(fetcher.Find(x => x.UseEyeRevealArea));
            sameCharacterOnly = Unpack(fetcher.Find(x => x.SameCharacterOnly));
            eyeRevealAngleEnabled = Unpack(fetcher.Find(x => x.EyeRevealAngleEnabled));
            eyeRevealAngleStrength = Unpack(fetcher.Find(x => x.EyeRevealAngleStrength));
            eyeRevealAngleYawRangeDegrees = Unpack(fetcher.Find(x => x.EyeRevealAngleYawRangeDegrees));
            eyeRevealAnglePitchRangeDegrees = Unpack(fetcher.Find(x => x.EyeRevealAnglePitchRangeDegrees));
            eyeRevealAngleSoftnessDegrees = Unpack(fetcher.Find(x => x.EyeRevealAngleSoftnessDegrees));
            hairDropShadowEnabled = Unpack(fetcher.Find(x => x.HairDropShadowEnabled));
            hairShadowColor = Unpack(fetcher.Find(x => x.HairShadowColor));
            hairShadowOpacity = Unpack(fetcher.Find(x => x.HairShadowOpacity));
            hairShadowDistancePixels = Unpack(fetcher.Find(x => x.HairShadowDistancePixels));
            hairShadowDistancePerspectiveStrength = Unpack(fetcher.Find(x => x.HairShadowDistancePerspectiveStrength));
            hairShadowDistanceReferenceDepth = Unpack(fetcher.Find(x => x.HairShadowDistanceReferenceDepth));
            hairShadowDistanceMinScale = Unpack(fetcher.Find(x => x.HairShadowDistanceMinScale));
            hairShadowAngleDegrees = Unpack(fetcher.Find(x => x.HairShadowAngleDegrees));
            hairShadowSoftnessPixels = Unpack(fetcher.Find(x => x.HairShadowSoftnessPixels));
            hairShadowSpreadPixels = Unpack(fetcher.Find(x => x.HairShadowSpreadPixels));
            hairShadowKeepOffHair = Unpack(fetcher.Find(x => x.HairShadowKeepOffHair));
            hairShadowBlendMode = Unpack(fetcher.Find(x => x.HairShadowBlendMode));
            faceHairDiffuseEnabled = Unpack(fetcher.Find(x => x.FaceHairDiffuseEnabled));
            faceHairDiffuseStrength = Unpack(fetcher.Find(x => x.FaceHairDiffuseStrength));
            faceHairDiffuseRadiusPixels = Unpack(fetcher.Find(x => x.FaceHairDiffuseRadiusPixels));
            faceHairDiffuseDepthTolerance = Unpack(fetcher.Find(x => x.FaceHairDiffuseDepthTolerance));
            faceHairDiffuseLevelBlack = Unpack(fetcher.Find(x => x.FaceHairDiffuseLevelBlack));
            faceHairDiffuseLevelWhite = Unpack(fetcher.Find(x => x.FaceHairDiffuseLevelWhite));
            faceHairDiffuseTintColor = Unpack(fetcher.Find(x => x.FaceHairDiffuseTintColor));
            faceHairDiffuseBlendMode = Unpack(fetcher.Find(x => x.FaceHairDiffuseBlendMode));
            subjectOutlineEnabled = Unpack(fetcher.Find(x => x.SubjectOutlineEnabled));
            subjectOutlineStrength = Unpack(fetcher.Find(x => x.SubjectOutlineStrength));
            subjectOutlineRadiusPixels = Unpack(fetcher.Find(x => x.SubjectOutlineRadiusPixels));
            subjectOutlineLevelBlack = Unpack(fetcher.Find(x => x.SubjectOutlineLevelBlack));
            subjectOutlineLevelWhite = Unpack(fetcher.Find(x => x.SubjectOutlineLevelWhite));
            subjectOutlineColor = Unpack(fetcher.Find(x => x.SubjectOutlineColor));
            subjectOutlineFillMode = Unpack(fetcher.Find(x => x.SubjectOutlineFillMode));
            subjectOutlineNormalRotationDegrees = Unpack(fetcher.Find(x => x.SubjectOutlineNormalRotationDegrees));
            subjectOutlineNormalFlowDegreesPerSecond = Unpack(fetcher.Find(x => x.SubjectOutlineNormalFlowDegreesPerSecond));
            subjectOutlineFogColor = Unpack(fetcher.Find(x => x.SubjectOutlineFogColor));
            subjectOutlineFogHueShiftDegrees = Unpack(fetcher.Find(x => x.SubjectOutlineFogHueShiftDegrees));
            subjectOutlineFogSaturation = Unpack(fetcher.Find(x => x.SubjectOutlineFogSaturation));
            subjectOutlineFogValue = Unpack(fetcher.Find(x => x.SubjectOutlineFogValue));
            subjectOutlineFogSoftness = Unpack(fetcher.Find(x => x.SubjectOutlineFogSoftness));
            subjectOutlineHeightFadeMode = Unpack(fetcher.Find(x => x.SubjectOutlineHeightFadeMode));
            subjectOutlineHeightFadeGroundY = Unpack(fetcher.Find(x => x.SubjectOutlineHeightFadeGroundY));
            subjectOutlineHeightFadeStart = Unpack(fetcher.Find(x => x.SubjectOutlineHeightFadeStart));
            subjectOutlineHeightFadeEnd = Unpack(fetcher.Find(x => x.SubjectOutlineHeightFadeEnd));
            subjectOutlineHeightFadeHardness = Unpack(fetcher.Find(x => x.SubjectOutlineHeightFadeHardness));
            enhancedOutlineEnabled = Unpack(fetcher.Find(x => x.EnhancedOutlineEnabled));
            enhancedOutlineSourceChannel = Unpack(fetcher.Find(x => x.EnhancedOutlineSourceChannel));
            enhancedOutlineStrength = Unpack(fetcher.Find(x => x.EnhancedOutlineStrength));
            enhancedOutlineRadiusPixels = Unpack(fetcher.Find(x => x.EnhancedOutlineRadiusPixels));
            enhancedOutlineFogColor = Unpack(fetcher.Find(x => x.EnhancedOutlineFogColor));
            enhancedOutlineFogHueShiftDegrees = Unpack(fetcher.Find(x => x.EnhancedOutlineFogHueShiftDegrees));
            enhancedOutlineFogSaturation = Unpack(fetcher.Find(x => x.EnhancedOutlineFogSaturation));
            enhancedOutlineFogValue = Unpack(fetcher.Find(x => x.EnhancedOutlineFogValue));
            enhancedOutlineFogSoftness = Unpack(fetcher.Find(x => x.EnhancedOutlineFogSoftness));
            enhancedOutlineHeightFadeMode = Unpack(fetcher.Find(x => x.EnhancedOutlineHeightFadeMode));
            enhancedOutlineHeightFadeGroundY = Unpack(fetcher.Find(x => x.EnhancedOutlineHeightFadeGroundY));
            enhancedOutlineHeightFadeStart = Unpack(fetcher.Find(x => x.EnhancedOutlineHeightFadeStart));
            enhancedOutlineHeightFadeEnd = Unpack(fetcher.Find(x => x.EnhancedOutlineHeightFadeEnd));
            enhancedOutlineHeightFadeHardness = Unpack(fetcher.Find(x => x.EnhancedOutlineHeightFadeHardness));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Renderer Data 里先添加 HoCharacter Specialization RendererFeature；然后在全局或局部 Volume 里添加本组件并启用。Face、FrontHair、Eye、EyeRevealArea 需要由 HoMetadataBufferGroup/RSUV 或材质 fallback 标记提供。",
                MessageType.Info);

            HoCharacterEyeRevealEditorSection.DrawVolume(
                eyeRevealEnabled,
                eyeRevealStrength,
                eyeRevealFeatherPixels,
                eyeRevealDilationPixels,
                eyeRevealDepthBias,
                useEyeRevealArea,
                sameCharacterOnly,
                eyeRevealAngleEnabled,
                eyeRevealAngleStrength,
                eyeRevealAngleYawRangeDegrees,
                eyeRevealAnglePitchRangeDegrees,
                eyeRevealAngleSoftnessDegrees,
                DrawDataParameter);
            HoCharacterDropShadowEditorSection.DrawVolume(
                hairDropShadowEnabled,
                hairShadowColor,
                hairShadowOpacity,
                hairShadowDistancePixels,
                hairShadowDistancePerspectiveStrength,
                hairShadowDistanceReferenceDepth,
                hairShadowDistanceMinScale,
                hairShadowAngleDegrees,
                hairShadowSoftnessPixels,
                hairShadowSpreadPixels,
                hairShadowKeepOffHair,
                hairShadowBlendMode,
                DrawDataParameter);
            HoCharacterFaceHairDiffuseEditorSection.DrawVolume(
                faceHairDiffuseEnabled,
                faceHairDiffuseStrength,
                faceHairDiffuseRadiusPixels,
                faceHairDiffuseDepthTolerance,
                faceHairDiffuseLevelBlack,
                faceHairDiffuseLevelWhite,
                faceHairDiffuseTintColor,
                faceHairDiffuseBlendMode,
                DrawDataParameter);
            HoCharacterSubjectOutlineEditorSection.DrawVolume(
                subjectOutlineEnabled,
                subjectOutlineStrength,
                subjectOutlineRadiusPixels,
                subjectOutlineLevelBlack,
                subjectOutlineLevelWhite,
                subjectOutlineColor,
                subjectOutlineFillMode,
                subjectOutlineNormalRotationDegrees,
                subjectOutlineNormalFlowDegreesPerSecond,
                subjectOutlineFogColor,
                subjectOutlineFogHueShiftDegrees,
                subjectOutlineFogSaturation,
                subjectOutlineFogValue,
                subjectOutlineFogSoftness,
                subjectOutlineHeightFadeMode,
                subjectOutlineHeightFadeGroundY,
                subjectOutlineHeightFadeStart,
                subjectOutlineHeightFadeEnd,
                subjectOutlineHeightFadeHardness,
                DrawDataParameter);
            HoCharacterEnhancedOutlineEditorSection.DrawVolume(
                enhancedOutlineEnabled,
                enhancedOutlineSourceChannel,
                enhancedOutlineStrength,
                enhancedOutlineRadiusPixels,
                enhancedOutlineFogColor,
                enhancedOutlineFogHueShiftDegrees,
                enhancedOutlineFogSaturation,
                enhancedOutlineFogValue,
                enhancedOutlineFogSoftness,
                enhancedOutlineHeightFadeMode,
                enhancedOutlineHeightFadeGroundY,
                enhancedOutlineHeightFadeStart,
                enhancedOutlineHeightFadeEnd,
                enhancedOutlineHeightFadeHardness,
                DrawDataParameter);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDataParameter(SerializedDataParameter parameter, GUIContent label)
        {
            PropertyField(parameter, label);
        }
    }
}
