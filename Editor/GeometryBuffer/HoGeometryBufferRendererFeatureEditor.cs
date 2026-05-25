using lilToon.URP.Extensions.GeometryBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryBuffer
{
    [CustomEditor(typeof(HoGeometryBufferRendererFeature))]
    internal sealed class HoGeometryBufferRendererFeatureEditor : UnityEditor.Editor
    {
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

            DrawProperty("enabled");
            DrawProperty("layerMask");
            DrawProperty("minRenderQueue");
            DrawProperty("maxRenderQueue");
            DrawProperty("renderScale");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug Preview", EditorStyles.boldLabel);
            DrawProperty("debugMode");
            DrawProperty("debugInSceneView");
            DrawProperty("debugInGameView");
            DrawProperty("debugDepthNear");
            DrawProperty("debugDepthFar");
            DrawDebugInteractionNotice();

            EditorGUILayout.Space(6);
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawProperty("passEvent");
                DrawProperty("debugPassEvent");
                DrawProperty("fallbackShader");
                DrawProperty("debugShader");
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProperty(string relativeName)
        {
            SerializedProperty property = settingsProperty.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property);
            }
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
