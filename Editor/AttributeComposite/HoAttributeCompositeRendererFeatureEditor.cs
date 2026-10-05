using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;
using lilToon.URP.Extensions.SurfaceBuffer;

namespace lilToon.URP.Extensions.Editor.AttributeComposite
{
    /// <summary>
    /// AC feature 面板：**运行（兜底）/ 声明（只读汇总）/ 调试（一行 → Volume）/ 高级**。
    /// 调试入口在 Volume（AC 架构 §9.13），feature 不放调试开关；解析不到的名字在这里报出来。
    /// </summary>
    [CustomEditor(typeof(HoAttributeCompositeRendererFeature))]
    internal sealed class HoAttributeCompositeRendererFeatureEditor : UnityEditor.Editor
    {
        // Palette (Ho-UI 风格规范 §1)。
        private static readonly Color RuntimeColor = new Color(0.46f, 0.64f, 0.92f);
        private static readonly Color DeclarationColor = new Color(0.80f, 0.55f, 0.85f);
        private static readonly Color DebugColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color AdvancedColor = new Color(0.62f, 0.58f, 0.78f);

        private static bool showRuntime = true;
        private static bool showDeclaration;
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
                "必须排在所需的 GeometryBuffer、ObjectBuffer 与 SurfaceBuffer 之后。输入可用性按相机发布。",
                MessageType.Info);

            DrawRuntime();
            DrawDeclaration();
            DrawDebug();
            DrawAdvanced();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRuntime()
        {
            SerializedProperty enabled = Find("enabled");
            string summary = LilUrpEditorSectionGui.BoolSummary(enabled)
                + " / " + HoSemanticSchema.LaneCount + " 条 lane";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showRuntime, "运行（兜底默认值）", summary, RuntimeColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawProperty(enabled, "启用");
            }
        }

        private void DrawDeclaration()
        {
            IReadOnlyList<HoAttributeCompositeConsumerDeclaration> consumers = HoAttributeCompositeConsumerRegistry.Declarations;
            string summary = HoSemanticSchema.LaneCount + " lane / " + consumers.Count + " 消费者";
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDeclaration, "声明（只读汇总）", summary, DeclarationColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSchemaTable();
                DrawConsumerTable();
                HoAttributeCompositeDemandSnapshot demand = HoAttributeCompositeConsumerRegistry.LastSnapshot;
                if (demand != null)
                {
                    EditorGUILayout.LabelField("最近需求快照", demand.CameraName + " / #" + demand.RenderSequence + " / frame " + demand.Frame);
                    EditorGUILayout.LabelField("请求资源", demand.Resources + " / lanes 0x" + demand.LaneMask.ToString("X2"));
                    HoAttributeCompositeProductionSnapshot production = HoAttributeCompositeProductionDiagnostics.LastSnapshot;
                    if (production != null && production.Demand.RenderSequence == demand.RenderSequence)
                        EditorGUILayout.LabelField("语义 RT", "Selection " + production.SelectionTextures + " / lane " + production.LegacySemanticTextures +
                            " / W,V,status " + production.CorrelatedStatisticTextures + " / MS捕获 " + production.CorrelatedCaptureTextures);
                }
                EditorGUILayout.LabelField("语义精度", HoSurfaceSemanticPrecisionDiagnostics.Status);
                if (HoSurfaceSemanticPrecisionDiagnostics.Frame >= 0)
                    EditorGUILayout.LabelField("最近相机与采样", HoSurfaceSemanticPrecisionDiagnostics.CameraName + " / " +
                        (HoSurfaceSemanticPrecisionDiagnostics.Samples > 0 ? HoSurfaceSemanticPrecisionDiagnostics.Samples + "x" : "—"));
            }
        }

        private void DrawDebug()
        {
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showDebug, "调试", "在 Volume", DebugColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox(
                    "调试模式与视图开关已移至 Ho-AttributeComposite Volume 的「调试」分组。",
                    MessageType.None);
            }
        }

        private void DrawAdvanced()
        {
            SerializedProperty passEvent = Find("passEvent");
            if (!LilUrpEditorSectionGui.DrawSectionHeader(ref showAdvanced, "高级", "时机 / Shader", AdvancedColor))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(Find("enabled") != null && !Find("enabled").boolValue))
                {
                    DrawProperty(passEvent, "产出时机", "AC 必须排在 GB / OB / SB 之后，同事件时按 Renderer Feature 列表顺序。");
                    DrawProperty(Find("debugPassEvent"), "调试时机");
                }


            }
        }

        private static void DrawSchemaTable()
        {
            EditorGUILayout.LabelField($"语义声明（{HoSemanticSchema.LaneCount} 条 lane）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Selection 合成物体与表面语义；几何覆盖率经 AC 独立查询。",
                EditorStyles.miniLabel);

            IReadOnlyList<HoSemanticEntry> declarations = HoSemanticSchema.Declarations;
            for (int i = 0; i < declarations.Count; i++)
            {
                HoSemanticEntry entry = declarations[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect swatch = GUILayoutUtility.GetRect(12.0f, 12.0f, GUILayout.Width(12.0f));
                    EditorGUI.DrawRect(swatch, entry.debugColor);
                    EditorGUILayout.LabelField(
                        $"lane {entry.laneIndex} · {entry.displayName}（{entry.name}）",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        $"id {entry.semanticId} · 物体位 {entry.objectTagBit} · {entry.sourceMode}",
                        EditorStyles.miniLabel,
                        GUILayout.Width(200.0f));
                }
            }

            string problem = HoSemanticSchema.DescribeValidation();
            if (!string.IsNullOrEmpty(problem))
            {
                EditorGUILayout.HelpBox(problem, MessageType.Error);
            }
        }

        private static void DrawConsumerTable()
        {
            EditorGUILayout.Space(4.0f);
            IReadOnlyList<HoAttributeCompositeConsumerDeclaration> declarations = HoAttributeCompositeConsumerRegistry.Declarations;
            EditorGUILayout.LabelField($"消费者登记（{declarations.Count} 个）", EditorStyles.boldLabel);
            if (declarations.Count == 0)
            {
                EditorGUILayout.LabelField("还没有消费者声明自己读了哪些名字。", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < declarations.Count; i++)
            {
                HoAttributeCompositeConsumerDeclaration declaration = declarations[i];
                string[] unresolved = declaration.UnresolvedNames;
                string[] names = declaration.Names;
                string identity = declaration.OwnerId != 0 ? " #" + declaration.OwnerId : "（全局旧声明）";
                string detail = names.Length > 0 ? string.Join(", ", names) : declaration.Resources.ToString();
                string line = $"{declaration.Consumer}{identity}：{detail}";
                if (unresolved.Length > 0 || declaration.InvalidQueryCount > 0)
                {
                    EditorGUILayout.HelpBox($"{line}\n解析不到：{string.Join(", ", unresolved)} / 无效查询 {declaration.InvalidQueryCount}", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                }
            }
        }

        private void DrawProperty(SerializedProperty property, string label, string tooltip = null)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
            }
        }

        private SerializedProperty Find(string relativeName)
        {
            return settingsProperty != null ? settingsProperty.FindPropertyRelative(relativeName) : null;
        }
    }
}
