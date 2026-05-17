using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(HoPostProcessStackVolume))]
    internal sealed partial class HoPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float EffectIconSize = 22.0f;
        private const float EffectIconSpacing = 2.0f;
        private const string PackageAssetRoot = "Packages/jp.lilxyzw.liltoon.urp.extensions";

        private static readonly string[] EdgeLightModes =
        {
            "单向",
            "双向",
            "单向锐化",
            "双向锐化"
        };

        private static readonly string[] DepthOfFieldModes =
        {
            "Gaussian",
            "Bokeh"
        };

        private static readonly string[] AovSources =
        {
            "遮罩",
            "角色组 ID",
            "部件 ID",
            "标记",
            "厚度",
            "曲率",
            "材质分类",
            "预留值",
            "材质自定义通道 0",
            "材质自定义通道 1",
            "材质自定义通道 2",
            "材质自定义通道 3",
            "主体",
            "脸",
            "前发",
            "眼睛",
            "眼透区域",
            "配件",
            "预留 6",
            "预留 7"
        };

        private static readonly string[] AovMaskModes =
        {
            "直接灰度",
            "阈值",
            "匹配数值 / ID",
            "匹配颜色"
        };

        private readonly struct EffectToggleEntry
        {
            public readonly HoPostProcessEffect Effect;
            public readonly string Label;
            public readonly string IconName;

            public EffectToggleEntry(HoPostProcessEffect effect, string label, string iconName)
            {
                Effect = effect;
                Label = label;
                IconName = iconName;
            }
        }

        private static readonly EffectToggleEntry[] VisibleEffectOrder =
        {
            new EffectToggleEntry(HoPostProcessEffect.EdgeLight, "边缘光", "icon_RimLight_v1"),
            new EffectToggleEntry(HoPostProcessEffect.Outline, "轮廓", "icon_OutLine_v1"),
            new EffectToggleEntry(HoPostProcessEffect.DropShadow, "投影", "icon_DropShadow_v1"),
            new EffectToggleEntry(HoPostProcessEffect.DepthOfField, "景深", "icon_Effects_v1"),
            new EffectToggleEntry(HoPostProcessEffect.CustomMaterial, "自定义", "icon_Effects_v1")
        };

        private static readonly Dictionary<HoPostProcessEffect, GUIContent> EffectIconContents = new Dictionary<HoPostProcessEffect, GUIContent>();

        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<HoPostProcessStackVolume> fetcher = new PropertyFetcher<HoPostProcessStackVolume>(serializedObject);
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, true, false, false, false);
            layerList.drawHeaderCallback = null;
            layerList.headerHeight = 0.0f;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(showInSceneView, new GUIContent("Scene View"));
            EditorGUILayout.Space(4.0f);

            DrawEffectIconToggles();
            EditorGUILayout.Space(4.0f);

            DrawLayerList();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLayerList()
        {
            if (layers == null)
            {
                return;
            }

            if (layerList != null)
            {
                layerList.DoLayoutList();
            }
            else
            {
                EditorGUILayout.PropertyField(layers, true);
            }
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return LineHeight + 6.0f;
            }

            if (!element.isExpanded)
            {
                return LineHeight + 6.0f;
            }

            int lineCount = GetElementLineCount(element);
            return (LineHeight + LineSpacing) * lineCount + 12.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            SerializedProperty enabledProperty = element.FindPropertyRelative("enabled");
            float y = DrawFoldoutLine(rect, rect.y, element, enabledProperty);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            HoPostProcessEffect effect = GetEffect(element);
            DrawCoreFields(
                rect,
                ref y,
                element,
                includeColorBlend: effect != HoPostProcessEffect.DepthOfField,
                includeTexture: effect == HoPostProcessEffect.CustomMaterial,
                includePassIndex: effect == HoPostProcessEffect.CustomMaterial,
                includeMaterialOverride: effect == HoPostProcessEffect.CustomMaterial);
            DrawAovMaskProperties(rect, ref y, element);

            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    DrawEdgeLightProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.Outline:
                    DrawOutlineProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.DropShadow:
                    DrawDropShadowProperties(rect, ref y, element);
                    break;
                case HoPostProcessEffect.DepthOfField:
                    DrawDepthOfFieldProperties(rect, ref y, element);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        private static int GetElementLineCount(SerializedProperty element)
        {
            switch (GetEffect(element))
            {
                case HoPostProcessEffect.EdgeLight:
                    return 15 + GetAovLineCount(element);
                case HoPostProcessEffect.Outline:
                    return 11 + GetAovLineCount(element);
                case HoPostProcessEffect.DepthOfField:
                    return GetDepthOfFieldLineCount(element) + GetAovLineCount(element);
                case HoPostProcessEffect.CustomMaterial:
                    return 7 + GetAovLineCount(element);
                case HoPostProcessEffect.DropShadow:
                default:
                    return 8 + GetAovLineCount(element);
            }
        }

        private static int GetAovLineCount(SerializedProperty element)
        {
            return HoPostAovMaskEditorUtility.GetLineCount(element);
        }

        private static int GetDepthOfFieldLineCount(SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            int mode = parameters0 != null ? Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, DepthOfFieldModes.Length - 1) : 1;
            return mode == 0 ? 6 : 8;
        }

        private float DrawFoldoutLine(Rect rect, float y, SerializedProperty element, SerializedProperty enabled)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            float checkboxWidth = 18.0f;
            float presetWidth = LayerPresetButtonSize;
            float intensityWidth = Mathf.Clamp(rect.width * 0.34f, 140.0f, 220.0f);
            float foldoutWidth = Mathf.Max(0.0f, rect.width - checkboxWidth - presetWidth - intensityWidth - 10.0f);

            if (enabled != null && enabled.propertyType == SerializedPropertyType.Boolean)
            {
                Rect enabledRect = new Rect(lineRect.x, lineRect.y, checkboxWidth, lineRect.height);
                EditorGUI.BeginChangeCheck();
                bool enabledValue = EditorGUI.Toggle(enabledRect, enabled.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    enabled.boolValue = enabledValue;
                    ApplyLayerListChanges();
                }
            }

            Rect foldoutRect = new Rect(lineRect.x + checkboxWidth, lineRect.y, foldoutWidth, lineRect.height);
            element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GetLayerLabel(element), true);

            Rect presetRect = new Rect(lineRect.xMax - intensityWidth - presetWidth - 4.0f, lineRect.y, presetWidth, lineRect.height);
            DrawLayerPresetButton(presetRect, element);

            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            if (intensity != null && intensity.propertyType == SerializedPropertyType.Float)
            {
                Rect intensityRect = new Rect(lineRect.xMax - intensityWidth, lineRect.y, intensityWidth, lineRect.height);
                Rect sliderRect = new Rect(intensityRect.x, intensityRect.y + 2.0f, intensityRect.width, intensityRect.height - 4.0f);
                EditorGUI.BeginChangeCheck();
                float intensityValue = GUI.HorizontalSlider(sliderRect, intensity.floatValue, 0.0f, 1.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    intensity.floatValue = intensityValue;
                    ApplyLayerListChanges();
                }
            }

            return y + LineHeight + LineSpacing;
        }

        private static void DrawCoreFields(Rect rect, ref float y, SerializedProperty element, bool includeColorBlend, bool includeTexture, bool includePassIndex, bool includeMaterialOverride)
        {
            if (includeColorBlend)
            {
            DrawPropertyLine(rect, ref y, element, "color", "颜色");
            DrawPropertyLine(rect, ref y, element, "blendMode", "混合模式");
            }

            if (includeTexture)
            {
                DrawPropertyLine(rect, ref y, element, "texture", "纹理");
            }

            if (includeMaterialOverride)
            {
                DrawPropertyLine(rect, ref y, element, "materialOverride", "材质覆盖");
                DrawPropertyLine(rect, ref y, element, "shaderOverride", "Shader 覆盖");
            }

            if (includePassIndex)
            {
                DrawPropertyLine(rect, ref y, element, "passIndex", "Pass 索引");
            }
        }

        private static void DrawEdgeLightProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            if (parameters0 == null || parameters1 == null || parameters2 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            if (p2 == Vector4.zero)
            {
                p2 = new Vector4(1.0f, 0.65f, 0.45f, 1.0f);
            }

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "边缘宽度", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "HDR 亮度", p0.y, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度", p0.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p1.x, -180.0f, 180.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", Mathf.Clamp(Mathf.RoundToInt(p1.y), 0, EdgeLightModes.Length - 1), EdgeLightModes);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "外扩宽度", p1.z, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "外扩强度", p1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "表面法线权重", p2.x, 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度边界权重", p2.y, 0.0f, 2.0f);
            y += LineHeight + LineSpacing;
            p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度灵敏度", p2.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "方向影响", p2.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
        }

        private static void DrawOutlineProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters0 == null || parameters1 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "线宽(像素)", p0.x, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度权重", p0.y, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线权重", p0.z, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "阈值", p0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", p1.x, 0.0001f, 1.0f);
            y += LineHeight + LineSpacing;
            p1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "深度灵敏度", p1.y, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "法线灵敏度", p1.z, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            p1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p1.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
        }

        private static void DrawDropShadowProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            if (parameters0 == null || parameters1 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            if (p0 == Vector4.zero && p1 == Vector4.zero)
            {
                p0 = new Vector4(0.35f, -45.0f, 0.85f, 6.0f);
                p1 = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
            }

            p0.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "距离", p0.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", p0.y, -180.0f, 180.0f);
            y += LineHeight + LineSpacing;
            p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "不透明度", p0.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度(像素)", p0.w, 0.0f, 32.0f);
            y += LineHeight + LineSpacing;
            p1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "扩散(像素)", p1.x, 0.0f, 8.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
        }

        private static void DrawDepthOfFieldProperties(Rect rect, ref float y, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            if (parameters0 == null || parameters1 == null || parameters2 == null)
            {
                return;
            }

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            if (p0 == Vector4.zero && p1 == Vector4.zero && p2 == Vector4.zero)
            {
                p0 = new Vector4(1.0f, 10.0f, 50.0f, 5.6f);
                p1 = new Vector4(10.0f, 30.0f, 8.0f, 1.0f);
                p2 = new Vector4(5.0f, 1.0f, 0.0f, 0.0f);
            }

            int mode = EditorGUI.Popup(
                new Rect(rect.x, y, rect.width, LineHeight),
                "模式",
                Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, DepthOfFieldModes.Length - 1),
                DepthOfFieldModes);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            if (mode == 0)
            {
                p1.x = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "开始距离", Mathf.Max(0.0f, p1.x));
                y += LineHeight + LineSpacing;
                p1.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "结束距离", Mathf.Max(p1.x + 0.001f, p1.y));
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "最大半径", p1.z, 0.0f, 16.0f);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "高质量采样", p1.w > 0.5f) ? 1.0f : 0.0f;
                y += LineHeight + LineSpacing;
            }
            else
            {
                p0.y = EditorGUI.FloatField(new Rect(rect.x, y, rect.width, LineHeight), "焦点距离", Mathf.Max(0.001f, p0.y));
                y += LineHeight + LineSpacing;
                p0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "焦距", p0.z, 1.0f, 300.0f);
                y += LineHeight + LineSpacing;
                p0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "光圈", p0.w, 1.0f, 32.0f);
                y += LineHeight + LineSpacing;
                p2.x = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "叶片数量", Mathf.Clamp(Mathf.RoundToInt(p2.x), 3, 9), 3, 9);
                y += LineHeight + LineSpacing;
                p2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片弧度", p2.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                p2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "叶片旋转", p2.z, -180.0f, 180.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
        }

        private static void DrawAovMaskProperties(Rect rect, ref float y, SerializedProperty element)
        {
            HoPostAovMaskEditorUtility.Draw(rect, ref y, element, LineHeight, LineSpacing);
        }

        private static void DrawAovSourceProperties(
            Rect rect,
            ref float y,
            SerializedProperty aovSource,
            SerializedProperty aovMaskMode)
        {
            aovSource.enumValueIndex = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "AOV 源", Mathf.Clamp(aovSource.enumValueIndex, 0, AovSources.Length - 1), AovSources);
            y += LineHeight + LineSpacing;
            aovMaskMode.enumValueIndex = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "使用方式", Mathf.Clamp(aovMaskMode.enumValueIndex, 0, AovMaskModes.Length - 1), AovMaskModes);
            y += LineHeight + LineSpacing;
        }

        private static void DrawAovMatchProperties(
            Rect rect,
            ref float y,
            SerializedProperty aovMaskMode,
            SerializedProperty aovThreshold,
            SerializedProperty aovMatchValue,
            SerializedProperty aovMatchColor,
            SerializedProperty invertAovMask,
            SerializedProperty debugAovMask)
        {
            HoPostAovMaskMode mode = (HoPostAovMaskMode)Mathf.Clamp(aovMaskMode.enumValueIndex, 0, AovMaskModes.Length - 1);
            switch (mode)
            {
                case HoPostAovMaskMode.Threshold:
                    aovThreshold.floatValue = EditorGUI.Slider(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("阈值", "通道灰度达到这个值后开始被选中。"),
                        Mathf.Max(0.0f, aovThreshold.floatValue),
                        0.0f,
                        1.0f);
                    y += LineHeight + LineSpacing;
                    break;

                case HoPostAovMaskMode.MatchValue:
                    aovMatchValue.floatValue = EditorGUI.FloatField(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("匹配数值 / ID", "目标值。GroupId、ObjectId、Flags、Material 会在 shader 里先编码再比较。"),
                        aovMatchValue.floatValue);
                    y += LineHeight + LineSpacing;
                    aovThreshold.floatValue = EditorGUI.Slider(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("数值容差", "允许目标值附近多宽的范围被选中。"),
                        Mathf.Max(0.0f, aovThreshold.floatValue),
                        0.0f,
                        1.0f);
                    y += LineHeight + LineSpacing;
                    break;

                case HoPostAovMaskMode.MatchColor:
                    aovMatchColor.colorValue = EditorGUI.ColorField(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("匹配颜色", "目标 RGB。会和所选 AOV 源所在 packed texture 的 RGB 做距离匹配。"),
                        aovMatchColor.colorValue);
                    y += LineHeight + LineSpacing;
                    aovThreshold.floatValue = EditorGUI.Slider(
                        new Rect(rect.x, y, rect.width, LineHeight),
                        new GUIContent("颜色容差", "RGB 距离小于这个范围时被选中。"),
                        Mathf.Max(0.0f, aovThreshold.floatValue),
                        0.0f,
                        1.0f);
                    y += LineHeight + LineSpacing;
                    break;
            }

            invertAovMask.boolValue = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("反转", "只在当前 HoAOV 覆盖范围内反转，避免把背景也选中。"),
                invertAovMask.boolValue);
            y += LineHeight + LineSpacing;
            debugAovMask.boolValue = EditorGUI.Toggle(
                new Rect(rect.x, y, rect.width, LineHeight),
                new GUIContent("输出匹配结果", "直接输出当前 AOV 源和使用方式解析出的 mask，用于调试。"),
                debugAovMask.boolValue);
            y += LineHeight + LineSpacing;
        }

        private static void DrawPropertyLine(Rect rect, ref float y, SerializedProperty element, string propertyName, string label)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property == null)
            {
                return;
            }

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, LineHeight), property, new GUIContent(label));
            y += LineHeight + LineSpacing;
        }

        private void DrawEffectIconToggles()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            DrawEffectIconRow(VisibleEffectOrder);
        }

        private void DrawEffectIconRow(EffectToggleEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            float width = Mathf.Max(160.0f, EditorGUIUtility.currentViewWidth - 40.0f);
            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((width + EffectIconSpacing) / (EffectIconSize + EffectIconSpacing)));
            int rowCount = Mathf.CeilToInt(entries.Length / (float)buttonsPerRow);
            float height = rowCount * EffectIconSize + Mathf.Max(0, rowCount - 1) * EffectIconSpacing;

            Rect rect = GUILayoutUtility.GetRect(0.0f, height, GUILayout.ExpandWidth(true));
            float x = rect.x;
            float y = rect.y;
            int column = 0;

            foreach (EffectToggleEntry entry in entries)
            {
                if (column >= buttonsPerRow)
                {
                    column = 0;
                    x = rect.x;
                    y += EffectIconSize + EffectIconSpacing;
                }

                DrawEffectIconButton(new Rect(x, y, EffectIconSize, EffectIconSize), entry);
                x += EffectIconSize + EffectIconSpacing;
                column++;
            }
        }

        private void DrawEffectIconButton(Rect rect, EffectToggleEntry entry)
        {
            bool active = HasLayer(entry.Effect);
            GUIContent content = GetEffectIconContent(entry);
            Texture icon = content.image;

            if (icon != null)
            {
                Color oldColor = GUI.color;
                GUI.color = active ? new Color(0.35f, 1.0f, 0.35f, 1.0f) : Color.white;
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = oldColor;
            }
            else
            {
                EditorGUI.LabelField(rect, content);
            }

            GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                ToggleEffect(entry.Effect);
                Event.current.Use();
            }
        }

        private static GUIContent GetEffectIconContent(EffectToggleEntry entry)
        {
            if (EffectIconContents.TryGetValue(entry.Effect, out GUIContent cached))
            {
                return cached;
            }

            Texture2D icon = LoadEffectIcon(entry.IconName);
            GUIContent content = icon != null ? new GUIContent(icon, entry.Label) : new GUIContent(entry.Label);
            EffectIconContents[entry.Effect] = content;
            return content;
        }

        private static Texture2D LoadEffectIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            string[] candidatePaths =
            {
                $"{PackageAssetRoot}/Editor/ShoostIcons/{iconName}.png",
                $"Assets/Editor/ShoostIcons/{iconName}.png"
            };

            foreach (string path in candidatePaths)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        private void ToggleEffect(HoPostProcessEffect effect)
        {
            if (HasLayer(effect))
            {
                RemoveLayer(effect);
            }
            else
            {
                AddLayer(effect);
            }

            ApplyLayerListChanges();
        }

        private bool HasLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return false;
            }

            int effectIndex = (int)effect;
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if ((int)GetEffect(layerValues.GetArrayElementAtIndex(index)) == effectIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray || HasLayer(effect))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add HoPost Effect");
            int index = layerValues.arraySize;
            layerValues.InsertArrayElementAtIndex(index);
            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element, effect);
            element.isExpanded = true;
        }

        private void RemoveLayer(HoPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            int effectIndex = (int)effect;
            bool recordedUndo = false;
            for (int index = layerValues.arraySize - 1; index >= 0; index--)
            {
                if ((int)GetEffect(layerValues.GetArrayElementAtIndex(index)) != effectIndex)
                {
                    continue;
                }

                if (!recordedUndo)
                {
                    Undo.RecordObject(serializedObject.targetObject, "Remove HoPost Effect");
                    recordedUndo = true;
                }

                layerValues.DeleteArrayElementAtIndex(index);
            }
        }

        private void ApplyLayerListChanges()
        {
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerValues == null || index < 0 || index >= layerValues.arraySize)
            {
                return null;
            }

            return layerValues.GetArrayElementAtIndex(index);
        }

        private static GUIContent GetLayerLabel(SerializedProperty element)
        {
            string effectName = GetEffectDisplayName(GetEffect(element));
            return new GUIContent(effectName, $"效果类型: {effectName}");
        }

        private static HoPostProcessEffect GetEffect(SerializedProperty element)
        {
            SerializedProperty effect = element.FindPropertyRelative("effect");
            int value = effect != null ? effect.enumValueIndex : 0;
            return (HoPostProcessEffect)Mathf.Clamp(value, 0, 4);
        }

        private static string GetEffectDisplayName(HoPostProcessEffect effect)
        {
            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    return "边缘光";
                case HoPostProcessEffect.Outline:
                    return "轮廓";
                case HoPostProcessEffect.DropShadow:
                    return "投影";
                case HoPostProcessEffect.DepthOfField:
                    return "景深";
                case HoPostProcessEffect.CustomMaterial:
                default:
                    return "自定义";
            }
        }

        private static void ResetLayerDefaults(SerializedProperty element, HoPostProcessEffect effect)
        {
            SetBool(element, "enabled", true);
            SetEnum(element, "effect", (int)effect);
            SetString(element, "name", GetEffectDisplayName(effect));
            SetObjectReference(element, "materialOverride", null);
            SetObjectReference(element, "shaderOverride", null);
            SetObjectReference(element, "texture", null);
            SetInt(element, "passIndex", 0);
            SetFloat(element, "intensity", 1.0f);
            SetColor(element, "color", Color.white);
            SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
            SetVector4(element, "parameters0", Vector4.zero);
            SetVector4(element, "parameters1", Vector4.zero);
            SetVector4(element, "parameters2", Vector4.zero);
            SetVector4(element, "parameters3", Vector4.zero);
            SetVector4(element, "parameters4", Vector4.zero);
            SetVector4(element, "parameters5", Vector4.zero);
            SetBool(element, "useAovMask", false);
            SetEnum(element, "aovSource", (int)HoPostAovSource.Mask);
            SetEnum(element, "aovMaskMode", (int)HoPostAovMaskMode.Direct);
            SetFloat(element, "aovThreshold", 0.5f);
            SetFloat(element, "aovMatchValue", 0.0f);
            SetColor(element, "aovMatchColor", Color.white);
            SetBool(element, "invertAovMask", false);
            SetBool(element, "debugAovMask", false);
            HoPostAovMaskEditorUtility.ResetRules(element);

            switch (effect)
            {
                case HoPostProcessEffect.EdgeLight:
                    SetColor(element, "color", new Color(1.0f, 0.82f, 0.55f, 1.0f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Add);
                    SetVector4(element, "parameters0", new Vector4(0.45f, 2.0f, 0.35f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 0.65f, 0.45f, 1.0f));
                    break;
                case HoPostProcessEffect.Outline:
                    SetColor(element, "color", Color.black);
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(1.5f, 1.0f, 2.0f, 0.15f));
                    SetVector4(element, "parameters1", new Vector4(0.3f, 1.0f, 0.2f, 1.0f));
                    break;
                case HoPostProcessEffect.DropShadow:
                    SetColor(element, "color", new Color(0.0f, 0.0f, 0.0f, 0.65f));
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Multiply);
                    SetVector4(element, "parameters0", new Vector4(0.35f, -45.0f, 0.85f, 6.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    SetBool(element, "useAovMask", true);
                    break;
                case HoPostProcessEffect.DepthOfField:
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 10.0f, 50.0f, 5.6f));
                    SetVector4(element, "parameters1", new Vector4(10.0f, 30.0f, 8.0f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(5.0f, 1.0f, 0.0f, 0.0f));
                    break;
                case HoPostProcessEffect.CustomMaterial:
                    SetEnum(element, "blendMode", (int)HoPostProcessBlendMode.Normal);
                    break;
            }
        }

        private static void SetBool(SerializedProperty element, string propertyName, bool value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetEnum(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetString(SerializedProperty element, string propertyName, string value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.stringValue = value;
            }
        }

        private static void SetInt(SerializedProperty element, string propertyName, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedProperty element, string propertyName, float value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetColor(SerializedProperty element, string propertyName, Color value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.colorValue = value;
            }
        }

        private static void SetVector4(SerializedProperty element, string propertyName, Vector4 value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.vector4Value = value;
            }
        }

        private static void SetObjectReference(SerializedProperty element, string propertyName, Object value)
        {
            SerializedProperty property = element.FindPropertyRelative(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }
    }
}
