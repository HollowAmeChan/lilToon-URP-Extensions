using System;
using System.Reflection;
using lilToon.URP.Extensions.CharacterSpecialization;
using lilToon.URP.Extensions.Editor.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.CharacterSpecialization
{
    /// <summary>
    /// 角色特化 Volume 的 Inspector：与 ImageProcess / ScreenProcess 同一套「效果浏览器」
    /// （顶栏搜索 + 左侧图标侧栏 + 右侧区段行）。
    /// </summary>
    /// <remarks>
    /// 五个效果各自保留一份参数；右侧列表可拖动排序，顺序同时用于最终颜色合成。
    /// 参数由 Volume 单一提供（不再有 Settings 兜底值与逐参数 override，见
    /// Documentation~/归档/CharacterSpecializationBrowser.md）。
    /// </remarks>
    [CustomEditor(typeof(HoCharacterSpecializationVolume))]
    internal sealed class HoCharacterSpecializationVolumeEditor : HoVolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float CheckboxWidth = 18.0f;
        private const float RemoveWidth = 18.0f;
        private const string ExpandKeyPrefix = "lilToon.CharacterSpecialization.";
        private static readonly string[] LayoutFields = { "subjectOutlineFillMode", "subjectOutlineHeightFadeMode", "enhancedOutlineHeightFadeMode", "capsuleAOMode" };

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

        /// <summary>目录的稳定 ID 对应 HoCharacterCompositionEffect；右侧顺序来自 Volume。</summary>
        private static readonly EffectSection[] Sections =
        {
            new EffectSection("DropShadow", "前发投影", "icon_DropShadow_v1", "hairDropShadowEnabled", "hairShadowOpacity",
                new[] { "hairDropShadow", "hairShadow" }),
            new EffectSection("FaceHairDiffuse", "前发漫反射", "icon_Blur_v1", "faceHairDiffuseEnabled", "faceHairDiffuseStrength",
                new[] { "faceHairDiffuse" }),
            new EffectSection("EnhancedOutline", "增强描边", "icon_RimLight_v1", "enhancedOutlineEnabled", "enhancedOutlineStrength",
                new[] { "enhancedOutline" }),
            new EffectSection("SubjectOutline", "主体描边", "icon_OutLine_v1", "subjectOutlineEnabled", "subjectOutlineStrength",
                new[] { "subjectOutline" }),
            new EffectSection("EyeReveal", "眼透", "icon_Glow_SelectColor_v1", "eyeRevealEnabled", "eyeRevealStrength",
                new[] { "eyeReveal", "useEyeRevealArea", "sameCharacterOnly" }),
            new EffectSection("CapsuleAO", "胶囊 AO（前置）", "icon_DropShadow_v1", "capsuleAOEnabled", "capsuleAOStrength",
                new[] { "capsuleAO" }),
        };

        private SerializedProperty effectsParameter;
        private SerializedProperty effects;
        private SerializedProperty compositionOrder;
        private ReorderableList effectList;
        private int layoutStamp;
        private EffectBrowserState browserState;
        private EffectBrowserCatalog browserCatalog;

        public override void OnEnable()
        {
            effectsParameter = serializedObject.FindProperty("Effects");
            effects = effectsParameter != null ? effectsParameter.FindPropertyRelative("m_Value") : null;
            compositionOrder = effects != null ? effects.FindPropertyRelative("compositionOrder") : null;
            browserState = EffectBrowserState.For("CharacterSpecialization");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (effects == null || compositionOrder == null)
            {
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EnsureCompositionOrder();
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

        private void EnsureCompositionOrder()
        {
            int used = 0;
            bool valid = compositionOrder.arraySize == HoCharacterCompositionOrder.Count;
            for (int i = 0; i < compositionOrder.arraySize && valid; i++)
            {
                int value = compositionOrder.GetArrayElementAtIndex(i).intValue;
                valid = value >= 0 && value < HoCharacterCompositionOrder.Count && (used & (1 << value)) == 0;
                if (valid) used |= 1 << value;
            }
            if (valid) return;
            var stored = new HoCharacterCompositionEffect[compositionOrder.arraySize];
            for (int i = 0; i < stored.Length; i++) stored[i] = (HoCharacterCompositionEffect)compositionOrder.GetArrayElementAtIndex(i).intValue;
            var normalized = HoCharacterCompositionOrder.Normalize(stored);
            compositionOrder.arraySize = HoCharacterCompositionOrder.Count;
            for (int i = 0; i < compositionOrder.arraySize; i++)
                compositionOrder.GetArrayElementAtIndex(i).intValue = (int)normalized[i];
        }

        private int GetLayoutStamp()
        {
            unchecked
            {
                int stamp = 17;
                for (int i = 0; i < compositionOrder.arraySize; i++)
                {
                    int effect = compositionOrder.GetArrayElementAtIndex(i).intValue;
                    stamp = stamp * 31 + effect;
                    stamp = stamp * 31 + (SessionState.GetBool(ExpandKeyPrefix + Sections[effect].Id, false) ? 1 : 0);
                }
                foreach (string name in LayoutFields)
                    stamp = stamp * 31 + Find(name).intValue;
                return stamp;
            }
        }

        private void DrawSections()
        {
            int stamp = GetLayoutStamp();
            if (effectList == null || stamp != layoutStamp)
            {
                layoutStamp = stamp;
                effectList = new ReorderableList(serializedObject, compositionOrder, true, false, false, false);
                effectList.headerHeight = 0;
                effectList.drawHeaderCallback = null;
                effectList.elementHeightCallback = GetElementHeight;
                effectList.drawElementCallback = (rect, index, active, focused) =>
                {
                    int effect = compositionOrder.GetArrayElementAtIndex(index).intValue;
                    DrawSectionRow(rect, effect, Sections[effect], EffectBrowserView.CurrentQuery);
                };
            }
            effectList.DoLayoutList();
        }

        private float GetElementHeight(int index)
        {
            EffectSection section = Sections[compositionOrder.GetArrayElementAtIndex(index).intValue];
            if (!SessionState.GetBool(ExpandKeyPrefix + section.Id, false)) return LineHeight + 6;
            var layout = new HoCharacterEffectLayout(new Rect(), false);
            DrawEffect(section, layout);
            return LineHeight + 10 + layout.Height;
        }

        private void DrawSectionRow(Rect rect, int effect, EffectSection section, string query)
        {
            SerializedProperty enabled = Find(section.EnableField);
            SerializedProperty summaryValue = Find(section.SummaryField);
            bool expanded = SessionState.GetBool(ExpandKeyPrefix + section.Id, false);
            rect.y += 2;
            DrawRowHighlight(new Rect(rect.x, rect.y, rect.width, LineHeight), effect, query);
            if (enabled != null)
                enabled.boolValue = EditorGUI.Toggle(new Rect(rect.x, rect.y, CheckboxWidth, LineHeight), enabled.boolValue);
            string summary = enabled != null && enabled.boolValue
                ? "开" + (summaryValue != null ? " " + summaryValue.floatValue.ToString("0.###") : string.Empty) : "关";
            float foldoutWidth = Mathf.Max(0, rect.width - CheckboxWidth - RemoveWidth - 6);
            var title = new GUIContent(section.Label + "　" + summary, section.Id == "CapsuleAO"
                ? "只附加到已发布 HoAO，始终在不透明绘制前执行；列表位置不改变该依赖阶段。"
                : "拖动左侧手柄调整颜色效果叠加顺序；眼透默认最后。前置 AO 不参与颜色叠加。");
            expanded = EditorGUI.Foldout(new Rect(rect.x + CheckboxWidth, rect.y, foldoutWidth, LineHeight), expanded, title, true);
            Rect removeRect = new Rect(rect.xMax - RemoveWidth, rect.y + 1, RemoveWidth, LineHeight - 2);
            if (EffectBrowserView.DrawChromeLessButton(removeRect, new GUIContent("×", "关闭这个效果，保留参数和位置")) && enabled != null)
                enabled.boolValue = false;
            SessionState.SetBool(ExpandKeyPrefix + section.Id, expanded);
            if (!expanded) return;
            using (new EditorGUI.IndentLevelScope())
                DrawEffect(section, new HoCharacterEffectLayout(new Rect(rect.x, rect.y + LineHeight + 4, rect.width, 0), true));
        }

        private void DrawEffect(EffectSection section, HoCharacterEffectLayout layout)
        {
            switch (section.Id)
            {
                case "EyeReveal": HoCharacterEyeRevealEditorSection.DrawEffects(effects, layout); break;
                case "DropShadow": HoCharacterDropShadowEditorSection.DrawEffects(effects, layout); break;
                case "FaceHairDiffuse": HoCharacterFaceHairDiffuseEditorSection.DrawEffects(effects, layout); break;
                case "SubjectOutline": HoCharacterSubjectOutlineEditorSection.DrawEffects(effects, layout); break;
                case "EnhancedOutline": HoCharacterEnhancedOutlineEditorSection.DrawEffects(effects, layout); break;
                case "CapsuleAO":
                    layout.Property(Find("capsuleAOMode"), "模式");
                    layout.Property(Find("capsuleAOBlend"), "与 HoAO 混合");
                    layout.Property(Find("capsuleAOStrength"), "强度");
                    layout.Property(Find("capsuleAOInfluenceDistance"), "影响距离");
                    if (Find("capsuleAOMode").enumValueIndex == 1)
                    {
                        layout.Property(Find("capsuleAOCrossLength"), "十字半臂长");
                        layout.Property(Find("capsuleAOCrossWidth"), "十字宽度");
                        layout.Property(Find("capsuleAOCrossAngle"), "交叉角度");
                        layout.Property(Find("capsuleAOCrossRotation"), "十字旋转");
                        layout.Property(Find("capsuleAOCrossSoftness"), "十字软度");
                    }
                    layout.Property(Find("capsuleAOMaxCapsules"), "相机胶囊预算");
                    break;
            }
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
