using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private const float LayerPresetButtonSize = 18.0f;
        private const string LayerPresetIconName = "icon_Settings_v1";
        private static GUIContent layerPresetIconContent;

        private void DrawLayerPresetButton(Rect rect, SerializedProperty element)
        {
            if (element == null)
            {
                return;
            }

            GUIContent content = GetLayerPresetIconContent();
            Texture icon = content.image;
            if (icon != null)
            {
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
            }
            else
            {
                EditorGUI.LabelField(rect, content);
            }

            GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                ShowLayerPresetMenu(element);
                Event.current.Use();
            }
        }

        private static GUIContent GetLayerPresetIconContent()
        {
            if (layerPresetIconContent != null)
            {
                return layerPresetIconContent;
            }

            Texture2D icon = LoadEffectIcon(LayerPresetIconName);
            layerPresetIconContent = icon != null
                ? new GUIContent(icon, "预设")
                : new GUIContent("P", "预设");
            return layerPresetIconContent;
        }

        private void ShowLayerPresetMenu(SerializedProperty element)
        {
            ImageProcessEffect effect = GetEffect(element);
            string propertyPath = element.propertyPath;
            GenericMenu menu = new GenericMenu();
            AddImageProcessPresetMenuItem(menu, propertyPath, effect, "默认", ApplyImageProcessDefaultPreset);
            AddImageProcessSpecificPresetMenuItems(menu, propertyPath, effect);
            menu.ShowAsContext();
        }

        private void AddImageProcessPresetMenuItem(
            GenericMenu menu,
            string propertyPath,
            ImageProcessEffect effect,
            string label,
            Action<SerializedProperty, ImageProcessEffect> apply)
        {
            menu.AddItem(new GUIContent(label), false, () => ApplyImageProcessPreset(propertyPath, effect, apply));
        }

        private void ApplyImageProcessPreset(
            string propertyPath,
            ImageProcessEffect effect,
            Action<SerializedProperty, ImageProcessEffect> apply)
        {
            serializedObject.Update();
            SerializedProperty element = serializedObject.FindProperty(propertyPath);
            if (element == null)
            {
                return;
            }

            bool wasExpanded = element.isExpanded;
            bool wasEnabled = GetBoolValue(element, "enabled", true);
            Undo.RecordObject(serializedObject.targetObject, "Apply ImageProcess Preset");
            apply(element, effect);
            SetEnum(element, "effect", (int)effect);
            SetBool(element, "enabled", wasEnabled);
            element.isExpanded = wasExpanded;
            CleanupLayersForUserOrder();
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        private void AddImageProcessSpecificPresetMenuItems(GenericMenu menu, string propertyPath, ImageProcessEffect effect)
        {
            switch (effect)
            {
                case ImageProcessEffect.SharpenBefore:
                case ImageProcessEffect.SharpenAfter:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和锐化", ApplyImageProcessSoftSharpenPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "清晰锐化", ApplyImageProcessClearSharpenPreset);
                    break;
                case ImageProcessEffect.AutoWhiteBalance:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "暖色校正", ApplyImageProcessWarmWhiteBalancePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "冷色校正", ApplyImageProcessCoolWhiteBalancePreset);
                    break;
                case ImageProcessEffect.LevelAdjustment:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "增强对比", ApplyImageProcessContrastLevelPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和褪色", ApplyImageProcessFadeLevelPreset);
                    break;
                case ImageProcessEffect.ColorGradingCustom:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "暖调", ApplyImageProcessWarmGradePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "冷调", ApplyImageProcessCoolGradePreset);
                    break;
                case ImageProcessEffect.Gradient:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "顶光", ApplyImageProcessTopLightGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "底光", ApplyImageProcessBottomLightGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "黄昏", ApplyImageProcessDuskGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "黄昏夜晚", ApplyImageProcessDuskNightGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "冷月夜", ApplyImageProcessMoonNightGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "压暗圆形", ApplyImageProcessRadialShadeGradientPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "暖色叠光", ApplyImageProcessWarmOverlayGradientPreset);
                    break;
                case ImageProcessEffect.Glow:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和发光", ApplyImageProcessSoftGlowPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强发光", ApplyImageProcessStrongGlowPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "星芒", ApplyImageProcessStarGlowPreset);
                    break;
                case ImageProcessEffect.CenterColorCorrection:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "中心提亮", ApplyImageProcessCenterBrightPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "中心降饱和", ApplyImageProcessCenterDesaturatePreset);
                    break;
                case ImageProcessEffect.Kuwahara:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和绘画", ApplyImageProcessSoftKuwaharaPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "线稿强化", ApplyImageProcessLineKuwaharaPreset);
                    break;
                case ImageProcessEffect.Weather:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "雨", ApplyImageProcessRainWeatherPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "雪", ApplyImageProcessSnowWeatherPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "烟雾", ApplyImageProcessSmokeWeatherPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "灰尘", ApplyImageProcessDustWeatherPreset);
                    break;
                case ImageProcessEffect.GlitchArt:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "赛博慢抖", ApplyImageProcessCyberGlitchArtPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻微故障", ApplyImageProcessSoftGlitchArtPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强烈故障", ApplyImageProcessStrongGlitchArtPreset);
                    break;
                case ImageProcessEffect.PrismFracture:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和棱镜", ApplyImageProcessSoftPrismFracturePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "中心碎裂", ApplyImageProcessCenterPrismFracturePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强彩虹折射", ApplyImageProcessRainbowPrismFracturePreset);
                    break;
                case ImageProcessEffect.SpeedLines:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "白色光晕", ApplyImageProcessWhiteSpeedLinesPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "黑色漫画", ApplyImageProcessBlackMangaSpeedLinesPreset);
                    break;
                case ImageProcessEffect.SkyGodRays:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "右上天光", ApplyImageProcessLeftSkyGodRaysPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "彩边神光", ApplyImageProcessChromaticSkyGodRaysPreset);
                    break;
                case ImageProcessEffect.FilmBreathGateWeave:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "60年代单色", ApplyImageProcessFilm60Preset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "80年代彩色", ApplyImageProcessFilm80Preset);
                    break;
                case ImageProcessEffect.VHS:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻微 VHS", ApplyImageProcessSoftVhsPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强烈 VHS", ApplyImageProcessStrongVhsPreset);
                    break;
                case ImageProcessEffect.Tube:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "70年代电视", ApplyImageProcessTube70Preset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "90年代电视", ApplyImageProcessTube90Preset);
                    break;
                case ImageProcessEffect.CRTEffects:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "RGB 扫描线", ApplyImageProcessRgbCrtPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "线条扫描线", ApplyImageProcessLineCrtPreset);
                    break;
                case ImageProcessEffect.DitheringCustom:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "复古单色", ApplyImageProcessMonoDitherPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "低色彩", ApplyImageProcessColorDitherPreset);
                    break;
                case ImageProcessEffect.IrisBlur:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻光圈模糊", ApplyImageProcessSoftIrisBlurPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强光圈模糊", ApplyImageProcessStrongIrisBlurPreset);
                    break;
                case ImageProcessEffect.RGBBlurV2:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻通道模糊", ApplyImageProcessSoftRgbBlurPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "蓝红拖影", ApplyImageProcessChromaticRgbBlurPreset);
                    break;
                case ImageProcessEffect.RGBSplit:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "斜向分离", ApplyImageProcessDiagonalRgbSplitPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "径向色差", ApplyImageProcessRadialRgbSplitPreset);
                    break;
                case ImageProcessEffect.RGBChannelSeparator:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "红通道", ApplyImageProcessRedChannelPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "蓝通道", ApplyImageProcessBlueChannelPreset);
                    break;
                case ImageProcessEffect.BokehZoomBlur:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和光斑", ApplyImageProcessSoftBokehZoomPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "强光拖影", ApplyImageProcessStrongBokehZoomPreset);
                    break;
                case ImageProcessEffect.ApertureBokeh:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "柔和散景", ApplyImageProcessSoftAperturePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "硬质光斑", ApplyImageProcessHardAperturePreset);
                    break;
                case ImageProcessEffect.LensFlare:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "明亮太阳", ApplyImageProcessBrightSunLensFlarePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "电影横光", ApplyImageProcessCinematicLensFlarePreset);
                    break;
                case ImageProcessEffect.GrainCustom:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "细颗粒", ApplyImageProcessFineGrainPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "粗颗粒", ApplyImageProcessCoarseGrainPreset);
                    break;
                case ImageProcessEffect.VignetteCustom:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻暗角", ApplyImageProcessSoftVignettePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "重暗角", ApplyImageProcessStrongVignettePreset);
                    break;
                case ImageProcessEffect.Pixelize:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻像素化", ApplyImageProcessSoftPixelizePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "粗像素化", ApplyImageProcessCoarsePixelizePreset);
                    break;
                case ImageProcessEffect.ChangeFrameRate:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "12 FPS", ApplyImageProcess12FpsPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "24 FPS", ApplyImageProcess24FpsPreset);
                    break;
                case ImageProcessEffect.Distortion:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "热浪", ApplyImageProcessHeatDistortionPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "湍流", ApplyImageProcessTurbulenceDistortionPreset);
                    break;
                case ImageProcessEffect.Fisheye:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "轻镜头", ApplyImageProcessSoftFisheyePreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "圆形鱼眼", ApplyImageProcessCircularFisheyePreset);
                    break;
                case ImageProcessEffect.ToonMap:
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "Neutral", ApplyImageProcessNeutralToonMapPreset);
                    AddImageProcessPresetMenuItem(menu, propertyPath, effect, "ACES", ApplyImageProcessAcesToonMapPreset);
                    break;
            }
        }

        private static void ApplyImageProcessDefaultPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ResetLayerDefaults(element);
            SetEnum(element, "effect", (int)effect);
            ResetEffectDefaults(element, effect);
        }

        private static void ApplyImageProcessSoftSharpenPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.35f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessClearSharpenPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.7f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessWarmWhiteBalancePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(18.0f, 4.0f, 1.0f, 0.0f));
        }

        private static void ApplyImageProcessCoolWhiteBalancePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(-18.0f, -3.0f, 1.0f, 0.0f));
        }

        private static void ApplyImageProcessContrastLevelPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.08f, 0.92f, 1.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker));
        }

        private static void ApplyImageProcessFadeLevelPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 1.12f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker));
        }

        private static void ApplyImageProcessWarmGradePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(1.0f, 0.95f, 0.88f, 0.03f));
            SetVector4(element, "parameters2", new Vector4(1.08f, 1.02f, 0.92f, 0.04f));
        }

        private static void ApplyImageProcessCoolGradePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.96f, 1.08f, 0.02f));
            SetVector4(element, "parameters2", new Vector4(0.92f, 1.0f, 1.12f, 0.03f));
        }

        private static void ApplyImageProcessTopLightGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 5.0f, 0.45f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.04f, 0.05f, 0.07f, 1.0f));
        }

        private static void ApplyImageProcessBottomLightGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.86f, 0.62f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 5.0f, 0.42f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.03f, 0.04f, 0.06f, 1.0f));
        }

        private static void ApplyImageProcessDuskGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.58f, 0.25f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.25f, 6.0f, 0.52f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.12f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.10f, 0.16f, 0.34f, 1.0f));
        }

        private static void ApplyImageProcessDuskNightGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(0.95f, 0.42f, 0.18f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.18f, 6.5f, 0.58f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.18f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.015f, 0.025f, 0.09f, 1.0f));
        }

        private static void ApplyImageProcessMoonNightGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(0.70f, 0.82f, 1.0f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.05f, 5.5f, 0.45f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.04f, -20.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.01f, 0.015f, 0.05f, 1.0f));
        }

        private static void ApplyImageProcessRadialShadeGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.Multiply);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.2f, 4.0f, 0.45f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyImageProcessWarmOverlayGradientPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ImageProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.72f, 0.42f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.1f, 4.0f, 0.35f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.15f, -35.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.14f, 0.23f, 0.5f, 1.0f));
        }

        private static void ApplyImageProcessSoftGlowPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.2f, 0.12f, 2.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.18f, 0.0f, 0.0f, 0.75f));
        }

        private static void ApplyImageProcessStrongGlowPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.18f, 3.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 0.02f, 0.0f, 0.9f));
        }

        private static void ApplyImageProcessStarGlowPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessStrongGlowPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.1f, 0.12f, 2.6f, 2.0f));
            SetVector4(element, "parameters1", new Vector4(0.35f, 0.0f, 0.0f, 0.85f));
            SetVector4(element, "parameters2", new Vector4(4.0f, 45.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessCenterBrightPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.12f, 0.16f, 0.08f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.55f, 0.45f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.85f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessCenterDesaturatePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(-0.45f, 0.02f, 0.12f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.6f, 0.55f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.75f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessSoftKuwaharaPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.0f, 0.0f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.12f, 0.02f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessLineKuwaharaPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(4.0f, 2.0f, 10.0f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.65f, 0.04f, 0.0f, 0.0f));
            SetColor(element, "color", Color.black);
        }

        private static void ApplyImageProcessRainWeatherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessWeatherPreset(element, effect, 0, Color.white, new Vector4(1.4f, 1.2f, 0.9f, 0.35f), new Vector4(1.2f, 1.2f, 0.8f, 0.9f));
        }

        private static void ApplyImageProcessSnowWeatherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessWeatherPreset(element, effect, 1, Color.white, new Vector4(0.55f, 1.15f, 1.25f, 0.55f), new Vector4(1.25f, 1.0f, 0.9f, 0.6f));
        }

        private static void ApplyImageProcessSmokeWeatherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessWeatherPreset(element, effect, 2, new Color(0.72f, 0.72f, 0.72f, 1.0f), new Vector4(0.35f, 0.8f, 1.6f, 0.8f), new Vector4(1.5f, 0.7f, 0.6f, 1.3f));
        }

        private static void ApplyImageProcessDustWeatherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessWeatherPreset(element, effect, 3, Color.black, new Vector4(1.54f, 2.88f, 2.0f, 0.8f), new Vector4(1.05f, 2.0f, 2.0f, 0.55f));
            SetVector4(element, "parameters0", new Vector4(3.0f, 1.0f, 1.0f, 0.85f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 0.30f, 1.0f, 0.0f));
        }

        private static void ApplyImageProcessSoftGlitchArtPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.08f, 0.18f, 0.10f, 0.05f));
            SetVector4(element, "parameters1", new Vector4(0.24f, 0.90f, 0.02f, 6.0f));
        }

        private static void ApplyImageProcessCyberGlitchArtPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.18f, 0.34f, 0.24f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.42f, 1.10f, 0.04f, 7.0f));
        }

        private static void ApplyImageProcessStrongGlitchArtPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.32f, 0.55f, 0.42f, 0.22f));
            SetVector4(element, "parameters1", new Vector4(0.62f, 1.35f, 0.08f, 9.0f));
        }

        private static void ApplyImageProcessSoftPrismFracturePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.36f, 0.18f));
            SetVector4(element, "parameters1", new Vector4(0.30f, 0.46f, 10.0f, -8.0f));
            SetVector4(element, "parameters2", new Vector4(0.22f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessCenterPrismFracturePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.42f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.58f, 0.74f, 15.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.38f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessRainbowPrismFracturePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.48f, 0.10f));
            SetVector4(element, "parameters1", new Vector4(0.78f, 1.0f, 20.0f, 14.0f));
            SetVector4(element, "parameters2", new Vector4(0.52f, 4.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessWhiteSpeedLinesPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.30f, 0.76f));
            SetVector4(element, "parameters1", new Vector4(74.0f, 0.22f, 0.68f, 0.18f));
            SetVector4(element, "parameters2", new Vector4(1.4f, 0.0f, 0.12f, 2.0f));
            SetVector4(element, "parameters3", new Vector4(0.35f, 0.10f, 8.0f, 0.12f));
        }

        private static void ApplyImageProcessBlackMangaSpeedLinesPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.34f, 0.92f));
            SetVector4(element, "parameters1", new Vector4(112.0f, 0.24f, 0.86f, 0.04f));
            SetVector4(element, "parameters2", new Vector4(1.8f, 0.0f, 0.10f, 11.0f));
            SetVector4(element, "parameters3", new Vector4(0.72f, 0.0f, 7.0f, 0.10f));
        }

        private static void ApplyImageProcessLeftSkyGodRaysPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.22f, 0.99f, 181.0f, 1.08f));
            SetVector4(element, "parameters1", new Vector4(130.0f, 85.0f, 234.0f, -53.0f));
            SetVector4(element, "parameters2", new Vector4(1.04f, 146.0f, 3.0f, 3.0f));
            SetVector4(element, "parameters3", new Vector4(32.0f, 0.36f, 0.0f, 0.21f));
        }

        private static void ApplyImageProcessChromaticSkyGodRaysPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.22f, 0.99f, 185.0f, 1.14f));
            SetVector4(element, "parameters1", new Vector4(124.0f, 80.0f, 250.0f, -58.0f));
            SetVector4(element, "parameters2", new Vector4(1.28f, 158.0f, 3.0f, 13.0f));
            SetVector4(element, "parameters3", new Vector4(34.0f, 0.34f, 0.0f, 0.28f));
        }

        private static void ApplyImageProcessWeatherPreset(SerializedProperty element, ImageProcessEffect effect, int particle, Color color, Vector4 particleParams, Vector4 variationParams)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", color);
            SetVector4(element, "parameters0", new Vector4(particle, 1.0f, 1.0f, 0.85f));
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.35f, 1.0f, particle == 0 ? 1.0f : 2.0f));
            SetVector4(element, "parameters2", particleParams);
            SetVector4(element, "parameters3", variationParams);
        }

        private static void ApplyImageProcessFilm60Preset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.15f));
            SetVector4(element, "parameters1", new Vector4(0.28f, 1.35f, 1.1f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, FilmInitMarker));
            SetObjectReference(element, "texture", LoadFilmLutTexture(0, 0));
        }

        private static void ApplyImageProcessFilm80Preset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.0f, 0.85f, 0.25f));
            SetVector4(element, "parameters1", new Vector4(0.22f, 1.0f, 0.65f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, FilmInitMarker));
            SetObjectReference(element, "texture", LoadFilmLutTexture(2, 1));
        }

        private static void ApplyImageProcessSoftVhsPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.2f, 0.25f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.8f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyImageProcessStrongVhsPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
            SetVector4(element, "parameters0", new Vector4(2.0f, 0.8f, 0.55f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.35f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyImageProcessTube70Preset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadTubeLutTexture(1));
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.2f, 0.8f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
        }

        private static void ApplyImageProcessTube90Preset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadTubeLutTexture(3));
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.35f, 1.1f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
        }

        private static void ApplyImageProcessRgbCrtPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadCrtEffectsTexture(0));
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.85f, 3.0f, 0.0f));
        }

        private static void ApplyImageProcessLineCrtPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadCrtEffectsTexture(3));
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.65f, 1.5f, 0.0f));
        }

        private static void ApplyImageProcessMonoDitherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDitheringTexture(2));
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.55f, 2.0f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(4.0f, 32.0f, 32.0f, 32.0f));
        }

        private static void ApplyImageProcessColorDitherPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDitheringTexture(1));
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.45f, 1.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(4.0f, 8.0f, 8.0f, 8.0f));
            SetVector4(element, "parameters2", new Vector4(0.65f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessSoftIrisBlurPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(1.5f, 2.0f, 3.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.78f, 0.18f));
        }

        private static void ApplyImageProcessStrongIrisBlurPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(5.0f, 2.0f, 3.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.45f, 0.25f));
        }

        private static void ApplyImageProcessSoftRgbBlurPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.12f, 0.12f, 0.12f, 0.0f));
        }

        private static void ApplyImageProcessChromaticRgbBlurPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.28f, 0.05f, 0.35f, 0.0f));
        }

        private static void ApplyImageProcessDiagonalRgbSplitPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.35f, 35.0f, RgbSplitInitMarker));
        }

        private static void ApplyImageProcessRadialRgbSplitPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.45f, 0.0f, RgbSplitInitMarker));
        }

        private static void ApplyImageProcessRedChannelPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessBlueChannelPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessSoftBokehZoomPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.45f, 0.3f, 0.25f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 3.0f, 1.0f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 1.2f, 0.0f));
        }

        private static void ApplyImageProcessStrongBokehZoomPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.15f, 1.25f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 4.0f, 2.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.85f, 20.0f, 0.75f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 3.5f, 0.0f));
        }

        private static void ApplyImageProcessSoftAperturePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.55f, 0.45f, 0.35f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.25f, 0.7f, 0.0f, 1.0f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 1.8f, 0.0f));
        }

        private static void ApplyImageProcessHardAperturePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.8f, 0.35f, 0.15f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 1.0f, 0.0f, 2.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.75f, 12.0f, 0.45f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 4.5f, 0.0f));
        }

        private static void ApplyImageProcessBrightSunLensFlarePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.86f, 0.55f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(-0.38f, 0.32f, -18.0f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.070f, 0.38f, 1.0f, 7.0f));
            SetVector4(element, "parameters2", new Vector4(0.90f, 1.08f, 0.62f, 0.62f));
            SetVector4(element, "parameters3", new Vector4(0.90f, 2.7f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessCinematicLensFlarePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.78f, 0.88f, 1.0f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(-0.55f, 0.18f, 0.0f, 1.35f));
            SetVector4(element, "parameters1", new Vector4(0.040f, 0.24f, 0.65f, 4.0f));
            SetVector4(element, "parameters2", new Vector4(0.55f, 1.20f, 0.38f, 0.85f));
            SetVector4(element, "parameters3", new Vector4(1.65f, 1.9f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessFineGrainPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.28f, 1.2f, 0.8f, 0.0f));
        }

        private static void ApplyImageProcessCoarseGrainPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.68f, 2.8f, 0.95f, 0.0f));
        }

        private static void ApplyImageProcessSoftVignettePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.45f, 0.9f, 0.35f));
        }

        private static void ApplyImageProcessStrongVignettePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.55f, 1.0f, 0.8f));
        }

        private static void ApplyImageProcessSoftPixelizePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 0.75f));
        }

        private static void ApplyImageProcessCoarsePixelizePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 0.32f));
        }

        private static void ApplyImageProcess12FpsPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(12.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcess24FpsPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(24.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessHeatDistortionPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.08f, 0.03f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(2.0f, 1.0f, -0.1f, -0.7f));
        }

        private static void ApplyImageProcessTurbulenceDistortionPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
            SetVector4(element, "parameters0", new Vector4(6.0f, 0.25f, 0.25f, 0.4f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.6f, -0.8f));
        }

        private static void ApplyImageProcessSoftFisheyePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.18f, 1.0f, 0.15f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessCircularFisheyePreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.65f, 0.82f, 0.08f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessNeutralToonMapPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyImageProcessAcesToonMapPreset(SerializedProperty element, ImageProcessEffect effect)
        {
            ApplyImageProcessDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(2.0f, 0.0f, 0.0f, 0.0f));
        }

        private static bool GetBoolValue(SerializedProperty element, string name, bool fallback)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null && property.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue
                : fallback;
        }
    }
}
