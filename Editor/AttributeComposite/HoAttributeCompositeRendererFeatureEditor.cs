using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.AttributeComposite
{
    /// <summary>
    /// AC feature 面板：**高级设置 + 只读的 schema/catalog 汇总 + 消费者登记表**（规划 §9.13：
    /// 调试入口在 Volume，feature 不放调试开关）。解析不到的名字在这里报出来。
    /// </summary>
    [CustomEditor(typeof(HoAttributeCompositeRendererFeature))]
    internal sealed class HoAttributeCompositeRendererFeatureEditor : UnityEditor.Editor
    {
        private SerializedProperty settingsProperty;

        private void OnEnable()
        {
            settingsProperty = serializedObject.FindProperty("settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty enabled = settingsProperty.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(enabled, new GUIContent("启用"));

            using (new EditorGUI.DisabledScope(enabled != null && !enabled.boolValue))
            {
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("passEvent"), new GUIContent("产出时机", "AC 必须排在 OB / SB 之后。同事件时按 Renderer Feature 列表顺序，加上 RenderGraph 的读依赖保证在后。"));
                EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("debugPassEvent"), new GUIContent("调试时机"));
            }

            serializedObject.ApplyModifiedProperties();

            DrawSchemaTable();
            DrawConsumerTable();
        }

        private static void DrawSchemaTable()
        {
            EditorGUILayout.Space(4.0f);
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
    }
}
