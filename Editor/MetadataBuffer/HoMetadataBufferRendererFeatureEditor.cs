using lilToon.URP.Extensions.MetadataBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.MetadataBuffer
{
    [CustomEditor(typeof(HoMetadataBufferRendererFeature))]
    internal sealed class HoMetadataBufferRendererFeatureEditor : UnityEditor.Editor
    {
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
            DrawProperty("enabled");
            DrawProperty("layerMask");
            DrawProperty("minRenderQueue");
            DrawProperty("maxRenderQueue");
            DrawProperty("renderScale");
            DrawProperty("systemChannels");
            DrawProperty("customChannelCount");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug Preview", EditorStyles.boldLabel);
            DrawProperty("debugMode");
            DrawProperty("debugInSceneView");
            DrawProperty("debugInGameView");
            DrawProperty("debugDepthNear");
            DrawProperty("debugDepthFar");
            DrawDebugInteractionNotice();

            EditorGUILayout.Space(6);
            showCustomChannels = EditorGUILayout.Foldout(showCustomChannels, "Custom Channel Names / Colors", true);
            if (showCustomChannels)
            {
                EditorGUI.indentLevel++;
                DrawProperty("customChannelNames", true);
                DrawProperty("customChannelColors", true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(6);
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawProperty("passEvent");
                DrawProperty("debugPassEvent");
                DrawProperty("useFallbackMaterial");
                DrawProperty("fallbackShader");
                DrawProperty("debugShader");
                EditorGUI.indentLevel--;
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
