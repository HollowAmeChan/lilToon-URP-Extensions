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

            if (GetEffect(element) == ShoostPostProcessEffect.IrisBlur)
            {
                return (LineHeight + LineSpacing) * 25.0f + 12.0f;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
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

            if (GetEffect(element) == ShoostPostProcessEffect.IrisBlur)
            {
                DrawIrisBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
            {
                DrawVignetteCustomElement(rect, element);
            }
            else
            {
                DrawSimpleLayerElement(rect, element);
            }
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
                y = DrawPropertyLine(x, y, width, blendMode, "混合模式");
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

    }
}
