using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.Editor;
using UnityEditor;
using UnityEngine;

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
                "AC 合成 OB / SB 产出的属性图，下游只吃 AC。必须排在 Ho-ObjectBuffer 与 Ho-SurfaceBuffer 之后："
                + "同事件时按 Renderer Feature 列表顺序，加上 RenderGraph 的读依赖保证在后。"
                + "调试入口在 Ho-AttributeComposite Volume 的「调试」分组。",
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
                EditorGUILayout.HelpBox(
                    "启用是兜底值：Ho-AttributeComposite Volume 覆盖了就用 Volume 的（Volume 未覆盖时用这里的值）。"
                    + "属性清单默认开关与 lane 成本档由 HoSemanticSchema 声明，见下面的「声明」。",
                    MessageType.None);
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
                    "调试模式（Lane Coverage / Lane SemanticId / Lane Object Mask）与 Debug In Scene View / "
                    + "Debug In Game View 已移至 Ho-AttributeComposite Volume 的「调试」分组。"
                    + "消费者登记表与「解析不到」的报错不在这里，在下面的「声明（只读汇总）」。",
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
                    DrawProperty(passEvent, "产出时机", "AC 必须排在 OB / SB 之后。同事件时按 Renderer Feature 列表顺序，加上 RenderGraph 的读依赖保证在后。");
                    DrawProperty(Find("debugPassEvent"), "调试时机");
                }

                EditorGUILayout.HelpBox(
                    "合成与调试用 shader 由 feature 按 HoAttributeCompositeShaderConstants 里的固定名字取（Shader.Find），"
                    + "不走资产字段。",
                    MessageType.None);
            }
        }

        private static void DrawSchemaTable()
        {
            EditorGUILayout.LabelField($"语义声明（{HoSemanticSchema.LaneCount} 条 lane）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "本轮只有 object 来源：lane 的值 = 「该物体位的覆盖率之和」。surface 来源与五种 sourceMode 的合成等 SB 落地。",
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
                string line = $"{declaration.Consumer}：{string.Join(", ", declaration.Names)}";
                if (unresolved.Length > 0)
                {
                    EditorGUILayout.HelpBox($"{line}\n解析不到：{string.Join(", ", unresolved)}", MessageType.Warning);
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
