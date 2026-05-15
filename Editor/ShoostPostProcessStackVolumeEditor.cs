using System;
using System.Collections.Generic;
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
        private const float LevelAdjustmentInitMarker = -12345.0f;
        private const float EffectIconSize = 22.0f;
        private const float EffectIconSpacing = 2.0f;
        private const string PackageAssetRoot = "Packages/jp.lilxyzw.liltoon.urp.extensions";

        private static readonly ShoostPostProcessEffect[] FixedEffectOrder =
        {
            ShoostPostProcessEffect.CustomMaterial,
            ShoostPostProcessEffect.AutoWhiteBalance,
            ShoostPostProcessEffect.ChangeFrameRate,
            ShoostPostProcessEffect.ColorGradingCustom,
            ShoostPostProcessEffect.CRTEffects,
            ShoostPostProcessEffect.Distortion,
            ShoostPostProcessEffect.DitheringCustom,
            ShoostPostProcessEffect.DownScaleResolution,
            ShoostPostProcessEffect.FilmBreathGateWeave,
            ShoostPostProcessEffect.Fisheye,
            ShoostPostProcessEffect.GateWeave,
            ShoostPostProcessEffect.GrainCustom,
            ShoostPostProcessEffect.IrisBlur,
            ShoostPostProcessEffect.KawaseBlur,
            ShoostPostProcessEffect.LensDistortionCustom,
            ShoostPostProcessEffect.LevelAdjustment,
            ShoostPostProcessEffect.LUTColorGrading,
            ShoostPostProcessEffect.MotionTrail,
            ShoostPostProcessEffect.Pixelize,
            ShoostPostProcessEffect.RGBBlur,
            ShoostPostProcessEffect.RGBBlurV2,
            ShoostPostProcessEffect.RGBChannelSeparator,
            ShoostPostProcessEffect.RGBSplit,
            ShoostPostProcessEffect.SharpenBefore,
            ShoostPostProcessEffect.SharpenAfter,
            ShoostPostProcessEffect.Tube,
            ShoostPostProcessEffect.VignetteCustom,
            ShoostPostProcessEffect.RetroLookProBleedCustom,
            ShoostPostProcessEffect.RetroLookProNoise2Custom,
            ShoostPostProcessEffect.RetroLookProOldFilm2Custom,
            ShoostPostProcessEffect.RetroLookProTVEffectCustom
        };

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

        private static readonly Dictionary<ShoostPostProcessEffect, string> EffectIconNames = new Dictionary<ShoostPostProcessEffect, string>
        {
            { ShoostPostProcessEffect.CustomMaterial, "icon_LayerAdd_v1" },
            { ShoostPostProcessEffect.AutoWhiteBalance, "icon_WhiteBalance_v1" },
            { ShoostPostProcessEffect.ChangeFrameRate, "icon_FPS_v1" },
            { ShoostPostProcessEffect.ColorGradingCustom, "icon_ColorGrading_v1" },
            { ShoostPostProcessEffect.CRTEffects, "icon_TV_v1" },
            { ShoostPostProcessEffect.Distortion, "icon_Distortion_v1" },
            { ShoostPostProcessEffect.DitheringCustom, "icon_Pixel_v1" },
            { ShoostPostProcessEffect.DownScaleResolution, "icon_Monitor_v1" },
            { ShoostPostProcessEffect.FilmBreathGateWeave, "icon_Film_v3" },
            { ShoostPostProcessEffect.Fisheye, "icon_FishEye_v1" },
            { ShoostPostProcessEffect.GateWeave, "icon_Film_v2" },
            { ShoostPostProcessEffect.GrainCustom, "icon_Grain_v1" },
            { ShoostPostProcessEffect.IrisBlur, "icon_IrisBlur_v1" },
            { ShoostPostProcessEffect.KawaseBlur, "icon_Blur_v1" },
            { ShoostPostProcessEffect.LensDistortionCustom, "icon_Distortion_v1" },
            { ShoostPostProcessEffect.LevelAdjustment, "icon_LevelsAdjustment_v1" },
            { ShoostPostProcessEffect.LUTColorGrading, "icon_ColorGrading_v1" },
            { ShoostPostProcessEffect.MotionTrail, "icon_CameraSwitch_Move_01" },
            { ShoostPostProcessEffect.Pixelize, "icon_Pixel_v1" },
            { ShoostPostProcessEffect.RGBBlur, "icon_RGBBlur_v2" },
            { ShoostPostProcessEffect.RGBBlurV2, "icon_RGBBlur_v2" },
            { ShoostPostProcessEffect.RGBChannelSeparator, "icon_RGBChannel_RGB" },
            { ShoostPostProcessEffect.RGBSplit, "icon_RGBSplit_v1" },
            { ShoostPostProcessEffect.SharpenBefore, "icon_Sharpen_v1" },
            { ShoostPostProcessEffect.SharpenAfter, "icon_Sharpen_v1" },
            { ShoostPostProcessEffect.Tube, "icon_TV_v1" },
            { ShoostPostProcessEffect.VignetteCustom, "icon_Vignette_v1" },
            { ShoostPostProcessEffect.RetroLookProBleedCustom, "icon_Glow_v1" },
            { ShoostPostProcessEffect.RetroLookProNoise2Custom, "icon_Grain_v1" },
            { ShoostPostProcessEffect.RetroLookProOldFilm2Custom, "icon_Film_v4" },
            { ShoostPostProcessEffect.RetroLookProTVEffectCustom, "icon_TV_v1" }
        };

        private static readonly Dictionary<ShoostPostProcessEffect, GUIContent> EffectIconContents = new Dictionary<ShoostPostProcessEffect, GUIContent>();

        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;
        private bool showAdvancedSettings;

        public override void OnEnable()
        {
            PropertyFetcher<ShoostPostProcessStackVolume> fetcher = new PropertyFetcher<ShoostPostProcessStackVolume>(serializedObject);
            showInSceneView = Unpack(fetcher.Find(x => x.ShowInSceneView));
            layers = serializedObject.FindProperty("layers");
            layerValues = layers != null ? layers.FindPropertyRelative("m_Value") : null;
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            layerList = new ReorderableList(serializedObject, layerValues, false, false, false, false);
            layerList.drawHeaderCallback = null;
            layerList.headerHeight = 0.0f;
            layerList.elementHeightCallback = GetElementHeight;
            layerList.drawElementCallback = DrawElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SortLayersByEffectOrder();

            showAdvancedSettings = EditorGUILayout.ToggleLeft("高级", showAdvancedSettings);
            if (showAdvancedSettings)
            {
                PropertyField(showInSceneView, new GUIContent("场景视图"));
            }

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
            if (element == null || !element.isExpanded)
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

        private int GetElementLineCount(SerializedProperty element)
        {
            if (element == null)
            {
                return 1;
            }

            ShoostPostProcessEffect effect = GetEffect(element);
            bool showAdvanced = showAdvancedSettings;

            int lineCount = 1;

            switch (effect)
            {
                case ShoostPostProcessEffect.KawaseBlur:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1 + (GetKawaseBlurUsesCustomResolution(element) ? 2 : 0) + 3;
                    break;
                case ShoostPostProcessEffect.SharpenBefore:
                case ShoostPostProcessEffect.SharpenAfter:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ShoostPostProcessEffect.RGBSplit:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2 + (GetRgbSplitUsesAngle(element) ? 1 : 0);
                    break;
                case ShoostPostProcessEffect.IrisBlur:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 11 + (GetIrisBlurUsesCustomResolution(element) ? 2 : 0) + (GetIrisBlurUsesRgbBlur(element) ? 3 : 0);
                    break;
                case ShoostPostProcessEffect.VignetteCustom:
                    lineCount += GetCoreLineCount(false, GetVignetteCustomUsesTintMode(element), false, false, false, showAdvanced);
                    lineCount += 4;
                    break;
                case ShoostPostProcessEffect.LUTColorGrading:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 7;
                    break;
                case ShoostPostProcessEffect.LevelAdjustment:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 6;
                    break;
                case ShoostPostProcessEffect.CustomMaterial:
                    lineCount += GetCoreLineCount(true, true, true, true, true, showAdvanced);
                    break;
                default:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    break;
            }

            return lineCount;
        }

        private static int GetCoreLineCount(bool includeBlendMode, bool includeColor, bool includeTexture, bool includePassIndex, bool includeMaterialOverride, bool showAdvancedFields)
        {
            int count = 0;
            if (showAdvancedFields)
            {
                count += 2;
            }

            if (includeBlendMode)
            {
                count += 1;
            }

            if (includeColor)
            {
                count += 1;
            }

            if (includeTexture)
            {
                count += 1;
            }

            if (includeMaterialOverride)
            {
                count += 2;
            }

            if (includePassIndex)
            {
                count += 1;
            }

            return count;
        }

        private static bool GetKawaseBlurUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private static bool GetIrisBlurUsesRgbBlur(SerializedProperty element)
        {
            SerializedProperty parameters3 = element?.FindPropertyRelative("parameters3");
            if (parameters3 == null || parameters3.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return parameters3.vector4Value.x > 0.5f;
        }

        private static bool GetIrisBlurUsesCustomResolution(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private static bool GetRgbSplitUsesAngle(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 0;
        }

        private static bool GetVignetteCustomUsesTintMode(SerializedProperty element)
        {
            SerializedProperty passIndex = element?.FindPropertyRelative("passIndex");
            return passIndex != null && passIndex.propertyType == SerializedPropertyType.Integer && passIndex.intValue == 1;
        }

        private void DrawEffectIconToggles()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            float width = EditorGUIUtility.currentViewWidth - 40.0f;
            width = Mathf.Max(160.0f, width);
            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((width + EffectIconSpacing) / (EffectIconSize + EffectIconSpacing)));
            int rowCount = Mathf.CeilToInt(FixedEffectOrder.Length / (float)buttonsPerRow);
            float height = rowCount * EffectIconSize + Mathf.Max(0, rowCount - 1) * EffectIconSpacing;

            Rect rect = GUILayoutUtility.GetRect(0.0f, height, GUILayout.ExpandWidth(true));
            float x = rect.x;
            float y = rect.y;
            int column = 0;

            foreach (ShoostPostProcessEffect effect in FixedEffectOrder)
            {
                if (column >= buttonsPerRow)
                {
                    column = 0;
                    x = rect.x;
                    y += EffectIconSize + EffectIconSpacing;
                }

                Rect buttonRect = new Rect(x, y, EffectIconSize, EffectIconSize);
                DrawEffectIconButton(buttonRect, effect);
                x += EffectIconSize + EffectIconSpacing;
                column++;
            }
        }

        private void AddLayer(ShoostPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray || HasLayer(effect))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add Shoost Effect");

            int index = layerValues.arraySize;
            layerValues.InsertArrayElementAtIndex(index);

            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element);
            SetEnum(element, "effect", (int)effect);
            element.isExpanded = true;
            SortLayersByEffectOrder();
        }

        private void RemoveLayer(ShoostPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if (GetEffectIndex(layerValues.GetArrayElementAtIndex(index)) != (int)effect)
                {
                    continue;
                }

                Undo.RecordObject(serializedObject.targetObject, "Remove Shoost Effect");
                layerValues.DeleteArrayElementAtIndex(index);
                break;
            }
        }

        private void ToggleEffect(ShoostPostProcessEffect effect)
        {
            if (HasLayer(effect))
            {
                RemoveLayer(effect);
            }
            else
            {
                AddLayer(effect);
            }
        }

        private void SortLayersByEffectOrder()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            uint seenEffects = 0u;
            for (int index = layerValues.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                int effectIndex = GetEffectIndex(element);
                uint effectMask = effectIndex >= 0 && effectIndex < 32 ? 1u << effectIndex : 0u;
                if (effectMask != 0u && (seenEffects & effectMask) != 0u)
                {
                    layerValues.DeleteArrayElementAtIndex(index);
                    continue;
                }

                seenEffects |= effectMask;
            }

            for (int targetIndex = 0; targetIndex < layerValues.arraySize; targetIndex++)
            {
                int bestIndex = targetIndex;
                int bestOrder = GetEffectSortIndex(layerValues.GetArrayElementAtIndex(targetIndex));
                for (int candidateIndex = targetIndex + 1; candidateIndex < layerValues.arraySize; candidateIndex++)
                {
                    int candidateOrder = GetEffectSortIndex(layerValues.GetArrayElementAtIndex(candidateIndex));
                    if (candidateOrder < bestOrder)
                    {
                        bestIndex = candidateIndex;
                        bestOrder = candidateOrder;
                    }
                }

                if (bestIndex != targetIndex)
                {
                    layerValues.MoveArrayElement(bestIndex, targetIndex);
                }
            }
        }

        private bool HasLayer(ShoostPostProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return false;
            }

            int effectIndex = (int)effect;
            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if (GetEffectIndex(layerValues.GetArrayElementAtIndex(index)) == effectIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawEffectIconButton(Rect rect, ShoostPostProcessEffect effect)
        {
            bool active = HasLayer(effect);
            GUIContent content = GetEffectIconContent(effect);
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
                ToggleEffect(effect);
                Event.current.Use();
            }
        }

        private static GUIContent GetEffectIconContent(ShoostPostProcessEffect effect)
        {
            if (EffectIconContents.TryGetValue(effect, out GUIContent cached))
            {
                return cached;
            }

            Texture2D icon = LoadEffectIcon(effect);
            string label = GetEffectDisplayName(effect);
            GUIContent content = icon != null ? new GUIContent(icon, label) : new GUIContent(label);
            EffectIconContents[effect] = content;
            return content;
        }

        private static Texture2D LoadEffectIcon(ShoostPostProcessEffect effect)
        {
            if (!EffectIconNames.TryGetValue(effect, out string iconName) || string.IsNullOrEmpty(iconName))
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
            return new GUIContent(effectName, $"效果类型：{effectName}");
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

        private static int GetEffectIndex(SerializedProperty layer)
        {
            return (int)GetEffect(layer);
        }

        private static int GetEffectSortIndex(SerializedProperty layer)
        {
            ShoostPostProcessEffect effect = GetEffect(layer);
            for (int index = 0; index < FixedEffectOrder.Length; index++)
            {
                if (FixedEffectOrder[index] == effect)
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private void DrawVignetteCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty passIndex = element.FindPropertyRelative("passIndex");
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureVignetteCustomDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawPopupLine(rect.x, y, rect.width, passIndex, "模式", new[] { "压暗", "染色" });
            y = DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: false,
                includeColor: GetVignetteCustomUsesTintMode(element),
                includeTexture: false,
                includePassIndex: false,
                includeMaterialOverride: false,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);

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
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureLutColorGradingDefaults(parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);
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
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureLevelAdjustmentDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

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
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureKawaseBlurDefaults(parameters0, parameters1);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

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
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            bool isCustomMaterial = GetEffect(element) == ShoostPostProcessEffect.CustomMaterial;
            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawLayerCoreFields(
                rect.x,
                y,
                rect.width,
                element,
                includeBlendMode: isCustomMaterial,
                includeColor: isCustomMaterial,
                includeTexture: isCustomMaterial,
                includePassIndex: isCustomMaterial,
                includeMaterialOverride: isCustomMaterial,
                includeParameters: false,
                showAdvancedFields: showAdvancedSettings);
            EditorGUI.indentLevel--;
        }

        private void DrawSharpenElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            EnsureSharpenDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 sharpenParams = parameters0.vector4Value;
            sharpenParams.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化强度", sharpenParams.x, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            parameters0.vector4Value = sharpenParams;
            EditorGUI.indentLevel--;
        }

        private void DrawRgbSplitElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            EnsureRgbSplitDefaults(parameters0);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 splitParams = parameters0.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(splitParams.x), 0, 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, new[] { "RGB 分离", "径向色差" });
            splitParams.x = mode;
            y += LineHeight + LineSpacing;

            splitParams.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分离强度", splitParams.y, 0.0f, 1.0f);
            y += LineHeight + LineSpacing;
            if (mode == 0)
            {
                splitParams.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "角度", splitParams.z, 0.0f, 360.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = splitParams;
            EditorGUI.indentLevel--;
        }

        private void DrawIrisBlurElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureIrisBlurDefaults(parameters0, parameters1, parameters2, parameters3);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

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

            bool useRgbBlur = irisParams3.x > 0.5f;
            useRgbBlur = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "启用 RGB 模糊", useRgbBlur);
            irisParams3.x = useRgbBlur ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;
            if (useRgbBlur)
            {
                irisParams3.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "红通道模糊半径", irisParams3.y, 0.0f, 5.0f);
                y += LineHeight + LineSpacing;
                irisParams3.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "绿通道模糊半径", irisParams3.z, 0.0f, 5.0f);
                y += LineHeight + LineSpacing;
                irisParams3.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "蓝通道模糊半径", irisParams3.w, 0.0f, 5.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = irisParams0;
            parameters1.vector4Value = irisParams1;
            parameters2.vector4Value = irisParams2;
            parameters3.vector4Value = irisParams3;
            EditorGUI.indentLevel--;
        }

        private float DrawFoldoutLine(Rect rect, float y, SerializedProperty element, SerializedProperty enabled)
        {
            Rect lineRect = new Rect(rect.x, y, rect.width, LineHeight);
            float checkboxWidth = 18.0f;
            SerializedProperty intensity = element.FindPropertyRelative("intensity");
            float intensityWidth = Mathf.Clamp(rect.width * 0.34f, 140.0f, 220.0f);
            float foldoutWidth = Mathf.Max(0.0f, rect.width - checkboxWidth - intensityWidth - 6.0f);

            if (enabled != null)
            {
                Rect enabledRect = new Rect(lineRect.x, lineRect.y, checkboxWidth, lineRect.height);
                enabled.boolValue = EditorGUI.Toggle(enabledRect, enabled.boolValue);
            }

            Rect foldoutRect = new Rect(lineRect.x + checkboxWidth, lineRect.y, foldoutWidth, lineRect.height);
            element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GetLayerLabel(element), true);

            if (intensity != null && intensity.propertyType == SerializedPropertyType.Float)
            {
                Rect intensityRect = new Rect(lineRect.xMax - intensityWidth, lineRect.y, intensityWidth, lineRect.height);
                Rect sliderRect = new Rect(intensityRect.x, intensityRect.y + 2.0f, intensityRect.width, intensityRect.height - 4.0f);
                intensity.floatValue = GUI.HorizontalSlider(sliderRect, intensity.floatValue, 0.0f, 1.0f);
            }

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
            bool includeMaterialOverride,
            bool includeParameters,
            bool showAdvancedFields)
        {
            SerializedProperty showInSceneView = element.FindPropertyRelative("showInSceneView");
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

            if (showAdvancedFields)
            {
                y = DrawPropertyLine(x, y, width, showInSceneView, "场景视图");
                y = DrawPropertyLine(x, y, width, injectionPoint, "插入位置");
            }

            if (includeBlendMode)
            {
                y = DrawBlendModeLine(x, y, width, blendMode);
            }

            if (includeColor)
            {
                y = DrawPropertyLine(x, y, width, color, "颜色");
            }

            if (includeTexture)
            {
                y = DrawPropertyLine(x, y, width, texture, "纹理");
            }

            if (includeMaterialOverride)
            {
                y = DrawPropertyLine(x, y, width, materialOverride, "材质覆盖");
                y = DrawPropertyLine(x, y, width, shaderOverride, "Shader 覆盖");
            }

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

            bool needsReset = Mathf.Abs(channelOutputParams.w - LevelAdjustmentInitMarker) > 0.001f;
            if (needsReset)
            {
                rgbParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
                rgbModeParams = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                channelParams = new Vector4(0.0f, 1.0f, 1.0f, 0.0f);
                channelOutputParams = new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker);
            }

            parameters0.vector4Value = rgbParams;
            parameters1.vector4Value = rgbModeParams;
            parameters2.vector4Value = channelParams;
            parameters3.vector4Value = channelOutputParams;
        }

    }
}
