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
                "Ho-SSGI reads the opaque camera color after opaques and the shared Ho-GeometryBuffer. Its runtime parameters are controlled by the Ho-SSGI Volume; this feature keeps resource and shader fallback settings.",
                MessageType.Info);

            DrawRuntime();
            DrawTracing();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            string summary = "Volume controlled / Opaque source";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    DrawProperty("enabled");
                    DrawProperty("intensity");
                    DrawProperty("sourceSaturation");
                }
                EditorGUILayout.HelpBox("Ho-SSGI runs after opaques so it can sample the lit opaque camera color. GeometryBuffer must run before it to provide the no-outline geometry test.", MessageType.None);
            }
        }

        private void DrawTracing()
        {
            string summary = "Volume controlled";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showTracing, "追踪", summary, AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    DrawProperty("rayCount");
                    DrawProperty("stepCount");
                    DrawProperty("rayLength");
                    DrawProperty("thickness");
                    DrawProperty("temporalBlend");
                    DrawProperty("spatialRadius");
                }
            }
        }

        private void DrawDebug()
        {
            string summary = "Volume controlled";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", summary, DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    DrawProperty("debugMode");
                    DrawProperty("debugInSceneView");
                    DrawProperty("debugInGameView");
                }
                EditorGUILayout.HelpBox("Set debugMode in the Ho-SSGI Volume. The preview replaces the current camera color after post-processing and does not feed lilToon or DebugTile.", MessageType.Info);
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
                DrawProperty("compositePassEvent");
                EditorGUILayout.HelpBox("The trace event is clamped between AfterRenderingOpaques and BeforeRenderingPostProcessing; the composite event is clamped before post-processing.", MessageType.None);
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
