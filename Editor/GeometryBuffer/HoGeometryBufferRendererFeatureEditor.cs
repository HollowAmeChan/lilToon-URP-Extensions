using lilToon.URP.Extensions.GeometryBuffer;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.GeometryBuffer
{
    [CustomEditor(typeof(HoGeometryBufferRendererFeature))]
    internal sealed class HoGeometryBufferRendererFeatureEditor : UnityEditor.Editor
    {
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
            DrawProperty("passEvent");
            DrawProperty("renderScale");
            DrawProperty("fallbackShader");

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
    }
}
