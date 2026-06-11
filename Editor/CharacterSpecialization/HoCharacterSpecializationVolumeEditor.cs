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
        private static readonly Color SettingsColor = new Color(0.45f, 0.64f, 0.96f);
        private static readonly Color CaptureColor = new Color(0.42f, 0.72f, 0.58f);

        private static bool showSettings;
        private static bool showCapture;

        private SerializedDataParameter enable;
        private SerializedDataParameter showInSceneView;
        private SerializedDataParameter layerMask;
        private SerializedDataParameter minRenderQueue;
        private SerializedDataParameter maxRenderQueue;
        private SerializedDataParameter passEvent;
        private SerializedDataParameter renderScale;
        private SerializedDataParameter eyeRevealEnabled;
        private SerializedDataParameter eyeRevealStrength;
        private SerializedDataParameter eyeRevealFeatherPixels;
        private SerializedDataParameter eyeRevealDilationPixels;
        private SerializedDataParameter eyeRevealDepthBias;
        private SerializedDataParameter useEyeRevealArea;
        private SerializedDataParameter sameCharacterOnly;
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

        public override void OnEnable()
        {
            PropertyFetcher<HoCharacterSpecializationVolume> fetcher = new PropertyFetcher<HoCharacterSpecializationVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layerMask = Unpack(fetcher.Find(x => x.LayerMask));
            minRenderQueue = Unpack(fetcher.Find(x => x.MinRenderQueue));
            maxRenderQueue = Unpack(fetcher.Find(x => x.MaxRenderQueue));
            passEvent = Unpack(fetcher.Find(x => x.PassEvent));
            renderScale = Unpack(fetcher.Find(x => x.RenderScale));
            eyeRevealEnabled = Unpack(fetcher.Find(x => x.EyeRevealEnabled));
            eyeRevealStrength = Unpack(fetcher.Find(x => x.EyeRevealStrength));
            eyeRevealFeatherPixels = Unpack(fetcher.Find(x => x.EyeRevealFeatherPixels));
            eyeRevealDilationPixels = Unpack(fetcher.Find(x => x.EyeRevealDilationPixels));
            eyeRevealDepthBias = Unpack(fetcher.Find(x => x.EyeRevealDepthBias));
            useEyeRevealArea = Unpack(fetcher.Find(x => x.UseEyeRevealArea));
            sameCharacterOnly = Unpack(fetcher.Find(x => x.SameCharacterOnly));
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
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Renderer Data 里先添加 HoCharacter Specialization RendererFeature；然后在全局或局部 Volume 里添加本组件并启用。Face、FrontHair、Eye、EyeRevealArea 需要由 HoMetadataBufferGroup/RSUV 或材质 fallback 标记提供。",
                MessageType.Info);

            DrawSettings();
            DrawCapture();
            HoCharacterEyeRevealEditorSection.DrawVolume(
                eyeRevealEnabled,
                eyeRevealStrength,
                eyeRevealFeatherPixels,
                eyeRevealDilationPixels,
                eyeRevealDepthBias,
                useEyeRevealArea,
                sameCharacterOnly,
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
                DrawDataParameter);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            string summary = LilUrpEditorSectionGui.BoolSummary(enable);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSettings, "体积设置", summary, SettingsColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(enable, "启用");
                DrawParameter(showInSceneView, "场景视图");
            }
        }

        private void DrawCapture()
        {
            string summary = LilUrpEditorSectionGui.IntSummary(minRenderQueue) + "-" + LilUrpEditorSectionGui.IntSummary(maxRenderQueue);
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCapture, "捕获范围", summary, CaptureColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawParameter(layerMask, "图层遮罩");
                DrawParameter(minRenderQueue, "最小渲染队列");
                DrawParameter(maxRenderQueue, "最大渲染队列");
                DrawParameter(passEvent, "渲染时机");
                DrawParameter(renderScale, "渲染缩放");
            }
        }

        private void DrawParameter(SerializedDataParameter parameter, string label)
        {
            if (parameter != null)
            {
                PropertyField(parameter, new GUIContent(label));
            }
        }

        private void DrawDataParameter(SerializedDataParameter parameter, GUIContent label)
        {
            PropertyField(parameter, label);
        }
    }
}
