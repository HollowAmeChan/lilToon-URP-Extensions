using lilToon.URP.Extensions.CharacterBuffer;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterBuffer
{
    [CustomEditor(typeof(HoCharacterBufferRendererFeature))]
    internal sealed class HoCharacterBufferRendererFeatureEditor : UnityEditor.Editor
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

            EditorGUILayout.HelpBox(
                "Ho-CharacterBuffer：per-pixel 只存 ID 与覆盖率，其余按 ID 查 palette。\n" +
                "覆盖率由本 feature 自建的 MSAA 产出，**与相机的 AA 设置无关**；几何（法线/深度/几何覆盖率）仍然只从 GeometryBuffer 读。",
                MessageType.Info);

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

                int partRows = HoCharacterBufferRegistry.PartRowCount;
                int selections = HoCharacterBufferRegistry.SelectionCount;
                EditorGUILayout.LabelField("已注册", $"部件行 {Mathf.Max(0, partRows - 1)} / 选择 {selections}", EditorStyles.miniLabel);

                if (!HoCharacterBufferRegistry.SupportsStructuredBuffer)
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
                EditorGUILayout.HelpBox(
                    "层数 K 固定为 4，采样数 N 封顶 4。因为 K = N，实际配置下不存在尾部丢失：" +
                    "层里找不到某个 ID 就等于它没覆盖这个像素（规划 §5.5）。\n" +
                    "把采样数降到 2 可以省一半瞬态带宽，代价是覆盖率量子变成 0.5。",
                    MessageType.None);
            }
        }

        private void DrawSelections()
        {
            SerializedProperty selectionLayers = Find("selectionLayers");
            string summary = LilUrpEditorSectionGui.EnumName(selectionLayers);

            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showSelections, "选择层（Cryptomatte 式）", summary, SelectionColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty("selectionLayers");
                EditorGUILayout.HelpBox(
                    "选择层是具名的选区（取代匿名通道）。只有 group 里注册了选择才会分配那张图；" +
                    "写入端在 lilToon 侧的材质里（跨仓协议见规划 §5.11）。P1 只实现 2 个选择/像素。\n" +
                    "Material0（管线逐像素材质值）同样等 lilToon 侧的写入端就绪后再接线，现在不分配、不发布。",
                    MessageType.None);
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
                EditorGUILayout.HelpBox(
                    "ID 视图按 palette 的显示色上色：洋红 = 未注册（RSUV 没写上或索引越界）；" +
                    "没产出时整屏暗红，用来区分「没跑」和「全背景」。",
                    MessageType.None);
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
                EditorGUILayout.HelpBox(
                    "fallback 材质只覆盖不透明队列（override 材质看不到源材质的 alpha/cutout）；" +
                    "cutout / 透明部件要靠 lilToon 侧的 HoCharacterBuffer pass（跨仓）。",
                    MessageType.None);
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
