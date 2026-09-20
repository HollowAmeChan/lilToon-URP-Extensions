using System.Collections.Generic;
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

        /// <summary>图例最多列这么多行：200 个部件的角色会把面板撑爆，剩下的用一行汇总。</summary>
        private const int LegendRowLimit = 64;

        private static bool showRuntime;
        private static bool showCoverage;
        private static bool showSelections;
        private static bool showDebug;
        private static bool showAdvanced;
        private static bool showIdentityLegend;
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
                "Ho-ObjectBuffer：per-pixel 只存 ID 与覆盖率，其余按 ID 查 palette。\n" +
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

                int partRows = HoObjectBufferRegistry.PartRowCount;
                int selections = HoObjectBufferRegistry.SelectionCount;
                EditorGUILayout.LabelField("已注册", $"部件行 {Mathf.Max(0, partRows - 1)} / 选择 {selections}", EditorStyles.miniLabel);

                if (!HoObjectBufferRegistry.SupportsStructuredBuffer)
                {
                    EditorGUILayout.HelpBox("平台不支持 StructuredBuffer（shader level < 4.5），feature 不会运行。", MessageType.Error);
                }

                DrawIdentityLegend();
            }
        }

        /// <summary>
        /// 身份图例：调试视图是"按 palette 显示色上色"的，没有这份对照就只能靠猜颜色。
        /// 槽位 = 部件在组里的顺序（也就是 ID 的低字节），所以这里按槽位顺序列。
        /// </summary>
        private static void DrawIdentityLegend()
        {
            HoObjectBufferRegistry.EnsureBuilt();
            IReadOnlyList<HoObjectBufferGroup> groups = HoObjectBufferGroup.GetActiveGroups();
            int partRows = Mathf.Max(0, HoObjectBufferRegistry.PartRowCount - 1);

            showIdentityLegend = EditorGUILayout.Foldout(
                showIdentityLegend,
                $"身份图例（部件 {partRows} · 组 {groups.Count}）",
                true);
            if (!showIdentityLegend)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (groups.Count == 0)
                {
                    EditorGUILayout.LabelField("当前没有活动的 Ho-ObjectBuffer Group（组件被禁用时也不会注册）。", EditorStyles.miniLabel);
                    return;
                }

                var buffer = new List<Renderer>();
                int shown = 0;
                int hidden = 0;
                for (int g = 0; g < groups.Count; g++)
                {
                    HoObjectBufferGroup group = groups[g];
                    if (group == null)
                    {
                        continue;
                    }

                    EditorGUILayout.LabelField($"组 {group.groupId} · {group.name}", EditorStyles.boldLabel);
                    IReadOnlyList<string> names = group.GetPartNames();
                    if (names.Count == 0)
                    {
                        EditorGUILayout.LabelField("   （没有部件 → 这一组不写 RSUV）", EditorStyles.miniLabel);
                        continue;
                    }

                    for (int slot = 0; slot < names.Count; slot++)
                    {
                        if (shown >= LegendRowLimit)
                        {
                            hidden++;
                            continue;
                        }

                        string partName = names[slot];
                        HoObjectBufferPartEntry entry = FindEntry(group, partName);
                        uint partId = HoObjectBufferRegistry.GetPartId(group.groupId, partName);
                        buffer.Clear();
                        if (entry != null)
                        {
                            HoObjectBufferGroup.CollectEntryRenderers(entry, buffer);
                        }

                        DrawLegendRow(
                            partId,
                            entry != null ? entry.displayColor : Color.gray,
                            partName,
                            entry != null ? entry.category : HoObjectBufferPartCategory.Unspecified,
                            buffer.Count);
                        shown++;
                    }
                }

                if (hidden > 0)
                {
                    EditorGUILayout.LabelField($"…还有 {hidden} 个部件（图例只列前 {LegendRowLimit} 行）", EditorStyles.miniLabel);
                }
            }
        }

        private static HoObjectBufferPartEntry FindEntry(HoObjectBufferGroup group, string partName)
        {
            for (int i = 0; i < group.parts.Count; i++)
            {
                HoObjectBufferPartEntry entry = group.parts[i];
                if (entry != null && entry.name == partName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static void DrawLegendRow(
            uint partId,
            Color displayColor,
            string partName,
            HoObjectBufferPartCategory category,
            int rendererCount)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 16.0f);
            Rect swatch = new Rect(rect.x, rect.y + 1.0f, 13.0f, 13.0f);
            EditorGUI.DrawRect(swatch, displayColor);

            Rect label = new Rect(swatch.xMax + 6.0f, rect.y, Mathf.Max(0.0f, rect.xMax - swatch.xMax - 6.0f), rect.height);
            string idText = partId > 0u ? $"0x{partId:X4}" : "未注册";
            EditorGUI.LabelField(label, $"{idText}   {partName}   （{category} · {rendererCount} Renderer）", EditorStyles.miniLabel);
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
                    "层数 K 固定为 4，请求采样数 N 封顶 4，但平台可降级为 2x/1x。" +
                    "K 不丢当帧实际 N≤4 个前表面 sample ID；1x 时 coverage 会退化为 0/1。",
                    MessageType.None);
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
                EditorGUILayout.HelpBox(
                    "R1 保留现有选择图仅作迁移兼容；正式 surface SemanticId 由后续 SB + AC 协议接管。" +
                    "没有注册选择时不会分配这张图。\n" +
                    "表面色与材质数值（roughness / metallic / thickness / 反射 …）已拆到 Ho-SurfaceBuffer（规划 §5.12），本 feature 不再有这些通道。",
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
                    "没产出时整屏暗红，用来区分「没跑」和「全背景」。" +
                    "同一批视图也已注册到 Ho-DebugTile；Volume 中的调试设置会在 override 时覆盖这里的兜底值。",
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
                    "cutout / 透明部件要靠 lilToon 侧的 HoObjectBuffer pass（跨仓）。",
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
