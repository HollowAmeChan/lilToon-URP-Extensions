using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Editor.PostProcessing;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    /// <summary>
    /// 角色特化 Volume 的 Inspector：与 ImageProcess / ScreenProcess 同一套「效果浏览器」
    /// （顶栏搜索 + 左侧图标侧栏 + 右侧区段行）。
    /// </summary>
    /// <remarks>
    /// 这块**没有图层、也没有顺序**：五个效果是固定的一组区段，每个只有"启用/停用"两态，
    /// 参数由 Volume 单一提供（不再有 Settings 兜底值与逐参数 override，见
    /// Documentation~/PostProcessing/CharacterSpecializationBrowser.md）。
    /// </remarks>
    [CustomEditor(typeof(HoCharacterSpecializationVolume))]
    internal sealed class HoCharacterSpecializationVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float CheckboxWidth = 18.0f;
        private const float RemoveWidth = 18.0f;
        private const string ExpandKeyPrefix = "lilToon.CharacterSpecialization.";

        /// <summary>区段行里那个「×」的背景色（与 IP/SP 的搜索高亮同一套观感）。</summary>
        private static readonly Color RowHighlightColor = new Color(0.30f, 0.55f, 0.95f, 0.16f);

        /// <summary>高亮行左侧的强调条。</summary>
        private static readonly Color RowHighlightAccent = new Color(0.35f, 0.65f, 1.0f, 0.85f);

        /// <summary>一个效果区段：显示名、图标、启用字段名，以及"恢复默认"要覆盖的字段前缀。</summary>
        private readonly struct EffectSection
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string IconName;
            public readonly string EnableField;
            public readonly string SummaryField;
            public readonly string[] FieldPrefixes;

            public EffectSection(string id, string label, string iconName, string enableField, string summaryField, string[] fieldPrefixes)
            {
                Id = id;
                Label = label;
                IconName = iconName;
                EnableField = enableField;
                SummaryField = summaryField;
                FieldPrefixes = fieldPrefixes;
            }
        }

        /// <summary>侧栏顺序 = 右侧区段顺序 = 执行顺序，写死（这块不做排序）。</summary>
        private static readonly EffectSection[] Sections =
        {
            new EffectSection("EyeReveal", "眼透", "icon_Glow_SelectColor_v1", "eyeRevealEnabled", "eyeRevealStrength",
                new[] { "eyeReveal", "useEyeRevealArea", "sameCharacterOnly" }),
            new EffectSection("DropShadow", "前发投影", "icon_DropShadow_v1", "hairDropShadowEnabled", "hairShadowOpacity",
                new[] { "hairDropShadow", "hairShadow" }),
            new EffectSection("FaceHairDiffuse", "前发漫反射", "icon_Blur_v1", "faceHairDiffuseEnabled", "faceHairDiffuseStrength",
                new[] { "faceHairDiffuse" }),
            new EffectSection("SubjectOutline", "主体描边", "icon_OutLine_v1", "subjectOutlineEnabled", "subjectOutlineStrength",
                new[] { "subjectOutline" }),
            new EffectSection("EnhancedOutline", "增强描边", "icon_RimLight_v1", "enhancedOutlineEnabled", "enhancedOutlineStrength",
                new[] { "enhancedOutline" }),
        };

        private SerializedProperty effectsParameter;
        private SerializedProperty effects;
        private EffectBrowserState browserState;
        private EffectBrowserCatalog browserCatalog;

        public override void OnEnable()
        {
            effectsParameter = serializedObject.FindProperty("Effects");
            effects = effectsParameter != null ? effectsParameter.FindPropertyRelative("m_Value") : null;
            browserState = EffectBrowserState.For("CharacterSpecialization");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (effects == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EnsureBrowser();
            EffectBrowserView.Draw(browserCatalog, browserState, DrawSections);

            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ 效果浏览器目录

        private void EnsureBrowser()
        {
            if (browserCatalog != null)
            {
                return;
            }

            var entries = new EffectBrowserEntry[Sections.Length];
            for (int i = 0; i < Sections.Length; i++)
            {
                entries[i] = new EffectBrowserEntry(i, Sections[i].Label, Sections[i].Id, Sections[i].IconName);
            }

            browserCatalog = new EffectBrowserCatalog(
                "CharacterSpecialization",
                entries,
                IsEffectEnabled,
                ToggleEffect,
                ResetEffectToDefaults,
                CountEnabledMatches);
        }

        /// <summary>侧栏绿色 = 该效果启用。</summary>
        private bool IsEffectEnabled(int effect)
        {
            if (effect >= Sections.Length)
            {
                return true;
            }

            SerializedProperty enabled = Find(Sections[effect].EnableField);
            return enabled != null && enabled.boolValue;
        }

        /// <summary>右击菜单里的「（n 个）」用不到层数，这里返回"启用与否"，计数文案才有意义。</summary>
        private int CountEnabledMatches(int effect)
        {
            return IsEffectEnabled(effect) ? 1 : 0;
        }

        private void ToggleEffect(int effect)
        {
            if (effect >= Sections.Length)
            {
                return;
            }

            SerializedProperty enabled = Find(Sections[effect].EnableField);
            if (enabled == null)
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Toggle Character Specialization Effect");
            enabled.boolValue = !enabled.boolValue;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        /// <summary>把一个效果的字段整体恢复成类里的默认值（等价于"恢复默认"，不改其它效果）。</summary>
        private void ResetEffectToDefaults(int effect)
        {
            Undo.RecordObject(serializedObject.targetObject, "Reset Character Specialization Effect");
            var defaults = new HoCharacterSpecializationEffects();
            FieldInfo[] fields = typeof(HoCharacterSpecializationEffects).GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                foreach (string prefix in Sections[effect].FieldPrefixes)
                {
                    if (field.Name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        ApplyDefault(fields, defaults, field.Name);
                        break;
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
        }

        private void ApplyDefault(FieldInfo[] fields, HoCharacterSpecializationEffects defaults, string name)
        {
            SerializedProperty property = Find(name);
            FieldInfo field = Array.Find(fields, f => f.Name == name);
            if (property == null || field == null)
            {
                return;
            }

            object value = field.GetValue(defaults);
            switch (value)
            {
                case bool boolValue:
                    property.boolValue = boolValue;
                    break;
                case float floatValue:
                    property.floatValue = floatValue;
                    break;
                case int intValue:
                    property.intValue = intValue;
                    break;
                case Color colorValue:
                    property.colorValue = colorValue;
                    break;
                case Enum enumValue:
                    property.enumValueIndex = Convert.ToInt32(enumValue);
                    break;
            }
        }

        private SerializedProperty Find(string name)
        {
            return effects != null ? effects.FindPropertyRelative(name) : null;
        }

        // ------------------------------------------------------------------ 右侧区段

        private void DrawSections()
        {
            string query = EffectBrowserView.CurrentQuery;

            for (int i = 0; i < Sections.Length; i++)
            {
                DrawSectionRow(i, Sections[i], query);
            }
        }

        private void DrawSectionRow(int index, EffectSection section, string query)
        {
            SerializedProperty enabled = Find(section.EnableField);
            SerializedProperty summaryValue = Find(section.SummaryField);
            bool expanded = SessionState.GetBool(ExpandKeyPrefix + section.Id, false);

            Rect rect = EditorGUILayout.GetControlRect(false, LineHeight);
            DrawRowHighlight(rect, index, query);

            float foldoutWidth = Mathf.Max(0.0f, rect.width - CheckboxWidth - RemoveWidth - 6.0f);
            if (enabled != null)
            {
                Rect toggleRect = new Rect(rect.x, rect.y, CheckboxWidth, rect.height);
                EditorGUI.BeginChangeCheck();
                bool value = EditorGUI.Toggle(toggleRect, enabled.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    enabled.boolValue = value;
                }
            }

            string summary = enabled != null && enabled.boolValue
                ? "开" + (summaryValue != null ? " " + summaryValue.floatValue.ToString("0.###") : string.Empty)
                : "关";
            Rect foldoutRect = new Rect(rect.x + CheckboxWidth, rect.y, foldoutWidth, rect.height);
            string title = section.Label + "　" + summary;
            expanded = EditorGUI.Foldout(foldoutRect, expanded, title, true);

            Rect removeRect = new Rect(rect.xMax - RemoveWidth, rect.y + 1.0f, RemoveWidth, rect.height - 2.0f);
            if (EffectBrowserView.DrawChromeLessButton(removeRect, new GUIContent("×", "关掉这个效果")))
            {
                if (enabled != null)
                {
                    enabled.boolValue = false;
                }
            }

            SessionState.SetBool(ExpandKeyPrefix + section.Id, expanded);
            if (!expanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            switch (section.Id)
            {
                case "EyeReveal":
                    HoCharacterEyeRevealEditorSection.DrawEffects(effects);
                    break;
                case "DropShadow":
                    HoCharacterDropShadowEditorSection.DrawEffects(effects);
                    break;
                case "FaceHairDiffuse":
                    HoCharacterFaceHairDiffuseEditorSection.DrawEffects(effects);
                    break;
                case "SubjectOutline":
                    HoCharacterSubjectOutlineEditorSection.DrawEffects(effects);
                    break;
                case "EnhancedOutline":
                    HoCharacterEnhancedOutlineEditorSection.DrawEffects(effects);
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2.0f);
        }

        private void DrawRowHighlight(Rect rect, int effect, string query)
        {
            if (string.IsNullOrEmpty(query) || browserCatalog == null)
            {
                return;
            }

            if (!browserCatalog.LayerEffectMatches(effect, query))
            {
                return;
            }

            EditorGUI.DrawRect(rect, RowHighlightColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2.0f, rect.height), RowHighlightAccent);
        }
    }
}
