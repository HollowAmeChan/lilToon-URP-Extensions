using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.GeometryBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryBuffer
{
    [CustomEditor(typeof(HoGeometryBufferRendererFeature))]
    internal sealed class HoGeometryBufferRendererFeatureEditor : UnityEditor.Editor
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
                "GeometryBuffer writes world normal and linear depth for screen-space semantic effects. Add it before ScreenProcess, SSS, and CharacterSpecialization consumers.",
                MessageType.Info);

            DrawRuntime();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            SerializedProperty renderScale = Find("renderScale");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled) + " / " + LilUrpEditorSectionGui.EnumName(renderScale);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("enabled");
                DrawProperty("layerMask");
                DrawProperty("minRenderQueue");
                DrawProperty("maxRenderQueue");
                DrawProperty("renderScale");
                DrawProperty("enableSkyBuffer");
                DrawProperty("skyRenderScale");
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
                DrawProperty("debugDepthNear");
                DrawProperty("debugDepthFar");
                DrawDebugInteractionNotice();
            }
        }

        private void DrawAdvanced()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvancedSettings, "高级", "时机 / Shader", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("passEvent");
                DrawProperty("skyPassEvent");
                DrawProperty("debugPassEvent");
                DrawProperty("fallbackShader");
                DrawProperty("skyCaptureShader");
                DrawProperty("debugShader");
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

        private void DrawDebugInteractionNotice()
        {
            SerializedProperty debugMode = settingsProperty.FindPropertyRelative("debugMode");
            if (debugMode == null || debugMode.enumValueIndex == (int)HoGeometryBufferDebugMode.Off)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "The debug preview writes to the current view color. If ScreenProcess or ImageProcess is also active in Scene View, those passes can still process the preview image.",
                MessageType.Info);
        }
    }
}
