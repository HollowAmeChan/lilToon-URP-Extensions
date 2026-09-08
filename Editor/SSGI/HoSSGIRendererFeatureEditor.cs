using lilToon.URP.Extensions.Editor;
using lilToon.URP.Extensions.SSGI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.SSGI
{
    [CustomEditor(typeof(HoSSGIRendererFeature))]
    internal sealed class HoSSGIRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime = true;
        private static bool showTracing = true;
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
                "Ho-SSGI reads the opaque source after opaques and the shared Ho-GeometryBuffer before opaques. This producer-only build publishes _HoGITexture and keeps its debug preview local to the feature.",
                MessageType.Info);

            DrawRuntime();
            DrawTracing();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled) + " / Opaque source";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("enabled");
                EditorGUILayout.HelpBox("GeometryBuffer should be before opaques; Ho-SSGI itself runs after opaques so the opaque source is valid.", MessageType.None);
                DrawProperty("intensity");
                DrawProperty("sourceSaturation");
            }
        }

        private void DrawTracing()
        {
            string summary = Find("rayCount").intValue + " rays / " + Find("stepCount").intValue + " steps";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showTracing, "追踪", summary, AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("rayCount");
                DrawProperty("stepCount");
                DrawProperty("rayLength");
                DrawProperty("thickness");
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
                if (debugMode != null && debugMode.enumValueIndex != (int)HoSSGIDebugMode.Off)
                {
                    EditorGUILayout.HelpBox("Debug preview replaces the current camera color after post-processing. It does not feed lilToon or DebugTile.", MessageType.Info);
                }
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
                DrawProperty("shader");
                DrawProperty("debugShader");
            }
        }

        private void DrawProperty(string relativeName)
        {
            SerializedProperty property = Find(relativeName);
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
