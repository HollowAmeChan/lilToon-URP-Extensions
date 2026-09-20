using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ObjectBuffer
{
    [CustomEditor(typeof(HoObjectBufferRendererFeature))]
    internal sealed class HoObjectBufferRendererFeatureEditor : UnityEditor.Editor
    {
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color CoverageColor = new Color(0.42f, 0.72f, 0.58f);
        private static readonly Color SelectionColor = new Color(0.80f, 0.55f, 0.85f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime;
        private static bool showCoverage;
        private static bool showSelections;
        private static bool showDebug;
        private static bool showAdvanced;
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

            DrawRuntime();
            DrawCoverage();
            DrawSelections();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled);

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

                int partRows = HoObjectBufferRegistry.PartRowCount;
                int selections = HoObjectBufferRegistry.SelectionCount;
                EditorGUILayout.LabelField("已注册", $"部件行 {Mathf.Max(0, partRows - 1)} / 选择 {selections}", EditorStyles.miniLabel);

                if (!HoObjectBufferRegistry.SupportsStructuredBuffer)
                {
                    EditorGUILayout.HelpBox("平台不支持 StructuredBuffer（shader level < 4.5），feature 不会运行。", MessageType.Error);
                }
            }
        }

        private void DrawCoverage()
        {
            SerializedProperty sampleCount = Find("sampleCount");
            string summary = LilUrpEditorSectionGui.EnumName(sampleCount);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showCoverage, "覆盖率（自建 MSAA）", summary, CoverageColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("sampleCount");
                EditorGUILayout.LabelField("层数 K 固定 4；平台可把 N 降到 2x/1x（1x 时 coverage 只有 0/1）。", EditorStyles.miniLabel);
            }
        }

        private void DrawSelections()
        {
            SerializedProperty selectionLayers = Find("selectionLayers");
            string summary = LilUrpEditorSectionGui.EnumName(selectionLayers);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSelections, "Selection（R1 兼容层）", summary, SelectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("selectionLayers");
                EditorGUILayout.LabelField("R1 迁移兼容层；没有注册选择时不分配这张图。", EditorStyles.miniLabel);
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
                EditorGUILayout.LabelField("洋红 = 未注册身份；整屏暗红 = 本帧没产出。", EditorStyles.miniLabel);
            }
        }

        private void DrawAdvanced()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "时机 / Shader", AdvancedColor))
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
                EditorGUILayout.LabelField("fallback 只覆盖不透明队列；cutout / 透明部件走 lilToon 侧 pass。", EditorStyles.miniLabel);
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
    }
}
