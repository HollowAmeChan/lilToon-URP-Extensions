using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ShoostPostProcessStackVolume))]
    internal sealed class ShoostPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;

        private static readonly string[] EffectDisplayNames =
        {
            "自定义材质",
            "自动白平衡",
            "变更帧率",
            "自定义调色",
            "CRT",
            "扭曲",
            "自定义抖动",
            "降分辨率",
            "胶片呼吸/网格抖动",
            "鱼眼",
            "画幅抖动",
            "自定义颗粒",
            "虹膜模糊",
            "Kawase 模糊",
            "镜头畸变",
            "色阶",
            "LUT 调色",
            "运动拖影",
            "像素化",
            "RGB 模糊",
            "RGB 模糊 V2",
            "RGB 通道分离",
            "RGB 分离",
            "锐化（前）",
            "锐化（后）",
            "Tube",
            "暗角",
            "RetroLookPro Bleed",
            "RetroLookPro Noise2",
            "RetroLookPro Old Film 2",
            "RetroLookPro TV Effect"
        };

        private static readonly GUIContent[] BlendModeDisplayNames =
        {
            new GUIContent("正常"),
            new GUIContent("相加"),
            new GUIContent("正片叠底"),
            new GUIContent("滤色"),
            new GUIContent("变暗"),
            new GUIContent("颜色加深"),
            new GUIContent("线性加深"),
            new GUIContent("变亮"),
            new GUIContent("颜色减淡"),
            new GUIContent("叠加"),
            new GUIContent("柔光"),
            new GUIContent("强光"),
            new GUIContent("亮光"),
            new GUIContent("线性光"),
            new GUIContent("点光"),
            new GUIContent("实色混合"),
            new GUIContent("差值"),
            new GUIContent("排除"),
            new GUIContent("减去"),
            new GUIContent("划分"),
            new GUIContent("色相"),
            new GUIContent("饱和度"),
            new GUIContent("颜色"),
            new GUIContent("明度")
        };

        private static readonly int[] BlendModeValues =
        {
            (int)ShoostPostProcessBlendMode.Normal,
            (int)ShoostPostProcessBlendMode.Add,
            (int)ShoostPostProcessBlendMode.Multiply,
            (int)ShoostPostProcessBlendMode.Screen,
            (int)ShoostPostProcessBlendMode.Darken,
            (int)ShoostPostProcessBlendMode.ColorBurn,
            (int)ShoostPostProcessBlendMode.LinearBurn,
            (int)ShoostPostProcessBlendMode.Lighten,
            (int)ShoostPostProcessBlendMode.ColorDodge,
            (int)ShoostPostProcessBlendMode.Overlay,
            (int)ShoostPostProcessBlendMode.SoftLight,
            (int)ShoostPostProcessBlendMode.HardLight,
            (int)ShoostPostProcessBlendMode.VividLight,
            (int)ShoostPostProcessBlendMode.LinearLight,
            (int)ShoostPostProcessBlendMode.PinLight,
            (int)ShoostPostProcessBlendMode.HardMix,
            (int)ShoostPostProcessBlendMode.Difference,
            (int)ShoostPostProcessBlendMode.Exclusion,
            (int)ShoostPostProcessBlendMode.Subtract,
            (int)ShoostPostProcessBlendMode.Divide,
            (int)ShoostPostProcessBlendMode.Hue,
            (int)ShoostPostProcessBlendMode.Saturation,
            (int)ShoostPostProcessBlendMode.Color,
            (int)ShoostPostProcessBlendMode.Luminosity
        };

        private SerializedDataParameter enable;
        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<ShoostPostProcessStackVolume> fetcher = new PropertyFetcher<ShoostPostProcessStackVolume>(serializedObject);
            enable = Unpack(fetcher.Find(x => x.Enable));
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, true, true, true, true);
            layerList.drawHeaderCallback = DrawHeader;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
            layerList.onAddCallback = AddLayer;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PropertyField(enable, new GUIContent("启用"));
            PropertyField(showInSceneView, new GUIContent("场景视图"));

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

            SerializedProperty overrideState = layers.FindPropertyRelative("m_OverrideState");
            if (overrideState != null)
            {
                EditorGUILayout.PropertyField(overrideState, new GUIContent("覆盖图层列表"));
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

        private static void DrawHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "图层");
        }

        private float GetElementHeight(int index)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null || !element.isExpanded)
            {
                return LineHeight + 6.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.KawaseBlur)
            {
                return (LineHeight + LineSpacing) * 16.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.SharpenBefore || GetEffect(element) == ShoostPostProcessEffect.SharpenAfter)
            {
                return (LineHeight + LineSpacing) * 10.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.RGBSplit)
            {
                return (LineHeight + LineSpacing) * 12.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.IrisBlur)
            {
                return (LineHeight + LineSpacing) * 25.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
            {
                return (LineHeight + LineSpacing) * 16.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.LUTColorGrading)
            {
                return (LineHeight + LineSpacing) * 17.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.LevelAdjustment)
            {
                return (LineHeight + LineSpacing) * 16.0f + 12.0f;
            }

            return (LineHeight + LineSpacing) * 18.0f + 12.0f;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = GetLayerProperty(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2.0f;
            if (GetEffect(element) == ShoostPostProcessEffect.KawaseBlur)
            {
                DrawKawaseBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.SharpenBefore || GetEffect(element) == ShoostPostProcessEffect.SharpenAfter)
            {
                DrawSharpenElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.RGBSplit)
            {
                DrawRgbSplitElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.IrisBlur)
            {
                DrawIrisBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
            {
                DrawVignetteCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.LUTColorGrading)
            {
                DrawLutColorGradingElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.LevelAdjustment)
            {
                DrawLevelAdjustmentElement(rect, element);
            }
            else
            {
                DrawSimpleLayerElement(rect, element);
            }
        }

        private void AddLayer(ReorderableList list)
        {
            if (list == null || list.serializedProperty == null)
            {
                return;
            }

            serializedObject.Update();

            SerializedProperty array = list.serializedProperty;
            int index = array.arraySize;
            array.InsertArrayElementAtIndex(index);

            SerializedProperty element = array.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element);
            element.isExpanded = true;
            list.index = index;

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty GetLayerProperty(int index)
        {
            if (layerList == null || layerList.serializedProperty == null || index < 0 || index >= layerList.serializedProperty.arraySize)
            {
                return null;
            }

            return layerList.serializedProperty.GetArrayElementAtIndex(index);
        }

        private static GUIContent GetLayerLabel(SerializedProperty layer)
        {
            string effectName = GetEffectDisplayName(GetEffect(layer));

            SerializedProperty nameProperty = layer.FindPropertyRelative("name");
            string layerName = nameProperty != null ? nameProperty.stringValue : string.Empty;
            string tooltip = string.IsNullOrWhiteSpace(layerName) ? "图层名称为空" : $"图层名称：{layerName}";
            return new GUIContent(effectName, tooltip);
        }

        private static string GetEffectDisplayName(ShoostPostProcessEffect effect)
        {
            int index = (int)effect;
            if (index >= 0 && index < EffectDisplayNames.Length)
            {
                return EffectDisplayNames[index];
            }

            return effect.ToString();
        }

        private static ShoostPostProcessEffect GetEffect(SerializedProperty layer)
        {
            SerializedProperty effectProperty = layer.FindPropertyRelative("effect");
            if (effectProperty == null || effectProperty.propertyType != SerializedPropertyType.Enum)
            {
                return ShoostPostProcessEffect.CustomMaterial;
            }

            int effectIndex = effectProperty.enumValueIndex;
            if (effectIndex < 0 || effectIndex >= System.Enum.GetValues(typeof(ShoostPostProcessEffect)).Length)
            {
                return ShoostPostProcessEffect.CustomMaterial;
            }

            return (ShoostPostProcessEffect)effectIndex;
        }

        private void DrawVignetteCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty passIndex = element.FindPropertyRelative("passIndex");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");

            EnsureVignetteCustomDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: true, includeTexture: false, includePassIndex: true, includeParameters: false);

            y = DrawPopupLine(rect.x, y, rect.width, passIndex, "模式", new[] { "压暗", "染色" });

            Vector4 vignetteParams = parameters0.vector4Value;
            y = DrawSliderLine(rect.x, y, rect.width, "中心 X", vignetteParams.x, 0.0f, 1.0f, value => vignetteParams.x = value);
            y = DrawSliderLine(rect.x, y, rect.width, "中心 Y", vignetteParams.y, 0.0f, 1.0f, value => vignetteParams.y = value);
            y = DrawSliderLine(rect.x, y, rect.width, "半径", vignetteParams.z, 0.0f, 1.0f, value => vignetteParams.z = value);
            y = DrawSliderLine(rect.x, y, rect.width, "柔和度", vignetteParams.w, 0.0f, 1.0f, value => vignetteParams.w = value);
            parameters0.vector4Value = vignetteParams;

            EditorGUI.indentLevel--;
        }

        private void DrawLutColorGradingElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");

            EnsureLutColorGradingDefaults(parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);
            y = DrawPropertyLine(rect.x, y, rect.width, texture, "LUT 贴图");

            Vector4 colorParams = parameters0.vector4Value;
            Vector4 contributionParams = parameters1.vector4Value;
            colorParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色温", colorParams.x, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            colorParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "色调", colorParams.y, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            colorParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "饱和度", colorParams.z, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            colorParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "亮度", colorParams.w, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            contributionParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "对比度", contributionParams.x, -100.0f, 100.0f);
            y += LineHeight + LineSpacing;
            contributionParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "LUT 贡献", contributionParams.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = colorParams;
            parameters1.vector4Value = contributionParams;
            EditorGUI.indentLevel--;
        }

        private void DrawLevelAdjustmentElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");

            EnsureLevelAdjustmentDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);

            Vector4 rgbParams = parameters0.vector4Value;
            Vector4 rgbModeParams = parameters1.vector4Value;
            Vector4 channelParams = parameters2.vector4Value;
            Vector4 channelOutputParams = parameters3.vector4Value;

            int channel = Mathf.Clamp(Mathf.RoundToInt(rgbModeParams.y), 0, 3);
            channel = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "调整范围", channel, new[] { "RGB", "红通道", "绿通道", "蓝通道" });
            rgbModeParams.y = channel;
            y += LineHeight + LineSpacing;

            if (channel == 0)
            {
                rgbParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入黑场", rgbParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入白场", rgbParams.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "伽马", rgbParams.z, 0.01f, 10.0f);
                y += LineHeight + LineSpacing;
                rgbParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出黑场", rgbParams.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                rgbModeParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出白场", rgbModeParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }
            else
            {
                channelParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入黑场", channelParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输入白场", channelParams.y, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "伽马", channelParams.z, 0.01f, 10.0f);
                y += LineHeight + LineSpacing;
                channelParams.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出黑场", channelParams.w, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
                channelOutputParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "输出白场", channelOutputParams.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = rgbParams;
            parameters1.vector4Value = rgbModeParams;
            parameters2.vector4Value = channelParams;
            parameters3.vector4Value = channelOutputParams;
            EditorGUI.indentLevel--;
        }

        private void DrawKawaseBlurElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");

            EnsureKawaseBlurDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);

            Vector4 blurParams0 = parameters0.vector4Value;
            Vector4 blurParams1 = parameters1.vector4Value;

            int resolutionMode = Mathf.Clamp(Mathf.RoundToInt(blurParams0.x), 0, 1);
            resolutionMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率模式", resolutionMode, new[] { "游戏视图", "自定义尺寸" });
            blurParams0.x = resolutionMode;
            y += LineHeight + LineSpacing;

            if (resolutionMode == 1)
            {
                blurParams0.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(blurParams0.y), 1, 8192));
                y += LineHeight + LineSpacing;
                blurParams0.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(blurParams0.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            blurParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", blurParams1.x, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;

            blurParams1.y = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "降采样", Mathf.RoundToInt(blurParams1.y), 1, 8);
            y += LineHeight + LineSpacing;

            blurParams1.z = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "迭代次数", Mathf.RoundToInt(blurParams1.z), 1, 10);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = blurParams0;
            parameters1.vector4Value = blurParams1;
            EditorGUI.indentLevel--;
        }

        private void DrawSimpleLayerElement(Rect rect, SerializedProperty element)
        {
            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: true, includeColor: true, includeTexture: true, includePassIndex: true, includeParameters: true);
            EditorGUI.indentLevel--;
        }

        private void DrawSharpenElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            EnsureSharpenDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);

            Vector4 sharpenParams = parameters0.vector4Value;
            sharpenParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化强度", sharpenParams.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = sharpenParams;
            EditorGUI.indentLevel--;
        }

        private void DrawRgbSplitElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            EnsureRgbSplitDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);

            Vector4 splitParams = parameters0.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(splitParams.x), 0, 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, new[] { "RGB 分离", "径向色差" });
            splitParams.x = mode;
            y += LineHeight + LineSpacing;

            splitParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分离强度", splitParams.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            splitParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", splitParams.z, 0.0f, 360.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = splitParams;
            EditorGUI.indentLevel--;
        }

        private void DrawIrisBlurElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");

            EnsureIrisBlurDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeParameters: false);

            Vector4 irisParams0 = parameters0.vector4Value;
            Vector4 irisParams1 = parameters1.vector4Value;
            Vector4 irisParams2 = parameters2.vector4Value;
            Vector4 irisParams3 = parameters3.vector4Value;

            int resolutionMode = Mathf.Clamp(Mathf.RoundToInt(irisParams0.x), 0, 1);
            resolutionMode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "分辨率模式", resolutionMode, new[] { "游戏视图", "自定义尺寸" });
            irisParams0.x = resolutionMode;
            y += LineHeight + LineSpacing;

            if (resolutionMode == 1)
            {
                irisParams0.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义宽度", Mathf.RoundToInt(irisParams0.y), 1, 8192));
                y += LineHeight + LineSpacing;
                irisParams0.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "自定义高度", Mathf.RoundToInt(irisParams0.z), 1, 8192));
                y += LineHeight + LineSpacing;
            }

            irisParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "距离", irisParams0.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            irisParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "半径", irisParams1.x, 0.0f, 10.0f);
            y += LineHeight + LineSpacing;
            irisParams1.y = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "降采样", Mathf.RoundToInt(irisParams1.y), 1, 4));
            y += LineHeight + LineSpacing;
            irisParams1.z = Mathf.Max(1.0f, EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "迭代次数", Mathf.RoundToInt(irisParams1.z), 1, 8));
            y += LineHeight + LineSpacing;
            irisParams1.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", irisParams1.w, 0.0f, 360.0f);
            y += LineHeight + LineSpacing;

            irisParams2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 X", irisParams2.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            irisParams2.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心 Y", irisParams2.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            irisParams2.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "中心范围", irisParams2.z, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            irisParams2.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "柔和度", irisParams2.w, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            irisParams3.x = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "启用 RGB 模糊", irisParams3.x > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            irisParams3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "红通道模糊半径", irisParams3.y, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            irisParams3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "绿通道模糊半径", irisParams3.z, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;
            irisParams3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "蓝通道模糊半径", irisParams3.w, 0.0f, 5.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = irisParams0;
            parameters1.vector4Value = irisParams1;
            parameters2.vector4Value = irisParams2;
            parameters3.vector4Value = irisParams3;
            EditorGUI.indentLevel--;
        }

        private float DrawFoldoutLine(Rect rect, float y, SerializedProperty element)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            element.isExpanded = EditorGUI.Foldout(lineRect, element.isExpanded, GetLayerLabel(element), true);
            return y + LineHeight + LineSpacing;
        }

        private float DrawLayerCoreFields(
            float x,
            float y,
            float width,
            SerializedProperty element,
            bool includeBlendMode,
            bool includeColor,
            bool includeTexture,
            bool includePassIndex,
            bool includeParameters)
        {
            SerializedProperty layerName = element.FindPropertyRelative("name");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            SerializedProperty effect = element.FindPropertyRelative("effect");
            SerializedProperty showInSceneView = element.FindPropertyRelative("showInSceneView");
            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
            SerializedProperty injectionPoint = element.FindPropertyRelative("injectionPoint");
            SerializedProperty color = element.FindPropertyRelative("color");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty materialOverride = element.FindPropertyRelative("materialOverride");
            SerializedProperty shaderOverride = element.FindPropertyRelative("shaderOverride");
            SerializedProperty passIndex = element.FindPropertyRelative("passIndex");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");

            y = DrawPropertyLine(x, y, width, layerName, "图层名称");
            y = DrawPropertyLine(x, y, width, enabled, "启用");
            y = DrawEffectLine(x, y, width, effect);
            y = DrawPropertyLine(x, y, width, showInSceneView, "场景视图");
            y = DrawPropertyLine(x, y, width, intensity, "强度");

            if (includeBlendMode)
            {
                y = DrawBlendModeLine(x, y, width, blendMode);
            }

            y = DrawPropertyLine(x, y, width, injectionPoint, "插入位置");

            if (includeColor)
            {
                y = DrawPropertyLine(x, y, width, color, "颜色");
            }

            if (includeTexture)
            {
                y = DrawPropertyLine(x, y, width, texture, "纹理");
            }

            y = DrawPropertyLine(x, y, width, materialOverride, "材质覆盖");
            y = DrawPropertyLine(x, y, width, shaderOverride, "Shader 覆盖");

            if (includePassIndex)
            {
                y = DrawPropertyLine(x, y, width, passIndex, "Pass 索引");
            }

            if (includeParameters)
            {
                y = DrawPropertyLine(x, y, width, parameters0, "参数 0");
                y = DrawPropertyLine(x, y, width, parameters1, "参数 1");
                y = DrawPropertyLine(x, y, width, parameters2, "参数 2");
                y = DrawPropertyLine(x, y, width, parameters3, "参数 3");
            }

            return y;
        }

        private static float DrawEffectLine(float x, float y, float width, SerializedProperty property)
        {
            if (property == null)
            {
                return y;
            }

            Rect lineRect = new Rect(x, y, width, LineHeight);
            int index = Mathf.Clamp(property.enumValueIndex, 0, EffectDisplayNames.Length - 1);
            index = EditorGUI.Popup(lineRect, "效果类型", index, EffectDisplayNames);
            property.enumValueIndex = index;
            return y + LineHeight + LineSpacing;
        }

        private static float DrawPopupLine(float x, float y, float width, SerializedProperty property, string label, string[] options)
        {
            if (property == null)
            {
                return y;
            }

            Rect lineRect = new Rect(x, y, width, LineHeight);
            int value = Mathf.Clamp(property.intValue, 0, options.Length - 1);
            value = EditorGUI.Popup(lineRect, label, value, options);
            property.intValue = value;
            return y + LineHeight + LineSpacing;
        }

        private static float DrawBlendModeLine(float x, float y, float width, SerializedProperty property)
        {
            if (property == null)
            {
                return y;
            }

            Rect lineRect = new Rect(x, y, width, LineHeight);
            int selected = System.Array.IndexOf(BlendModeValues, property.intValue);
            if (selected < 0)
            {
                selected = 0;
            }

            selected = EditorGUI.Popup(lineRect, new GUIContent("混合模式"), selected, BlendModeDisplayNames);
            property.intValue = BlendModeValues[Mathf.Clamp(selected, 0, BlendModeValues.Length - 1)];
            return y + LineHeight + LineSpacing;
        }

        private static float DrawSliderLine(float x, float y, float width, string label, float value, float min, float max, System.Action<float> setter)
        {
            Rect lineRect = new Rect(x, y, width, LineHeight);
            float newValue = EditorGUI.Slider(lineRect, label, value, min, max);
            setter?.Invoke(newValue);
            return y + LineHeight + LineSpacing;
        }

        private static float DrawPropertyLine(float x, float y, float width, SerializedProperty property, string label)
        {
            if (property == null)
            {
                return y;
            }

            Rect lineRect = new Rect(x, y, width, LineHeight);
            EditorGUI.PropertyField(lineRect, property, new GUIContent(label));
            return y + LineHeight + LineSpacing;
        }

        private static void EnsureKawaseBlurDefaults(SerializedProperty parameters0, SerializedProperty parameters1)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters0.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(0.5f, 2.0f, 6.0f, 0.0f);
                }
            }
        }

        private static void EnsureSharpenDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.2f, 0.0f, 0.0f, 0.0f);
            }
        }

        private static void EnsureRgbSplitDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.35f, 0.0f, 0.0f);
            }
        }

        private static void EnsureIrisBlurDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty parameters3)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters0.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters0.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, 0.15f);
                }
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters1.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters1.vector4Value = new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
                }
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters2.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters2.vector4Value = new Vector4(0.5f, 0.5f, 0.35f, 0.25f);
                }
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 value = parameters3.vector4Value;
                if (value.sqrMagnitude <= 0.000001f)
                {
                    parameters3.vector4Value = new Vector4(0.0f, 1.0f, 1.0f, 1.0f);
                }
            }
        }

        private static void EnsureVignetteCustomDefaults(SerializedProperty parameters0)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters0.vector4Value;
            if (value.sqrMagnitude > 0.000001f)
            {
                return;
            }

            parameters0.vector4Value = new Vector4(0.5f, 0.5f, 0.35f, 0.25f);
        }

        private static void ResetLayerDefaults(SerializedProperty element)
        {
            if (element == null)
            {
                return;
            }

            SetString(element, "name", "Post Process Layer");
            SetBool(element, "enabled", true);
            SetEnum(element, "effect", (int)ShoostPostProcessEffect.CustomMaterial);
            SetBool(element, "showInSceneView", true);
            SetObjectReference(element, "materialOverride", null);
            SetObjectReference(element, "shaderOverride", null);
            SetInt(element, "passIndex", 0);
            SetFloat(element, "intensity", 1.0f);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.Normal);
            SetEnum(element, "injectionPoint", (int)ShoostPostProcessInjectionPoint.EffectDefault);
            SetColor(element, "color", Color.white);
            SetObjectReference(element, "texture", null);
            SetVector4(element, "parameters0", Vector4.zero);
            SetVector4(element, "parameters1", Vector4.zero);
            SetVector4(element, "parameters2", Vector4.zero);
            SetVector4(element, "parameters3", Vector4.zero);
        }

        private static void SetString(SerializedProperty element, string name, string value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.String)
            {
                property.stringValue = value;
            }
        }

        private static void SetBool(SerializedProperty element, string name, bool value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean)
            {
                property.boolValue = value;
            }
        }

        private static void SetInt(SerializedProperty element, string name, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = value;
            }
        }

        private static void SetFloat(SerializedProperty element, string name, float value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value;
            }
        }

        private static void SetEnum(SerializedProperty element, string name, int value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Enum)
            {
                property.enumValueIndex = value;
            }
        }

        private static void SetColor(SerializedProperty element, string name, Color value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Color)
            {
                property.colorValue = value;
            }
        }

        private static void SetObjectReference(SerializedProperty element, string name, UnityEngine.Object value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetVector4(SerializedProperty element, string name, Vector4 value)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            if (property != null && property.propertyType == SerializedPropertyType.Vector4)
            {
                property.vector4Value = value;
            }
        }

        private static void EnsureLutColorGradingDefaults(SerializedProperty parameters1)
        {
            if (parameters1 == null || parameters1.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 value = parameters1.vector4Value;
            if (value.sqrMagnitude <= 0.000001f)
            {
                value.y = 1.0f;
                parameters1.vector4Value = value;
            }
        }

        private static void EnsureLevelAdjustmentDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty parameters3)
        {
            if (parameters0 == null || parameters1 == null || parameters2 == null || parameters3 == null)
            {
                return;
            }

            if (parameters0.propertyType != SerializedPropertyType.Vector4
                || parameters1.propertyType != SerializedPropertyType.Vector4
                || parameters2.propertyType != SerializedPropertyType.Vector4
                || parameters3.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            Vector4 rgbParams = parameters0.vector4Value;
            Vector4 rgbModeParams = parameters1.vector4Value;
            Vector4 channelParams = parameters2.vector4Value;
            Vector4 channelOutputParams = parameters3.vector4Value;

            if (rgbParams.sqrMagnitude <= 0.000001f)
            {
                rgbParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
            }

            if (rgbModeParams.sqrMagnitude <= 0.000001f)
            {
                rgbModeParams = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }

            if (channelParams.sqrMagnitude <= 0.000001f)
            {
                channelParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
            }

            if (channelOutputParams.sqrMagnitude <= 0.000001f)
            {
                channelOutputParams = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
            }

            parameters0.vector4Value = rgbParams;
            parameters1.vector4Value = rgbModeParams;
            parameters2.vector4Value = channelParams;
            parameters3.vector4Value = channelOutputParams;
        }

    }
}
