using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MetadataBuffer
{
    [CustomEditor(typeof(HoMetadataBufferRendererFeature))]
    internal sealed class HoMetadataBufferRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color ChannelColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime;
        private static bool showDebug;
        private static bool showAdvancedSettings;
        private static bool showCustomChannels;
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
                "MetadataBuffer writes surface, material, object, and currently shared SSS source data. Add GeometryBuffer separately for normal/depth consumers.",
                MessageType.Info);

            DrawSettings();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSettings()
        {
            DrawRuntime();
            DrawDebug();
            DrawCustomChannels();
            DrawAdvanced();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            SerializedProperty renderScale = Find("renderScale");
            SerializedProperty customChannelCount = Find("customChannelCount");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled) + " / " + LilUrpEditorSectionGui.EnumName(renderScale) + " / C" + LilUrpEditorSectionGui.IntSummary(customChannelCount);

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
                DrawProperty("systemChannels");
                DrawProperty("customChannelCount");
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
                DrawDebugInteractionNotice();
            }
        }

        private void DrawCustomChannels()
        {
            SerializedProperty customChannelCount = Find("customChannelCount");
            string summary = "C" + LilUrpEditorSectionGui.IntSummary(customChannelCount);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCustomChannels, "Custom Channels", summary, ChannelColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("customChannelNames", true);
                DrawProperty("customChannelColors", true);
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
                DrawProperty("debugPassEvent");
                DrawProperty("useFallbackMaterial");
                DrawProperty("fallbackShader");
                DrawProperty("debugShader");
            }
        }

        private void DrawProperty(string relativeName, bool includeChildren = false)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty?.FindPropertyRelative(relativeName);
        }

        private void DrawDebugInteractionNotice()
        {
            SerializedProperty debugMode = settingsProperty.FindPropertyRelative("debugMode");
            if (debugMode == null || debugMode.enumValueIndex == (int)HoMetadataBufferDebugMode.Off)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "The debug preview writes to the current view color. If ScreenProcess or ImageProcess is also active in Scene View, those passes can still process the preview image.",
                MessageType.Info);
        }
    }
}
