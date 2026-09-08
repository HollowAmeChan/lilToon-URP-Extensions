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
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime;
        private static bool showDebug;
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
            DrawDebug();

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
                DrawProperty("passEvent");
                DrawProperty("shader");
            }
        }

        private void DrawDebug()
        {
            SerializedProperty debugMode = Find("debugMode");
            string summary = LilUrpEditorSectionGui.EnumName(debugMode);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("debugMode");
                DrawProperty("debugInSceneView");
                DrawProperty("debugInGameView");
                DrawProperty("debugIntensity");
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
