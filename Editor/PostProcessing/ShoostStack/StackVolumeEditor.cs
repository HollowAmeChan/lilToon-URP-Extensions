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
    internal sealed partial class ShoostPostProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float LevelAdjustmentInitMarker = -12345.0f;
        private const float RgbSplitInitMarker = -12346.0f;
        private const float EffectIconSize = 22.0f;
        private const float EffectIconSpacing = 2.0f;
        private const float ColorWheelMinSize = 64.0f;
        private const float ColorWheelMaxSize = 120.0f;
        private const float ColorWheelGap = 4.0f;
        private const string TrackballShaderName = "Hidden/Universal Render Pipeline/Editor/Trackball";
        private const string DefaultDistortionTextureGuid = "f4c1f3c21e3ec4a479c69cffea26c6cd";
        private const string DefaultVhsEdgeNoiseTextureGuid = "014de9bcc7cd0a148929d7e58755ee44";
        private const string PackageAssetRoot = "Packages/jp.lilxyzw.liltoon.urp.extensions";
        private static Texture2D colorWheelTexture;
        private static GUIStyle colorWheelThumbStyle;
        private static Vector2 colorWheelThumbSize;
        private static Material trackballMaterial;

        private readonly struct EffectToggleEntry
        {
            public readonly ShoostPostProcessEffect Effect;
            public readonly string Label;
            public readonly string IconName;

            public EffectToggleEntry(ShoostPostProcessEffect effect, string label, string iconName)
            {
                Effect = effect;
                Label = label;
                IconName = iconName;
            }
        }

        private static readonly ShoostPostProcessEffect[] FixedEffectOrder =
        {
            ShoostPostProcessEffect.SharpenBefore,
            ShoostPostProcessEffect.AutoWhiteBalance,
            ShoostPostProcessEffect.LevelAdjustment,
            ShoostPostProcessEffect.ColorGradingCustom,
            ShoostPostProcessEffect.EdgeLight,
            ShoostPostProcessEffect.Outline,
            ShoostPostProcessEffect.DropShadow,
            ShoostPostProcessEffect.Gradient,
            ShoostPostProcessEffect.Glow,
            ShoostPostProcessEffect.Lighting,
            ShoostPostProcessEffect.CenterColorCorrection,
            ShoostPostProcessEffect.LED,
            ShoostPostProcessEffect.Weather,
            ShoostPostProcessEffect.Particle,
            ShoostPostProcessEffect.CameraSwitcher,
            ShoostPostProcessEffect.TransparentBackground,
            ShoostPostProcessEffect.FilmBreathGateWeave,
            ShoostPostProcessEffect.Tube,
            ShoostPostProcessEffect.VHS,
            ShoostPostProcessEffect.CRTEffects,
            ShoostPostProcessEffect.DitheringCustom,
            ShoostPostProcessEffect.IrisBlur,
            ShoostPostProcessEffect.RGBBlurV2,
            ShoostPostProcessEffect.RGBSplit,
            ShoostPostProcessEffect.GrainCustom,
            ShoostPostProcessEffect.VignetteCustom,
            ShoostPostProcessEffect.Pixelize,
            ShoostPostProcessEffect.ChangeFrameRate,
            ShoostPostProcessEffect.Distortion,
            ShoostPostProcessEffect.Fisheye,
            ShoostPostProcessEffect.CameraFlash,
            ShoostPostProcessEffect.CustomMaterial,
            ShoostPostProcessEffect.GateWeave,
            ShoostPostProcessEffect.KawaseBlur,
            ShoostPostProcessEffect.LensDistortionCustom,
            ShoostPostProcessEffect.MotionTrail,
            ShoostPostProcessEffect.RGBBlur,
            ShoostPostProcessEffect.RGBChannelSeparator,
            ShoostPostProcessEffect.SharpenAfter,
            ShoostPostProcessEffect.RetroLookProBleedCustom,
            ShoostPostProcessEffect.RetroLookProNoise2Custom,
            ShoostPostProcessEffect.RetroLookProOldFilm2Custom,
            ShoostPostProcessEffect.RetroLookProTVEffectCustom
        };

        private static readonly EffectToggleEntry[] VisibleEffectOrder =
        {
            new EffectToggleEntry(ShoostPostProcessEffect.SharpenBefore, "锐化", "icon_Sharpen_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.AutoWhiteBalance, "白平衡", "icon_WhiteBalance_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.LevelAdjustment, "色阶", "icon_LevelsAdjustment_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.ColorGradingCustom, "调色", "icon_ColorGrading_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.EdgeLight, "边缘光", "icon_RimLight_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Outline, "轮廓", "icon_OutLine_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.DropShadow, "投影", "icon_DropShadow_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Gradient, "渐变", "icon_Gradient_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Glow, "发光", "icon_Glow_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Lighting, "光照", "icon_Lighting_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.CenterColorCorrection, "中心色彩校正", "icon_CenterColorCorrection"),
            new EffectToggleEntry(ShoostPostProcessEffect.LED, "LED", "icon_LEDPanel_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Weather, "天气", "icon_Weather_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Particle, "粒子", "icon_Particle_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.CameraSwitcher, "摄像头切换器", "icon_CameraSwitch_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.TransparentBackground, "透明背景", "icon_TransparentBackground_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.FilmBreathGateWeave, "胶片", "icon_Film_v3"),
            new EffectToggleEntry(ShoostPostProcessEffect.Tube, "电视", "icon_TV_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.VHS, "VHS", "icon_VHS_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.CRTEffects, "显示器", "icon_Monitor_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.DitheringCustom, "视频游戏", "icon_GameBoy_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.IrisBlur, "光圈模糊", "icon_IrisBlur_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.RGBBlurV2, "通道模糊", "icon_RGBBlur_v2"),
            new EffectToggleEntry(ShoostPostProcessEffect.RGBSplit, "RGB 分离", "icon_RGBSplit_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.GrainCustom, "颗粒", "icon_Grain_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.VignetteCustom, "暗角", "icon_Vignette_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Pixelize, "像素化", "icon_Pixel_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.ChangeFrameRate, "帧率限制", "icon_FPS_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Distortion, "湍流置换", "icon_Distortion_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.Fisheye, "镜头畸变", "icon_FishEye_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.CameraFlash, "摄像机闪光", "icon_CameraFlash_v1")
        };

        private static readonly EffectToggleEntry[] LegacyEffectOrder =
        {
            new EffectToggleEntry(ShoostPostProcessEffect.KawaseBlur, "Kawase 模糊", "icon_Blur_v1"),
            new EffectToggleEntry(ShoostPostProcessEffect.RGBChannelSeparator, "RGB 通道分离", "icon_RGBChannel_RGB"),
        };

        private static readonly string[] EffectDisplayNames =
        {
            "自定义材质",
            "自动白平衡",
            "变更帧率",
            "调色",
            "显示器",
            "湍流置换",
            "视频游戏",
            "降分辨率",
            "胶片",
            "镜头畸变",
            "画幅抖动",
            "颗粒",
            "光圈模糊",
            "Kawase 模糊",
            "镜头畸变（自定义）",
            "色阶",
            "运动拖影",
            "像素化",
            "RGB 模糊",
            "通道模糊",
            "RGB 通道分离",
            "RGB 分离",
            "锐化",
            "锐化（后）",
            "电视",
            "暗角",
            "RetroLookPro Bleed",
            "RetroLookPro Noise2",
            "RetroLookPro Old Film 2",
            "RetroLookPro TV Effect",
            "边缘光",
            "轮廓",
            "投影",
            "渐变",
            "发光",
            "光照",
            "中心色彩校正",
            "LED",
            "天气",
            "粒子",
            "摄像头切换器",
            "透明背景",
            "VHS",
            "摄像机闪光"
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

        private static readonly string[] ColorGradingWheelModeNames =
        {
            "色轮",
            "对数色轮"
        };

        private static readonly string[] ColorGradingShiftModeNames =
        {
            "色相对色相",
            "色相对饱和度",
            "色相对明度",
            "明度对饱和度"
        };

        private static readonly string[] SixColorNames =
        {
            "红色",
            "黄色",
            "绿色",
            "青色",
            "蓝色",
            "洋红"
        };

        private static readonly Color[] SixColorSwatches =
        {
            new Color(0.85f, 0.0f, 0.0f),
            new Color(0.78f, 0.78f, 0.0f),
            new Color(0.0f, 0.75f, 0.0f),
            new Color(0.0f, 0.7f, 0.75f),
            new Color(0.0f, 0.08f, 0.75f),
            new Color(0.7f, 0.0f, 0.7f)
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

            if (GetEffect(element) == ShoostPostProcessEffect.RGBBlurV2)
            {
                DrawRgbBlurV2Element(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.IrisBlur)
            {
                DrawIrisBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.AutoWhiteBalance)
            {
                DrawAutoWhiteBalanceElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.Fisheye)
            {
                DrawFisheyeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.GateWeave)
            {
                DrawGateWeaveElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.FilmBreathGateWeave)
            {
                DrawFilmBreathGateWeaveElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.GrainCustom)
            {
                DrawGrainCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.DitheringCustom)
            {
                DrawDitheringCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.CRTEffects)
            {
                DrawCrtEffectsElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VHS)
            {
                DrawVhsElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.Tube)
            {
                DrawTubeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.Pixelize)
            {
                DrawPixelizeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.ChangeFrameRate)
            {
                DrawChangeFrameRateElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.DownScaleResolution)
            {
                DrawDownScaleResolutionElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.Distortion)
            {
                DrawDistortionElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.RGBChannelSeparator)
            {
                DrawRgbChannelSeparatorElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.VignetteCustom)
            {
                DrawVignetteCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.LevelAdjustment)
            {
                DrawLevelAdjustmentElement(rect, element);
                return;
            }

            if (GetEffect(element) == ShoostPostProcessEffect.ColorGradingCustom)
            {
                DrawColorGradingCustomElement(rect, element);
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
                case ShoostPostProcessEffect.RGBBlurV2:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 3;
                    break;
                case ShoostPostProcessEffect.IrisBlur:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 5;
                    break;
                case ShoostPostProcessEffect.AutoWhiteBalance:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 3;
                    break;
                case ShoostPostProcessEffect.Fisheye:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 4;
                    break;
                case ShoostPostProcessEffect.GateWeave:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 5;
                    break;
                case ShoostPostProcessEffect.FilmBreathGateWeave:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 13;
                    break;
                case ShoostPostProcessEffect.GrainCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2;
                    break;
                case ShoostPostProcessEffect.DitheringCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetDitheringCustomUsesColorMode(element) ? 8 : 8;
                    break;
                case ShoostPostProcessEffect.CRTEffects:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2;
                    break;
                case ShoostPostProcessEffect.VHS:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetVhsUsesScanline(element) ? 5 : 4;
                    break;
                case ShoostPostProcessEffect.Tube:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 4;
                    break;
                case ShoostPostProcessEffect.Pixelize:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ShoostPostProcessEffect.ChangeFrameRate:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ShoostPostProcessEffect.DownScaleResolution:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1 + (GetDownScaleUsesCustomResolution(element) ? 2 : 0) + 1;
                    break;
                case ShoostPostProcessEffect.Distortion:
                    lineCount += GetCoreLineCount(false, false, true, false, false, showAdvanced);
                    lineCount += 8;
                    break;
                case ShoostPostProcessEffect.RGBChannelSeparator:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ShoostPostProcessEffect.VignetteCustom:
                    lineCount += GetCoreLineCount(false, GetVignetteCustomUsesTintMode(element), false, false, false, showAdvanced);
                    lineCount += 4;
                    break;
                case ShoostPostProcessEffect.LevelAdjustment:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 6;
                    break;
                case ShoostPostProcessEffect.ColorGradingCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 18 + (GetColorGradingUsesLogWheels(element) ? 2 : 0);
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

        private void DrawEffectIconToggles()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            DrawEffectIconRow(VisibleEffectOrder);
            EditorGUILayout.Space(3.0f);
            EditorGUILayout.LabelField("旧实现", EditorStyles.miniBoldLabel);
            DrawEffectIconRow(LegacyEffectOrder);
        }

        private void DrawEffectIconRow(EffectToggleEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            float width = EditorGUIUtility.currentViewWidth - 40.0f;
            width = Mathf.Max(160.0f, width);
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

                Rect buttonRect = new Rect(x, y, EffectIconSize, EffectIconSize);
                DrawEffectIconButton(buttonRect, entry);
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
            ResetEffectDefaults(element, effect);
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

            HashSet<int> seenEffects = new HashSet<int>();
            for (int index = layerValues.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                int effectIndex = GetEffectIndex(element);
                if (effectIndex >= 0 && !seenEffects.Add(effectIndex))
                {
                    layerValues.DeleteArrayElementAtIndex(index);
                    continue;
                }
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
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty parameters6 = element.FindPropertyRelative("parameters6");
            SerializedProperty parameters7 = element.FindPropertyRelative("parameters7");
            SerializedProperty parameters8 = element.FindPropertyRelative("parameters8");
            SerializedProperty parameters9 = element.FindPropertyRelative("parameters9");
            SerializedProperty parameters10 = element.FindPropertyRelative("parameters10");
            SerializedProperty parameters11 = element.FindPropertyRelative("parameters11");
            SerializedProperty parameters12 = element.FindPropertyRelative("parameters12");

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
                y = DrawPropertyLine(x, y, width, parameters4, "参数 4");
                y = DrawPropertyLine(x, y, width, parameters5, "参数 5");
                y = DrawPropertyLine(x, y, width, parameters6, "参数 6");
                y = DrawPropertyLine(x, y, width, parameters7, "参数 7");
                y = DrawPropertyLine(x, y, width, parameters8, "参数 8");
                y = DrawPropertyLine(x, y, width, parameters9, "参数 9");
                y = DrawPropertyLine(x, y, width, parameters10, "参数 10");
                y = DrawPropertyLine(x, y, width, parameters11, "参数 11");
                y = DrawPropertyLine(x, y, width, parameters12, "参数 12");
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

        private static void DrawSixColorAdjustmentSliders(
            float x,
            ref float y,
            float width,
            int mode,
            ref Vector4 hueVsHueA,
            ref Vector4 hueVsHueB,
            ref Vector4 hueVsSatA,
            ref Vector4 hueVsSatB,
            ref Vector4 hueVsLumA,
            ref Vector4 hueVsLumB)
        {
            float min = mode == 0 ? -180.0f : -1.0f;
            float max = mode == 0 ? 180.0f : 1.0f;
            for (int i = 0; i < SixColorNames.Length; i++)
            {
                float value = GetSixColorAdjustment(mode, i, hueVsHueA, hueVsHueB, hueVsSatA, hueVsSatB, hueVsLumA, hueVsLumB);
                value = DrawSixColorAdjustmentLine(x, y, width, SixColorNames[i], SixColorSwatches[i], value, min, max);
                SetSixColorAdjustment(mode, i, value, ref hueVsHueA, ref hueVsHueB, ref hueVsSatA, ref hueVsSatB, ref hueVsLumA, ref hueVsLumB);
                y += LineHeight + LineSpacing;
            }
        }

        private static float GetSixColorAdjustment(
            int mode,
            int colorIndex,
            Vector4 hueVsHueA,
            Vector4 hueVsHueB,
            Vector4 hueVsSatA,
            Vector4 hueVsSatB,
            Vector4 hueVsLumA,
            Vector4 hueVsLumB)
        {
            int index = mode * 6 + colorIndex;
            switch (index)
            {
                case 0: return hueVsHueA.x;
                case 1: return hueVsHueA.y;
                case 2: return hueVsHueA.z;
                case 3: return hueVsHueA.w;
                case 4: return hueVsHueB.x;
                case 5: return hueVsHueB.y;
                case 6: return hueVsHueB.z;
                case 7: return hueVsHueB.w;
                case 8: return hueVsSatA.x;
                case 9: return hueVsSatA.y;
                case 10: return hueVsSatA.z;
                case 11: return hueVsSatA.w;
                case 12: return hueVsSatB.x;
                case 13: return hueVsSatB.y;
                case 14: return hueVsSatB.z;
                case 15: return hueVsSatB.w;
                case 16: return hueVsLumA.x;
                case 17: return hueVsLumA.y;
                case 18: return hueVsLumA.z;
                case 19: return hueVsLumA.w;
                case 20: return hueVsLumB.x;
                case 21: return hueVsLumB.y;
                case 22: return hueVsLumB.z;
                case 23: return hueVsLumB.w;
                default: return 0.0f;
            }
        }

        private static void SetSixColorAdjustment(
            int mode,
            int colorIndex,
            float value,
            ref Vector4 hueVsHueA,
            ref Vector4 hueVsHueB,
            ref Vector4 hueVsSatA,
            ref Vector4 hueVsSatB,
            ref Vector4 hueVsLumA,
            ref Vector4 hueVsLumB)
        {
            int index = mode * 6 + colorIndex;
            switch (index)
            {
                case 0: hueVsHueA.x = value; break;
                case 1: hueVsHueA.y = value; break;
                case 2: hueVsHueA.z = value; break;
                case 3: hueVsHueA.w = value; break;
                case 4: hueVsHueB.x = value; break;
                case 5: hueVsHueB.y = value; break;
                case 6: hueVsHueB.z = value; break;
                case 7: hueVsHueB.w = value; break;
                case 8: hueVsSatA.x = value; break;
                case 9: hueVsSatA.y = value; break;
                case 10: hueVsSatA.z = value; break;
                case 11: hueVsSatA.w = value; break;
                case 12: hueVsSatB.x = value; break;
                case 13: hueVsSatB.y = value; break;
                case 14: hueVsSatB.z = value; break;
                case 15: hueVsSatB.w = value; break;
                case 16: hueVsLumA.x = value; break;
                case 17: hueVsLumA.y = value; break;
                case 18: hueVsLumA.z = value; break;
                case 19: hueVsLumA.w = value; break;
                case 20: hueVsLumB.x = value; break;
                case 21: hueVsLumB.y = value; break;
                case 22: hueVsLumB.z = value; break;
                case 23: hueVsLumB.w = value; break;
            }
        }

        private static void ResetEffectDefaults(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            if (element == null)
            {
                return;
            }

            switch (effect)
            {
                case ShoostPostProcessEffect.AutoWhiteBalance:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.Fisheye:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(0.2f, 1.0f, 0.1f, 0.0f));
                    break;
                case ShoostPostProcessEffect.IrisBlur:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 2.0f, 3.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.8f, 0.1f));
                    SetVector4(element, "parameters3", Vector4.zero);
                    break;
                case ShoostPostProcessEffect.GateWeave:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.02f, 20.0f, 0.05f, 15.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.FilmBreathGateWeave:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.02f, 20.0f, 0.05f, 15.0f));
                    SetVector4(element, "parameters1", new Vector4(0.005f, 5.0f, 0.2f, 15.0f));
                    SetVector4(element, "parameters2", new Vector4(0.1f, 12.0f, 0.1f, 16.0f));
                    SetVector4(element, "parameters3", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.GrainCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 2.0f, 0.9f, 0.0f));
                    SetVector4(element, "parameters1", Vector4.zero);
                    SetObjectReference(element, "texture", LoadDefaultGrainNoiseTexture());
                    break;
                case ShoostPostProcessEffect.ColorGradingCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters3", Vector4.zero);
                    SetVector4(element, "parameters4", Vector4.zero);
                    SetVector4(element, "parameters5", Vector4.zero);
                    SetVector4(element, "parameters6", new Vector4(0.3f, 0.55f, 0.0f, 0.0f));
                    SetVector4(element, "parameters7", Vector4.zero);
                    SetVector4(element, "parameters8", Vector4.zero);
                    SetVector4(element, "parameters9", Vector4.zero);
                    SetVector4(element, "parameters10", Vector4.zero);
                    SetVector4(element, "parameters11", Vector4.zero);
                    SetVector4(element, "parameters12", Vector4.zero);
                    break;
                case ShoostPostProcessEffect.Pixelize:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.ChangeFrameRate:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(12.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.RGBBlurV2:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", Vector4.zero);
                    break;
                case ShoostPostProcessEffect.RGBSplit:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.35f, 0.0f, RgbSplitInitMarker));
                    break;
                case ShoostPostProcessEffect.DownScaleResolution:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    break;
                case ShoostPostProcessEffect.Distortion:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
                    SetVector4(element, "parameters0", new Vector4(5.0f, 0.1f, 0.2f, 0.1f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, -2.0f));
                    break;
                case ShoostPostProcessEffect.DitheringCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDitheringTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(4.0f, 32.0f, 32.0f, 32.0f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(0.1254902f, 0.2f, 0.1764706f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.3372549f, 0.4980392f, 0.3803922f, 1.0f));
                    SetVector4(element, "parameters5", new Vector4(0.8627451f, 0.8862745f, 0.3882353f, 1.0f));
                    break;
                case ShoostPostProcessEffect.CRTEffects:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadCrtEffectsTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 3.0f, 0.0f));
                    break;
                case ShoostPostProcessEffect.RGBChannelSeparator:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", Vector4.zero);
                    break;
                case ShoostPostProcessEffect.Tube:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadTubeLutTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", Vector4.zero);
                    SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
                    break;
                case ShoostPostProcessEffect.VHS:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    break;
                case ShoostPostProcessEffect.EdgeLight:
                case ShoostPostProcessEffect.Outline:
                case ShoostPostProcessEffect.DropShadow:
                case ShoostPostProcessEffect.Gradient:
                case ShoostPostProcessEffect.Glow:
                case ShoostPostProcessEffect.Lighting:
                case ShoostPostProcessEffect.CenterColorCorrection:
                case ShoostPostProcessEffect.LED:
                case ShoostPostProcessEffect.Weather:
                case ShoostPostProcessEffect.Particle:
                case ShoostPostProcessEffect.CameraSwitcher:
                case ShoostPostProcessEffect.TransparentBackground:
                case ShoostPostProcessEffect.CameraFlash:
                    SetFloat(element, "intensity", 1.0f);
                    break;
            }
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
            SetVector4(element, "parameters4", Vector4.zero);
            SetVector4(element, "parameters5", Vector4.zero);
            SetVector4(element, "parameters6", Vector4.zero);
            SetVector4(element, "parameters7", Vector4.zero);
            SetVector4(element, "parameters8", Vector4.zero);
            SetVector4(element, "parameters9", Vector4.zero);
            SetVector4(element, "parameters10", Vector4.zero);
            SetVector4(element, "parameters11", Vector4.zero);
            SetVector4(element, "parameters12", Vector4.zero);
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

    }
}
