using lilToon.URP.Extensions.CharacterSpecialization;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    [CustomEditor(typeof(HoCharacterSpecializationRendererFeature))]
    internal sealed class HoCharacterSpecializationRendererFeatureEditor : UnityEditor.Editor
    {
        private static bool showRuntimeStatus = true;
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

            DrawRuntimeStatus();
            DrawFallbackSettings();
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawRuntimeStatus()
        {
            showRuntimeStatus = EditorGUILayout.Foldout(showRuntimeStatus, "运行状态", true);
            if (!showRuntimeStatus)
            {
                return;
            }

            HoCharacterSpecializationRuntimeDiagnosticSnapshot snapshot = HoCharacterSpecializationRuntimeDiagnostics.CurrentSnapshot;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!snapshot.IsValid)
                {
                    EditorGUILayout.HelpBox("尚未记录 Ho-CharacterSpecialization 运行帧。进入 Play Mode，或让使用该 RendererFeature 的 Scene/Game camera 渲染一帧。", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("帧", snapshot.FrameCount.ToString());
                EditorGUILayout.LabelField("相机", snapshot.CameraName);
                EditorGUILayout.LabelField("阶段", snapshot.Stage);
                EditorGUILayout.LabelField("Active Back Buffer", snapshot.BackBufferActive ? "是" : "否");
                EditorGUILayout.LabelField("Camera Color", FormatAvailable(snapshot.CameraColorAvailable));
                EditorGUILayout.LabelField("MetadataBuffer", FormatAvailable(snapshot.MetadataBufferAvailable));
                EditorGUILayout.LabelField("GeometryBuffer", FormatAvailable(snapshot.GeometryBufferAvailable));
                EditorGUILayout.LabelField("MaskId", FormatAvailable(snapshot.MetadataMaskIdAvailable));
                EditorGUILayout.LabelField("ObjectCustom0", FormatAvailable(snapshot.MetadataObjectCustom0Available));
                EditorGUILayout.LabelField("ObjectCustom1", FormatAvailable(snapshot.MetadataObjectCustom1Available));
                EditorGUILayout.LabelField("NormalDepth", FormatAvailable(snapshot.GeometryNormalDepthAvailable));

                EditorGUILayout.HelpBox(
                    snapshot.Ready
                        ? "角色特化输入有效：MetadataBuffer 与 GeometryBuffer 均可用。"
                        : "角色特化已跳过：" + snapshot.Reason,
                    snapshot.Ready ? MessageType.Info : MessageType.Warning);
            }
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

        private static string FormatAvailable(bool available)
        {
            return available ? "可用" : "缺失";
        }
    }
}
