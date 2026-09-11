using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GTAO
{
    [CustomEditor(typeof(HoGTAORendererFeature))]
    internal sealed class HoGTAORendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime;
        private static bool showAdvancedSettings;
        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (settingsProperty == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.HelpBox(
                "Ho-GTAO transport validation reads the Ho-GeometryBuffer normal/depth resource and publishes the shared AO semantic. This validation build runs after Ho-GeometryBuffer; move both features before opaque rendering when the consumer path is enabled.",
                MessageType.Info);

            DrawRuntime();
            DrawAdvanced();
            EditorGUILayout.HelpBox("调试模式、Scene/Game View 开关和 AO Debug Pow 已移至 Ho-GTAO Volume 的“调试”分组。", MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            SerializedProperty quality = Find("quality");
            SerializedProperty resolution = Find("resolution");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled)
                + " / " + LilUrpEditorSectionGui.EnumName(quality)
                + " / " + LilUrpEditorSectionGui.EnumName(resolution);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("enabled");
                DrawProperty("quality");
                DrawProperty("resolution");
                DrawProperty("sliceCount");
                DrawProperty("stepCount");
            }
        }

        private void DrawAdvanced()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvancedSettings, "高级", "追踪 / 去噪 / 时机", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("worldSpaceRadius");
                DrawProperty("screenSpaceRadius");
                DrawProperty("thickness");
                DrawProperty("useAttenuation");
                DrawProperty("useLinearThickness");
                DrawProperty("temporalFrameCount");
                DrawProperty("temporalRejection");
                DrawProperty("spatialFilter");
                DrawProperty("filterRadius");
                DrawProperty("filterAdaptivity");
                DrawProperty("boxPassCount");
                EditorGUILayout.HelpBox("HoAO 必须在不透明物体绘制前生成；旧资产的 AfterRenderingOpaques 会在运行时自动改为 BeforeRenderingOpaques。", MessageType.Warning);
                DrawProperty("passEvent");
                DrawProperty("shader");
            }
        }

        private void DrawProperty(string relativeName)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property);
            }
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }
    }
}
