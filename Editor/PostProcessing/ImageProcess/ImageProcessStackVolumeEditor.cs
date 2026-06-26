using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Rendering;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    [CustomEditor(typeof(ImageProcessStackVolume))]
    internal sealed partial class ImageProcessStackVolumeEditor : VolumeComponentEditor
    {
        private const float LineHeight = 18.0f;
        private const float LineSpacing = 2.0f;
        private const float LevelAdjustmentInitMarker = -12345.0f;
        private const float RgbSplitInitMarker = -12346.0f;
        private const float LogoOverlayInitMarker = -12347.0f;
        private const float EffectIconSize = 22.0f;
        private const float EffectIconSpacing = 2.0f;
        private const float ColorWheelMinSize = 64.0f;
        private const float ColorWheelMaxSize = 120.0f;
        private const float ColorWheelGap = 4.0f;
        private const string TrackballShaderName = "Hidden/Universal Render Pipeline/Editor/Trackball";
        private const string DefaultDistortionTextureGuid = "f4c1f3c21e3ec4a479c69cffea26c6cd";
        private const string DefaultVhsEdgeNoiseTextureGuid = "014de9bcc7cd0a148929d7e58755ee44";
        private const string DefaultFeatherParticleLargeTextureGuid = "b48fa69ab45f44318ad8b46afe34cfc7";
        private const string DefaultFeatherParticleSmallTextureGuid = "77ce570fc573479190a9bc876c4fe1d3";
        private const string PackageAssetRoot = "Packages/jp.lilxyzw.liltoon.urp.extensions";
        private const bool showAdvancedSettings = false;
        private static Texture2D colorWheelTexture;
        private static GUIStyle colorWheelThumbStyle;
        private static Vector2 colorWheelThumbSize;
        private static Material trackballMaterial;

        private readonly struct EffectToggleEntry
        {
            public readonly ImageProcessEffect Effect;
            public readonly string Label;
            public readonly string IconName;

            public EffectToggleEntry(ImageProcessEffect effect, string label, string iconName)
            {
                Effect = effect;
                Label = label;
                IconName = iconName;
            }
        }

        private static readonly EffectToggleEntry[] VisibleEffectOrder =
        {
            new EffectToggleEntry(ImageProcessEffect.SharpenBefore, "锐化", "icon_Sharpen_v1"),
            new EffectToggleEntry(ImageProcessEffect.AutoWhiteBalance, "白平衡", "icon_WhiteBalance_v1"),
            new EffectToggleEntry(ImageProcessEffect.LogoOverlay, "图标显示", "icon_Picture_v1"),
            new EffectToggleEntry(ImageProcessEffect.LevelAdjustment, "色阶", "icon_LevelsAdjustment_v1"),
            new EffectToggleEntry(ImageProcessEffect.ColorGradingCustom, "调色", "icon_ColorGrading_v1"),
            new EffectToggleEntry(ImageProcessEffect.Gradient, "渐变", "icon_Gradient_v1"),
            new EffectToggleEntry(ImageProcessEffect.Glow, "发光", "icon_Glow_v1"),
            new EffectToggleEntry(ImageProcessEffect.ToonMap, "色调映射", "icon_ScreenEffects_v1"),
            new EffectToggleEntry(ImageProcessEffect.Lighting, "光照", "icon_Lighting_v1"),
            new EffectToggleEntry(ImageProcessEffect.CenterColorCorrection, "中心色彩校正", "icon_CenterColorCorrection"),
            new EffectToggleEntry(ImageProcessEffect.Kuwahara, "桑原", "filter_v2"),
            new EffectToggleEntry(ImageProcessEffect.Weather, "天气", "icon_Weather_v1"),
            new EffectToggleEntry(ImageProcessEffect.Particle, "粒子", "icon_Particle_v1"),
            new EffectToggleEntry(ImageProcessEffect.GlitchArt, "故障艺术", "icon_Distortion_v1"),
            new EffectToggleEntry(ImageProcessEffect.PrismFracture, "棱镜破碎", "icon_RGBSplit_v1"),
            new EffectToggleEntry(ImageProcessEffect.SpeedLines, "集中线", "icon_Flare_Ray_v1"),
            new EffectToggleEntry(ImageProcessEffect.SkyGodRays, "天空神光", "icon_Flare_Ray_v1"),
            new EffectToggleEntry(ImageProcessEffect.CinematicBars, "电影黑边", "icon_ScreenEffects_v1"),
            new EffectToggleEntry(ImageProcessEffect.FilmBreathGateWeave, "胶片", "icon_Film_v3"),
            new EffectToggleEntry(ImageProcessEffect.Tube, "电视", "icon_TV_v1"),
            new EffectToggleEntry(ImageProcessEffect.VHS, "VHS", "icon_VHS_v1"),
            new EffectToggleEntry(ImageProcessEffect.CRTEffects, "显示器", "icon_Monitor_v1"),
            new EffectToggleEntry(ImageProcessEffect.DitheringCustom, "视频游戏", "icon_GameBoy_v1"),
            new EffectToggleEntry(ImageProcessEffect.BlueNoise, "蓝噪色块", "icon_Grain_v1"),
            new EffectToggleEntry(ImageProcessEffect.IrisBlur, "光圈模糊", "icon_IrisBlur_v1"),
            new EffectToggleEntry(ImageProcessEffect.RGBBlurV2, "通道模糊", "icon_RGBBlur_v2"),
            new EffectToggleEntry(ImageProcessEffect.RGBSplit, "RGB 分离", "icon_RGBSplit_v1"),
            new EffectToggleEntry(ImageProcessEffect.RGBChannelSeparator, "RGB 通道分离", "icon_RGBChannel_RGB"),
            new EffectToggleEntry(ImageProcessEffect.BokehZoomBlur, "光斑变焦", "icon_Flare_Ray_v1"),
            new EffectToggleEntry(ImageProcessEffect.ApertureBokeh, "光圈散景", "icon_Glow_SelectColor_v1"),
            new EffectToggleEntry(ImageProcessEffect.LensFlare, "镜头光晕", "icon_Flare_Ray_v1"),
            new EffectToggleEntry(ImageProcessEffect.GrainCustom, "颗粒", "icon_Grain_v1"),
            new EffectToggleEntry(ImageProcessEffect.VignetteCustom, "暗角", "icon_Vignette_v1"),
            new EffectToggleEntry(ImageProcessEffect.Pixelize, "像素化", "icon_Pixel_v1"),
            new EffectToggleEntry(ImageProcessEffect.ChangeFrameRate, "帧率限制", "icon_FPS_v1"),
            new EffectToggleEntry(ImageProcessEffect.Distortion, "湍流置换", "icon_Distortion_v1"),
            new EffectToggleEntry(ImageProcessEffect.Fisheye, "镜头畸变", "icon_FishEye_v1"),
            new EffectToggleEntry(ImageProcessEffect.CameraFlash, "摄像机闪光", "icon_CameraFlash_v1")
        };

        private static readonly EffectToggleEntry[] LegacyEffectOrder =
        {
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
            "已移除",
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
            "已移除",
            "已移除",
            "已移除",
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
            "摄像机闪光",
            "色调映射",
            "桑原",
            "光斑变焦",
            "光圈散景",
            "镜头光晕",
            "电影黑边",
            "故障艺术",
            "棱镜破碎",
            "集中线",
            "天空神光",
            "图标显示",
            "蓝噪色块"
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

        private static readonly string[] LuminanceBandNames =
        {
            "阴影",
            "暗部",
            "中间调",
            "亮部",
            "明亮",
            "高光"
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

        private static readonly Color[] LuminanceBandSwatches =
        {
            new Color(0.08f, 0.08f, 0.08f),
            new Color(0.2f, 0.2f, 0.2f),
            new Color(0.38f, 0.38f, 0.38f),
            new Color(0.58f, 0.58f, 0.58f),
            new Color(0.78f, 0.78f, 0.78f),
            new Color(0.95f, 0.95f, 0.95f)
        };

        private static readonly int[] BlendModeValues =
        {
            (int)ImageProcessBlendMode.Normal,
            (int)ImageProcessBlendMode.Add,
            (int)ImageProcessBlendMode.Multiply,
            (int)ImageProcessBlendMode.Screen,
            (int)ImageProcessBlendMode.Darken,
            (int)ImageProcessBlendMode.ColorBurn,
            (int)ImageProcessBlendMode.LinearBurn,
            (int)ImageProcessBlendMode.Lighten,
            (int)ImageProcessBlendMode.ColorDodge,
            (int)ImageProcessBlendMode.Overlay,
            (int)ImageProcessBlendMode.SoftLight,
            (int)ImageProcessBlendMode.HardLight,
            (int)ImageProcessBlendMode.VividLight,
            (int)ImageProcessBlendMode.LinearLight,
            (int)ImageProcessBlendMode.PinLight,
            (int)ImageProcessBlendMode.HardMix,
            (int)ImageProcessBlendMode.Difference,
            (int)ImageProcessBlendMode.Exclusion,
            (int)ImageProcessBlendMode.Subtract,
            (int)ImageProcessBlendMode.Divide,
            (int)ImageProcessBlendMode.Hue,
            (int)ImageProcessBlendMode.Saturation,
            (int)ImageProcessBlendMode.Color,
            (int)ImageProcessBlendMode.Luminosity
        };

        private static readonly Dictionary<ImageProcessEffect, GUIContent> EffectIconContents = new Dictionary<ImageProcessEffect, GUIContent>();

        private SerializedDataParameter showInSceneView;
        private SerializedProperty layers;
        private SerializedProperty layerValues;
        private ReorderableList layerList;

        public override void OnEnable()
        {
            PropertyFetcher<ImageProcessStackVolume> fetcher = new PropertyFetcher<ImageProcessStackVolume>(serializedObject);
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

        public override void OnDisable()
        {
            DisableGradientViewControlForThisEditor();
            DisableImageProcessLayerViewControlsForThisEditor();
            base.OnDisable();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CleanupLayersForUserOrder();

            PropertyField(showInSceneView, new GUIContent("场景视图"));
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
            if (GetEffect(element) == ImageProcessEffect.SharpenBefore || GetEffect(element) == ImageProcessEffect.SharpenAfter)
            {
                DrawSharpenElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.RGBSplit)
            {
                DrawRgbSplitElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.RGBBlurV2)
            {
                DrawRgbBlurV2Element(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.IrisBlur)
            {
                DrawIrisBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.BokehZoomBlur)
            {
                DrawBokehZoomBlurElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.ApertureBokeh)
            {
                DrawApertureBokehElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.LensFlare)
            {
                DrawLensFlareElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.AutoWhiteBalance)
            {
                DrawAutoWhiteBalanceElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.LogoOverlay)
            {
                DrawLogoOverlayElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Fisheye)
            {
                DrawFisheyeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.GateWeave)
            {
                DrawGateWeaveElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.FilmBreathGateWeave)
            {
                DrawFilmBreathGateWeaveElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.GrainCustom)
            {
                DrawGrainCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.DitheringCustom)
            {
                DrawDitheringCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.BlueNoise)
            {
                DrawBlueNoiseElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.CRTEffects)
            {
                DrawCrtEffectsElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.VHS)
            {
                DrawVhsElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Tube)
            {
                DrawTubeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Pixelize)
            {
                DrawPixelizeElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.ChangeFrameRate)
            {
                DrawChangeFrameRateElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.DownScaleResolution)
            {
                DrawDownScaleResolutionElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Distortion)
            {
                DrawDistortionElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.RGBChannelSeparator)
            {
                DrawRgbChannelSeparatorElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.VignetteCustom)
            {
                DrawVignetteCustomElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Gradient)
            {
                DrawGradientElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Glow)
            {
                DrawGlowElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.ToonMap)
            {
                DrawToonMapElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.CenterColorCorrection)
            {
                DrawCenterColorCorrectionElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Kuwahara)
            {
                DrawKuwaharaElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Weather)
            {
                DrawWeatherElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.Particle)
            {
                DrawParticleElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.CinematicBars)
            {
                DrawCinematicBarsElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.GlitchArt)
            {
                DrawGlitchArtElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.PrismFracture)
            {
                DrawPrismFractureElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.SpeedLines)
            {
                DrawSpeedLinesElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.SkyGodRays)
            {
                DrawSkyGodRaysElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.LevelAdjustment)
            {
                DrawLevelAdjustmentElement(rect, element);
                return;
            }

            if (GetEffect(element) == ImageProcessEffect.ColorGradingCustom)
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

            ImageProcessEffect effect = GetEffect(element);
            bool showAdvanced = showAdvancedSettings;

            int lineCount = 1;

            switch (effect)
            {
                case ImageProcessEffect.SharpenBefore:
                case ImageProcessEffect.SharpenAfter:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ImageProcessEffect.RGBSplit:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2 + (GetRgbSplitUsesAngle(element) ? 1 : 0);
                    break;
                case ImageProcessEffect.RGBBlurV2:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 3;
                    break;
                case ImageProcessEffect.IrisBlur:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 6;
                    break;
                case ImageProcessEffect.BokehZoomBlur:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 17;
                    break;
                case ImageProcessEffect.ApertureBokeh:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 15;
                    break;
                case ImageProcessEffect.LensFlare:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 18;
                    break;
                case ImageProcessEffect.AutoWhiteBalance:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 3;
                    break;
                case ImageProcessEffect.LogoOverlay:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 24;
                    break;
                case ImageProcessEffect.Fisheye:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 6;
                    break;
                case ImageProcessEffect.GateWeave:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 5;
                    break;
                case ImageProcessEffect.FilmBreathGateWeave:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 7;
                    break;
                case ImageProcessEffect.GrainCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2;
                    break;
                case ImageProcessEffect.DitheringCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetDitheringCustomUsesColorMode(element) ? 8 : 8;
                    break;
                case ImageProcessEffect.BlueNoise:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 8;
                    break;
                case ImageProcessEffect.CRTEffects:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 2;
                    break;
                case ImageProcessEffect.VHS:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetVhsUsesScanline(element) ? 5 : 4;
                    break;
                case ImageProcessEffect.Tube:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 4;
                    break;
                case ImageProcessEffect.Pixelize:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ImageProcessEffect.ChangeFrameRate:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ImageProcessEffect.DownScaleResolution:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1 + (GetDownScaleUsesCustomResolution(element) ? 2 : 0) + 1;
                    break;
                case ImageProcessEffect.Distortion:
                    lineCount += GetCoreLineCount(false, false, true, false, false, showAdvanced);
                    lineCount += 8;
                    break;
                case ImageProcessEffect.RGBChannelSeparator:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ImageProcessEffect.VignetteCustom:
                    lineCount += GetCoreLineCount(false, GetVignetteCustomUsesTintMode(element), false, false, false, showAdvanced);
                    lineCount += 5;
                    break;
                case ImageProcessEffect.Gradient:
                    lineCount += GetGradientLineCount(element);
                    break;
                case ImageProcessEffect.Glow:
                    lineCount += GetGlowLineCount(element);
                    break;
                case ImageProcessEffect.ToonMap:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 1;
                    break;
                case ImageProcessEffect.CenterColorCorrection:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 11;
                    break;
                case ImageProcessEffect.Kuwahara:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 7;
                    break;
                case ImageProcessEffect.Weather:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetWeatherLineCount(element);
                    break;
                case ImageProcessEffect.Particle:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += GetParticleLineCount(element);
                    break;
                case ImageProcessEffect.CinematicBars:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 3;
                    break;
                case ImageProcessEffect.GlitchArt:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 8;
                    break;
                case ImageProcessEffect.PrismFracture:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 11;
                    break;
                case ImageProcessEffect.SpeedLines:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 11;
                    break;
                case ImageProcessEffect.SkyGodRays:
                    lineCount += GetCoreLineCount(false, true, false, false, false, showAdvanced);
                    lineCount += 19;
                    break;
                case ImageProcessEffect.LevelAdjustment:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 6;
                    break;
                case ImageProcessEffect.ColorGradingCustom:
                    lineCount += GetCoreLineCount(false, false, false, false, false, showAdvanced);
                    lineCount += 19 + (GetColorGradingUsesLogWheels(element) ? 2 : 0);
                    break;
                case ImageProcessEffect.CustomMaterial:
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
            if (LegacyEffectOrder.Length > 0)
            {
                EditorGUILayout.Space(3.0f);
                EditorGUILayout.LabelField("旧实现", EditorStyles.miniBoldLabel);
                DrawEffectIconRow(LegacyEffectOrder);
            }
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

        private void AddLayer(ImageProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray || HasLayer(effect))
            {
                return;
            }

            Undo.RecordObject(serializedObject.targetObject, "Add ImageProcess Effect");

            int index = layerValues.arraySize;
            layerValues.InsertArrayElementAtIndex(index);

            SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
            ResetLayerDefaults(element);
            SetEnum(element, "effect", (int)effect);
            ResetEffectDefaults(element, effect);
            element.isExpanded = true;
        }

        private void RemoveLayer(ImageProcessEffect effect)
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            Dictionary<int, bool> expandedByEffect = CaptureLayerExpandedStates();

            for (int index = 0; index < layerValues.arraySize; index++)
            {
                if (GetEffectIndex(layerValues.GetArrayElementAtIndex(index)) != (int)effect)
                {
                    continue;
                }

                Undo.RecordObject(serializedObject.targetObject, "Remove ImageProcess Effect");
                layerValues.DeleteArrayElementAtIndex(index);
                RestoreLayerExpandedStates(expandedByEffect);
                break;
            }
        }

        private void ToggleEffect(ImageProcessEffect effect)
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

        private void CleanupLayersForUserOrder()
        {
            if (layerValues == null || !layerValues.isArray)
            {
                return;
            }

            Dictionary<int, bool> expandedByEffect = CaptureLayerExpandedStates();

            HashSet<int> seenEffects = new HashSet<int>();
            for (int index = layerValues.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                int effectIndex = GetEffectIndex(element);
                if (IsRemovedEffectSlot(effectIndex))
                {
                    layerValues.DeleteArrayElementAtIndex(index);
                    continue;
                }

                if (effectIndex >= 0 && !seenEffects.Add(effectIndex))
                {
                    layerValues.DeleteArrayElementAtIndex(index);
                    continue;
                }
            }

            RestoreLayerExpandedStates(expandedByEffect);
        }

        private Dictionary<int, bool> CaptureLayerExpandedStates()
        {
            Dictionary<int, bool> expandedByEffect = new Dictionary<int, bool>();
            if (layerValues == null || !layerValues.isArray)
            {
                return expandedByEffect;
            }

            for (int index = 0; index < layerValues.arraySize; index++)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                int effectIndex = GetEffectIndex(element);
                bool isExpanded = element.isExpanded;
                if (expandedByEffect.TryGetValue(effectIndex, out bool existing))
                {
                    expandedByEffect[effectIndex] = existing || isExpanded;
                }
                else
                {
                    expandedByEffect.Add(effectIndex, isExpanded);
                }
            }

            return expandedByEffect;
        }

        private void RestoreLayerExpandedStates(Dictionary<int, bool> expandedByEffect)
        {
            if (layerValues == null || !layerValues.isArray || expandedByEffect == null)
            {
                return;
            }

            for (int index = 0; index < layerValues.arraySize; index++)
            {
                SerializedProperty element = layerValues.GetArrayElementAtIndex(index);
                int effectIndex = GetEffectIndex(element);
                if (expandedByEffect.TryGetValue(effectIndex, out bool isExpanded))
                {
                    element.isExpanded = isExpanded;
                }
            }
        }

        private bool HasLayer(ImageProcessEffect effect)
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
                $"{PackageAssetRoot}/Editor/ImageProcessIcons/{iconName}.png",
                $"Assets/Editor/ImageProcessIcons/{iconName}.png"
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

        private static string GetEffectDisplayName(ImageProcessEffect effect)
        {
            int index = (int)effect;
            if (index >= 0 && index < EffectDisplayNames.Length)
            {
                return EffectDisplayNames[index];
            }

            return effect.ToString();
        }

        private static ImageProcessEffect GetEffect(SerializedProperty layer)
        {
            SerializedProperty effectProperty = layer.FindPropertyRelative("effect");
            if (effectProperty == null || effectProperty.propertyType != SerializedPropertyType.Enum)
            {
                return ImageProcessEffect.CustomMaterial;
            }

            int effectIndex = effectProperty.enumValueIndex;
            if (effectIndex < 0 || effectIndex >= System.Enum.GetValues(typeof(ImageProcessEffect)).Length)
            {
                return ImageProcessEffect.CustomMaterial;
            }

            return (ImageProcessEffect)effectIndex;
        }

        private static int GetEffectIndex(SerializedProperty layer)
        {
            return (int)GetEffect(layer);
        }

        private static bool IsRemovedEffectSlot(int effectIndex)
        {
            return effectIndex == (int)ImageProcessEffect.RemovedEffectSlot13 ||
                   effectIndex == (int)ImageProcessEffect.RemovedEffectSlot30 ||
                   effectIndex == (int)ImageProcessEffect.RemovedEffectSlot31 ||
                   effectIndex == (int)ImageProcessEffect.RemovedEffectSlot32;
        }

        private void DrawSimpleLayerElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty enabled = element.FindPropertyRelative("enabled");
            bool isCustomMaterial = GetEffect(element) == ImageProcessEffect.CustomMaterial;
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
            float presetWidth = LayerPresetButtonSize;
            float intensityWidth = Mathf.Clamp(rect.width * 0.34f, 140.0f, 220.0f);
            float foldoutWidth = Mathf.Max(0.0f, rect.width - checkboxWidth - presetWidth - intensityWidth - 10.0f);

            if (enabled != null)
            {
                Rect enabledRect = new Rect(lineRect.x, lineRect.y, checkboxWidth, lineRect.height);
                enabled.boolValue = EditorGUI.Toggle(enabledRect, enabled.boolValue);
            }

            Rect foldoutRect = new Rect(lineRect.x + checkboxWidth, lineRect.y, foldoutWidth, lineRect.height);
            element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GetLayerLabel(element), true);

            Rect presetRect = new Rect(lineRect.xMax - intensityWidth - presetWidth - 4.0f, lineRect.y, presetWidth, lineRect.height);
            DrawLayerPresetButton(presetRect, element);

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
            SerializedProperty blendMode = element.FindPropertyRelative("blendMode");
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
            Color[] swatches = mode == 3 ? LuminanceBandSwatches : SixColorSwatches;
            for (int i = 0; i < SixColorNames.Length; i++)
            {
                float value = GetSixColorAdjustment(mode, i, hueVsHueA, hueVsHueB, hueVsSatA, hueVsSatB, hueVsLumA, hueVsLumB);
                value = DrawSixColorAdjustmentLine(x, y, width, GetSixColorAdjustmentLabel(mode, i), swatches[i], value, min, max);
                SetSixColorAdjustment(mode, i, value, ref hueVsHueA, ref hueVsHueB, ref hueVsSatA, ref hueVsSatB, ref hueVsLumA, ref hueVsLumB);
                y += LineHeight + LineSpacing;
            }
        }

        private static string GetSixColorAdjustmentLabel(int mode, int index)
        {
            if (mode == 3)
            {
                return LuminanceBandNames[Mathf.Clamp(index, 0, LuminanceBandNames.Length - 1)] + "饱和度";
            }

            string colorName = SixColorNames[Mathf.Clamp(index, 0, SixColorNames.Length - 1)];
            switch (mode)
            {
                case 0:
                    return colorName + "色相";
                case 1:
                    return colorName + "饱和度";
                case 2:
                    return colorName + "明度";
                default:
                    return colorName;
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

        private static void ResetEffectDefaults(SerializedProperty element, ImageProcessEffect effect)
        {
            if (element == null)
            {
                return;
            }

            switch (effect)
            {
                case ImageProcessEffect.AutoWhiteBalance:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                    break;
                case ImageProcessEffect.LogoOverlay:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters3", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters5", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters6", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters7", new Vector4(0.5f, 0.5f, 0.2f, 1.0f));
                    SetVector4(element, "parameters8", new Vector4(0.0f, 1.0f, 2.0f, 3.0f));
                    SetVector4(element, "parameters9", new Vector4(4.0f, 5.0f, 6.0f, 7.0f));
                    SetVector4(element, "parameters10", Vector4.one);
                    SetVector4(element, "parameters11", Vector4.one);
                    SetVector4(element, "parameters12", new Vector4(LogoOverlayInitMarker, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.Fisheye:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(0.2f, 1.0f, 0.1f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.IrisBlur:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 2.0f, 3.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.8f, 0.1f));
                    SetVector4(element, "parameters3", Vector4.zero);
                    break;
                case ImageProcessEffect.GateWeave:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.02f, 20.0f, 0.05f, 15.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.FilmBreathGateWeave:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(0.2f, 1.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, 202605.0f));
                    SetVector4(element, "parameters3", Vector4.zero);
                    SetObjectReference(element, "texture", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("4cdb4a3a04be3954f81ba4e7912a2a54")));
                    break;
                case ImageProcessEffect.GrainCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 2.0f, 0.9f, 0.0f));
                    SetVector4(element, "parameters1", Vector4.zero);
                    SetObjectReference(element, "texture", LoadDefaultGrainNoiseTexture());
                    break;
                case ImageProcessEffect.ColorGradingCustom:
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
                case ImageProcessEffect.Pixelize:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.ChangeFrameRate:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(12.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.RGBBlurV2:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", Vector4.zero);
                    break;
                case ImageProcessEffect.RGBSplit:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.35f, 0.0f, RgbSplitInitMarker));
                    break;
                case ImageProcessEffect.DownScaleResolution:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    break;
                case ImageProcessEffect.Distortion:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
                    SetVector4(element, "parameters0", new Vector4(5.0f, 0.1f, 0.2f, 0.1f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, -2.0f));
                    break;
                case ImageProcessEffect.DitheringCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDitheringTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(4.0f, 32.0f, 32.0f, 32.0f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(0.1254902f, 0.2f, 0.1764706f, 1.0f));
                    SetVector4(element, "parameters4", new Vector4(0.3372549f, 0.4980392f, 0.3803922f, 1.0f));
                    SetVector4(element, "parameters5", new Vector4(0.8627451f, 0.8862745f, 0.3882353f, 1.0f));
                    break;
                case ImageProcessEffect.BlueNoise:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 18.0f, 0.78f));
                    SetVector4(element, "parameters1", new Vector4(0.35f, 0.75f, 0.18f, 12.0f));
                    break;
                case ImageProcessEffect.CRTEffects:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadCrtEffectsTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 3.0f, 0.0f));
                    break;
                case ImageProcessEffect.RGBChannelSeparator:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", Vector4.zero);
                    break;
                case ImageProcessEffect.VignetteCustom:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 1.0f, 0.5f));
                    break;
                case ImageProcessEffect.Tube:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadTubeLutTexture(0));
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.0f));
                    SetVector4(element, "parameters1", Vector4.zero);
                    SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
                    break;
                case ImageProcessEffect.VHS:
                    SetFloat(element, "intensity", 1.0f);
                    SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
                    SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    break;
                case ImageProcessEffect.Gradient:
                    SetFloat(element, "intensity", 1.0f);
                    SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 5.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 0.5f, 1.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    break;
                case ImageProcessEffect.Glow:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 2.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(0.2f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(3.0f, 180.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.ToonMap:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(2.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.CenterColorCorrection:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.18f, 0.0f, 0.0f, 0.0f));
                    SetVector4(element, "parameters1", new Vector4(0.5f, 0.5f, 0.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.Kuwahara:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(3.0f, 1.0f, 0.0f, 0.1f));
                    SetVector4(element, "parameters1", new Vector4(0.25f, 0.05f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.BokehZoomBlur:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 4.0f, 2.0f));
                    SetVector4(element, "parameters2", new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                    SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 0.5f, 0.0f));
                    break;
                case ImageProcessEffect.ApertureBokeh:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(1.0f, 0.4f, 0.2f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.35f, 1.0f, 0.0f, 2.0f));
                    SetVector4(element, "parameters2", new Vector4(0.0f, 1.0f, 0.0f, 0.35f));
                    SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 4.0f, 0.0f));
                    break;
                case ImageProcessEffect.LensFlare:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", new Color(1.0f, 0.86f, 0.55f, 1.0f));
                    SetVector4(element, "parameters0", new Vector4(-0.38f, 0.32f, -18.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(0.065f, 0.34f, 0.92f, 6.0f));
                    SetVector4(element, "parameters2", new Vector4(0.78f, 1.0f, 0.55f, 0.55f));
                    SetVector4(element, "parameters3", new Vector4(0.85f, 2.4f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.Weather:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 1.0f, 1.0f));
                    SetVector4(element, "parameters1", new Vector4(1.0f, 0.35f, 1.0f, 1.0f));
                    SetVector4(element, "parameters2", new Vector4(1.0f, 1.0f, 1.0f, 0.35f));
                    SetVector4(element, "parameters3", Vector4.one);
                    break;
                case ImageProcessEffect.Particle:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetDefaultFeatherParticleTextures(element);
                    SetVector4(element, "parameters0", new Vector4(0.85f, 0.85f, 0.85f, 13.0f));
                    SetVector4(element, "parameters1", new Vector4(0.0f, -90.0f, 0.16f, 0.85f));
                    SetVector4(element, "parameters2", new Vector4(0.5f, 0.58f, 0.0f, 0.0f));
                    SetVector4(element, "parameters3", new Vector4(0.62f, 0.85f, 0.35f, 2.0f));
                    SetVector4(element, "parameters4", new Vector4(0.16f, 0.0f, 0.55f, 0.34f));
                    SetVector4(element, "parameters5", new Vector4(0.0f, 1.0f, 1.0f, 0.22f));
                    SetVector4(element, "parameters6", new Vector4(0.13f, 2.4f, 0.65f, 0.58f));
                    SetVector4(element, "parameters7", new Vector4(1.15f, 1.45f, 0.75f, 1.25f));
                    SetVector4(element, "parameters8", new Vector4(2.0f, 0.0f, 1.0f, 3.0f));
                    SetVector4(element, "parameters9", new Vector4(0.0f, -90.0f, 0.0f, 0.75f));
                    break;
                case ImageProcessEffect.CinematicBars:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(2.39f, 0.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.GlitchArt:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.18f, 0.34f, 0.24f, 0.12f));
                    SetVector4(element, "parameters1", new Vector4(0.42f, 1.10f, 0.04f, 7.0f));
                    break;
                case ImageProcessEffect.PrismFracture:
                    SetFloat(element, "intensity", 1.0f);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.42f, 0.12f));
                    SetVector4(element, "parameters1", new Vector4(0.58f, 0.74f, 15.0f, 0.0f));
                    SetVector4(element, "parameters2", new Vector4(0.38f, 1.0f, 0.0f, 0.0f));
                    break;
                case ImageProcessEffect.SpeedLines:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.black);
                    SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.34f, 0.82f));
                    SetVector4(element, "parameters1", new Vector4(88.0f, 0.24f, 0.72f, 0.05f));
                    SetVector4(element, "parameters2", new Vector4(2.0f, 0.0f, 0.16f, 2.0f));
                    SetVector4(element, "parameters3", new Vector4(0.55f, 0.10f, 10.0f, 0.10f));
                    break;
                case ImageProcessEffect.SkyGodRays:
                    SetFloat(element, "intensity", 1.0f);
                    SetColor(element, "color", Color.white);
                    SetVector4(element, "parameters0", new Vector4(1.22f, 0.99f, 181.0f, 1.08f));
                    SetVector4(element, "parameters1", new Vector4(130.0f, 85.0f, 234.0f, -53.0f));
                    SetVector4(element, "parameters2", new Vector4(1.04f, 146.0f, 3.0f, 3.0f));
                    SetVector4(element, "parameters3", new Vector4(32.0f, 0.36f, 0.0f, 0.21f));
                    break;
                case ImageProcessEffect.Lighting:
                case ImageProcessEffect.LED:
                case ImageProcessEffect.CameraSwitcher:
                case ImageProcessEffect.TransparentBackground:
                case ImageProcessEffect.CameraFlash:
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
            SetEnum(element, "effect", (int)ImageProcessEffect.CustomMaterial);
            SetObjectReference(element, "materialOverride", null);
            SetObjectReference(element, "shaderOverride", null);
            SetInt(element, "passIndex", 0);
            SetFloat(element, "intensity", 1.0f);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.Normal);
            SetColor(element, "color", Color.white);
            SetObjectReference(element, "texture", null);
            SetObjectReference(element, "logoTexture0", null);
            SetObjectReference(element, "logoTexture1", null);
            SetObjectReference(element, "logoTexture2", null);
            SetObjectReference(element, "logoTexture3", null);
            SetObjectReference(element, "logoTexture4", null);
            SetObjectReference(element, "logoTexture5", null);
            SetObjectReference(element, "logoTexture6", null);
            SetObjectReference(element, "logoTexture7", null);
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
