using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterSpecializationRendererFeature))]
    internal sealed class HoCharacterSpecializationRendererFeatureEditor : UnityEditor.Editor
    {
        private static bool showFallbackSettings;
        private SerializedProperty useVolumesProperty;
        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            useVolumesProperty = serializedObject.FindProperty("UseVolumes");
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "这个 RendererFeature 负责把 HoCharacter 捕获/合成 pass 安装进当前 Renderer。推荐勾选“使用 Volume 参数”，然后在场景或全局 Volume 里添加“Ho-CharacterSpecialization/角色特化”来调眼睛透过和前发投影。",
                MessageType.Info);

            if (useVolumesProperty != null)
            {
                EditorGUILayout.PropertyField(useVolumesProperty);
            }

            if (useVolumesProperty == null || useVolumesProperty.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Volume 模式下，这里只保留默认值/兜底资源。日常不要改 Render Asset；请到 Volume 里调参数。",
                    MessageType.None);
            }

            DrawFallbackSettings();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFallbackSettings()
        {
            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                return;
            }

            showFallbackSettings = EditorGUILayout.Foldout(showFallbackSettings, "默认/兜底设置", true);
            if (!showFallbackSettings)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawProperty("enabled");
            DrawProperty("layerMask");
            DrawProperty("minRenderQueue");
            DrawProperty("maxRenderQueue");
            DrawProperty("passEvent");
            DrawProperty("renderScale");
            DrawProperty("compositeShader");

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("眼睛透过默认值", EditorStyles.boldLabel);
            DrawProperty("eyeRevealEnabled");
            DrawProperty("eyeRevealStrength");
            DrawProperty("eyeRevealFeatherPixels");
            DrawProperty("eyeRevealDilationPixels");
            DrawProperty("eyeRevealDepthBias");
            DrawProperty("useEyeRevealArea");
            DrawProperty("sameCharacterOnly");

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("前发投影默认值", EditorStyles.boldLabel);
            DrawProperty("hairDropShadowEnabled");
            DrawProperty("hairShadowColor");
            DrawProperty("hairShadowOpacity");
            DrawProperty("hairShadowDistancePixels");
            DrawProperty("hairShadowDistancePerspectiveStrength");
            DrawProperty("hairShadowDistanceReferenceDepth");
            DrawProperty("hairShadowDistanceMinScale");
            DrawProperty("hairShadowAngleDegrees");
            DrawProperty("hairShadowSoftnessPixels");
            DrawProperty("hairShadowSpreadPixels");
            DrawProperty("hairShadowKeepOffHair");
            DrawProperty("hairShadowBlendMode");

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField("调试/预留", EditorStyles.boldLabel);
            DrawProperty("debugMode");
            DrawProperty("farPlaneShadowReserved");
            DrawProperty("reflectionSpaceReserved");
            EditorGUI.indentLevel--;
        }

        private void DrawProperty(string relativeName)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property);
            }
        }
    }
}
