using System;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
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
            ShoostPostProcessEffect effect = GetEffect(element);
            string propertyPath = element.propertyPath;
            GenericMenu menu = new GenericMenu();
            AddShoostPresetMenuItem(menu, propertyPath, effect, "默认", ApplyShoostDefaultPreset);
            AddShoostSpecificPresetMenuItems(menu, propertyPath, effect);
            menu.ShowAsContext();
        }

        private void AddShoostPresetMenuItem(
            GenericMenu menu,
            string propertyPath,
            ShoostPostProcessEffect effect,
            string label,
            Action<SerializedProperty, ShoostPostProcessEffect> apply)
        {
            menu.AddItem(new GUIContent(label), false, () => ApplyShoostPreset(propertyPath, effect, apply));
        }

        private void ApplyShoostPreset(
            string propertyPath,
            ShoostPostProcessEffect effect,
            Action<SerializedProperty, ShoostPostProcessEffect> apply)
        {
            serializedObject.Update();
            SerializedProperty element = serializedObject.FindProperty(propertyPath);
            if (element == null)
            {
                return;
            }

            bool wasExpanded = element.isExpanded;
            bool wasEnabled = GetBoolValue(element, "enabled", true);
            Undo.RecordObject(serializedObject.targetObject, "Apply Shoost Preset");
            apply(element, effect);
            SetEnum(element, "effect", (int)effect);
            SetBool(element, "enabled", wasEnabled);
            element.isExpanded = wasExpanded;
            SortLayersByEffectOrder();
            serializedObject.ApplyModifiedProperties();
            if (serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(serializedObject.targetObject);
            }
        }

        private void AddShoostSpecificPresetMenuItems(GenericMenu menu, string propertyPath, ShoostPostProcessEffect effect)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.SharpenBefore:
                case ShoostPostProcessEffect.SharpenAfter:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和锐化", ApplyShoostSoftSharpenPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "清晰锐化", ApplyShoostClearSharpenPreset);
                    break;
                case ShoostPostProcessEffect.AutoWhiteBalance:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "暖色校正", ApplyShoostWarmWhiteBalancePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "冷色校正", ApplyShoostCoolWhiteBalancePreset);
                    break;
                case ShoostPostProcessEffect.LevelAdjustment:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "增强对比", ApplyShoostContrastLevelPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和褪色", ApplyShoostFadeLevelPreset);
                    break;
                case ShoostPostProcessEffect.ColorGradingCustom:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "暖调", ApplyShoostWarmGradePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "冷调", ApplyShoostCoolGradePreset);
                    break;
                case ShoostPostProcessEffect.Gradient:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "顶光", ApplyShoostTopLightGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "底光", ApplyShoostBottomLightGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "黄昏", ApplyShoostDuskGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "黄昏夜晚", ApplyShoostDuskNightGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "冷月夜", ApplyShoostMoonNightGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "压暗圆形", ApplyShoostRadialShadeGradientPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "暖色叠光", ApplyShoostWarmOverlayGradientPreset);
                    break;
                case ShoostPostProcessEffect.Glow:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和发光", ApplyShoostSoftGlowPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强发光", ApplyShoostStrongGlowPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "星芒", ApplyShoostStarGlowPreset);
                    break;
                case ShoostPostProcessEffect.CenterColorCorrection:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "中心提亮", ApplyShoostCenterBrightPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "中心降饱和", ApplyShoostCenterDesaturatePreset);
                    break;
                case ShoostPostProcessEffect.Kuwahara:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和绘画", ApplyShoostSoftKuwaharaPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "线稿强化", ApplyShoostLineKuwaharaPreset);
                    break;
                case ShoostPostProcessEffect.Weather:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "雨", ApplyShoostRainWeatherPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "雪", ApplyShoostSnowWeatherPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "烟雾", ApplyShoostSmokeWeatherPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "灰尘", ApplyShoostDustWeatherPreset);
                    break;
                case ShoostPostProcessEffect.GlitchArt:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "赛博慢抖", ApplyShoostCyberGlitchArtPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻微故障", ApplyShoostSoftGlitchArtPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强烈故障", ApplyShoostStrongGlitchArtPreset);
                    break;
                case ShoostPostProcessEffect.PrismFracture:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和棱镜", ApplyShoostSoftPrismFracturePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "中心碎裂", ApplyShoostCenterPrismFracturePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强彩虹折射", ApplyShoostRainbowPrismFracturePreset);
                    break;
                case ShoostPostProcessEffect.SpeedLines:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "白色光晕", ApplyShoostWhiteSpeedLinesPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "黑色漫画", ApplyShoostBlackMangaSpeedLinesPreset);
                    break;
                case ShoostPostProcessEffect.SkyGodRays:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "右上天光", ApplyShoostLeftSkyGodRaysPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "彩边神光", ApplyShoostChromaticSkyGodRaysPreset);
                    break;
                case ShoostPostProcessEffect.FilmBreathGateWeave:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "60年代单色", ApplyShoostFilm60Preset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "80年代彩色", ApplyShoostFilm80Preset);
                    break;
                case ShoostPostProcessEffect.VHS:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻微 VHS", ApplyShoostSoftVhsPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强烈 VHS", ApplyShoostStrongVhsPreset);
                    break;
                case ShoostPostProcessEffect.Tube:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "70年代电视", ApplyShoostTube70Preset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "90年代电视", ApplyShoostTube90Preset);
                    break;
                case ShoostPostProcessEffect.CRTEffects:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "RGB 扫描线", ApplyShoostRgbCrtPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "线条扫描线", ApplyShoostLineCrtPreset);
                    break;
                case ShoostPostProcessEffect.DitheringCustom:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "复古单色", ApplyShoostMonoDitherPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "低色彩", ApplyShoostColorDitherPreset);
                    break;
                case ShoostPostProcessEffect.IrisBlur:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻光圈模糊", ApplyShoostSoftIrisBlurPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强光圈模糊", ApplyShoostStrongIrisBlurPreset);
                    break;
                case ShoostPostProcessEffect.RGBBlurV2:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻通道模糊", ApplyShoostSoftRgbBlurPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "蓝红拖影", ApplyShoostChromaticRgbBlurPreset);
                    break;
                case ShoostPostProcessEffect.RGBSplit:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "斜向分离", ApplyShoostDiagonalRgbSplitPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "径向色差", ApplyShoostRadialRgbSplitPreset);
                    break;
                case ShoostPostProcessEffect.RGBChannelSeparator:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "红通道", ApplyShoostRedChannelPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "蓝通道", ApplyShoostBlueChannelPreset);
                    break;
                case ShoostPostProcessEffect.BokehZoomBlur:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和光斑", ApplyShoostSoftBokehZoomPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "强光拖影", ApplyShoostStrongBokehZoomPreset);
                    break;
                case ShoostPostProcessEffect.ApertureBokeh:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "柔和散景", ApplyShoostSoftAperturePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "硬质光斑", ApplyShoostHardAperturePreset);
                    break;
                case ShoostPostProcessEffect.LensFlare:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "明亮太阳", ApplyShoostBrightSunLensFlarePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "电影横光", ApplyShoostCinematicLensFlarePreset);
                    break;
                case ShoostPostProcessEffect.GrainCustom:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "细颗粒", ApplyShoostFineGrainPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "粗颗粒", ApplyShoostCoarseGrainPreset);
                    break;
                case ShoostPostProcessEffect.VignetteCustom:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻暗角", ApplyShoostSoftVignettePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "重暗角", ApplyShoostStrongVignettePreset);
                    break;
                case ShoostPostProcessEffect.Pixelize:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻像素化", ApplyShoostSoftPixelizePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "粗像素化", ApplyShoostCoarsePixelizePreset);
                    break;
                case ShoostPostProcessEffect.ChangeFrameRate:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "12 FPS", ApplyShoost12FpsPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "24 FPS", ApplyShoost24FpsPreset);
                    break;
                case ShoostPostProcessEffect.Distortion:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "热浪", ApplyShoostHeatDistortionPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "湍流", ApplyShoostTurbulenceDistortionPreset);
                    break;
                case ShoostPostProcessEffect.Fisheye:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "轻镜头", ApplyShoostSoftFisheyePreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "圆形鱼眼", ApplyShoostCircularFisheyePreset);
                    break;
                case ShoostPostProcessEffect.ToonMap:
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "Neutral", ApplyShoostNeutralToonMapPreset);
                    AddShoostPresetMenuItem(menu, propertyPath, effect, "ACES", ApplyShoostAcesToonMapPreset);
                    break;
            }
        }

        private static void ApplyShoostDefaultPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ResetLayerDefaults(element);
            SetEnum(element, "effect", (int)effect);
            ResetEffectDefaults(element, effect);
        }

        private static void ApplyShoostSoftSharpenPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.35f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostClearSharpenPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.7f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostWarmWhiteBalancePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(18.0f, 4.0f, 1.0f, 0.0f));
        }

        private static void ApplyShoostCoolWhiteBalancePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(-18.0f, -3.0f, 1.0f, 0.0f));
        }

        private static void ApplyShoostContrastLevelPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.08f, 0.92f, 1.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker));
        }

        private static void ApplyShoostFadeLevelPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1.0f, 1.12f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(1.0f, 0.0f, 0.0f, LevelAdjustmentInitMarker));
        }

        private static void ApplyShoostWarmGradePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(1.0f, 0.95f, 0.88f, 0.03f));
            SetVector4(element, "parameters2", new Vector4(1.08f, 1.02f, 0.92f, 0.04f));
        }

        private static void ApplyShoostCoolGradePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.96f, 1.08f, 0.02f));
            SetVector4(element, "parameters2", new Vector4(0.92f, 1.0f, 1.12f, 0.03f));
        }

        private static void ApplyShoostTopLightGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 5.0f, 0.45f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.04f, 0.05f, 0.07f, 1.0f));
        }

        private static void ApplyShoostBottomLightGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.86f, 0.62f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.0f, 5.0f, 0.42f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.03f, 0.04f, 0.06f, 1.0f));
        }

        private static void ApplyShoostDuskGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.58f, 0.25f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.25f, 6.0f, 0.52f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.12f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.10f, 0.16f, 0.34f, 1.0f));
        }

        private static void ApplyShoostDuskNightGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(0.95f, 0.42f, 0.18f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.18f, 6.5f, 0.58f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.18f, 180.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.015f, 0.025f, 0.09f, 1.0f));
        }

        private static void ApplyShoostMoonNightGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(0.70f, 0.82f, 1.0f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.05f, 5.5f, 0.45f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.04f, -20.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.01f, 0.015f, 0.05f, 1.0f));
        }

        private static void ApplyShoostRadialShadeGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.Multiply);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.2f, 4.0f, 0.45f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyShoostWarmOverlayGradientPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetEnum(element, "blendMode", (int)ShoostPostProcessBlendMode.SoftLight);
            SetColor(element, "color", new Color(1.0f, 0.72f, 0.42f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(1.0f, 1.1f, 4.0f, 0.35f));
            SetVector4(element, "parameters1", new Vector4(0.0f, -0.15f, -35.0f, 0.0f));
            SetVector4(element, "parameters3", new Vector4(0.14f, 0.23f, 0.5f, 1.0f));
        }

        private static void ApplyShoostSoftGlowPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.2f, 0.12f, 2.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.18f, 0.0f, 0.0f, 0.75f));
        }

        private static void ApplyShoostStrongGlowPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.18f, 3.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 0.02f, 0.0f, 0.9f));
        }

        private static void ApplyShoostStarGlowPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostStrongGlowPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.1f, 0.12f, 2.6f, 2.0f));
            SetVector4(element, "parameters1", new Vector4(0.35f, 0.0f, 0.0f, 0.85f));
            SetVector4(element, "parameters2", new Vector4(4.0f, 45.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostCenterBrightPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.12f, 0.16f, 0.08f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.55f, 0.45f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.85f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostCenterDesaturatePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(-0.45f, 0.02f, 0.12f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(0.6f, 0.55f, 0.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.75f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostSoftKuwaharaPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.0f, 0.0f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.12f, 0.02f, 0.0f, 0.0f));
        }

        private static void ApplyShoostLineKuwaharaPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(4.0f, 2.0f, 10.0f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(0.65f, 0.04f, 0.0f, 0.0f));
            SetColor(element, "color", Color.black);
        }

        private static void ApplyShoostRainWeatherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostWeatherPreset(element, effect, 0, Color.white, new Vector4(1.4f, 1.2f, 0.9f, 0.35f), new Vector4(1.2f, 1.2f, 0.8f, 0.9f));
        }

        private static void ApplyShoostSnowWeatherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostWeatherPreset(element, effect, 1, Color.white, new Vector4(0.55f, 1.15f, 1.25f, 0.55f), new Vector4(1.25f, 1.0f, 0.9f, 0.6f));
        }

        private static void ApplyShoostSmokeWeatherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostWeatherPreset(element, effect, 2, new Color(0.72f, 0.72f, 0.72f, 1.0f), new Vector4(0.35f, 0.8f, 1.6f, 0.8f), new Vector4(1.5f, 0.7f, 0.6f, 1.3f));
        }

        private static void ApplyShoostDustWeatherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostWeatherPreset(element, effect, 3, Color.black, new Vector4(1.54f, 2.88f, 2.0f, 0.8f), new Vector4(1.05f, 2.0f, 2.0f, 0.55f));
            SetVector4(element, "parameters0", new Vector4(3.0f, 1.0f, 1.0f, 0.85f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 0.30f, 1.0f, 0.0f));
        }

        private static void ApplyShoostSoftGlitchArtPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.08f, 0.18f, 0.10f, 0.05f));
            SetVector4(element, "parameters1", new Vector4(0.24f, 0.90f, 0.02f, 6.0f));
        }

        private static void ApplyShoostCyberGlitchArtPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.18f, 0.34f, 0.24f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.42f, 1.10f, 0.04f, 7.0f));
        }

        private static void ApplyShoostStrongGlitchArtPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.32f, 0.55f, 0.42f, 0.22f));
            SetVector4(element, "parameters1", new Vector4(0.62f, 1.35f, 0.08f, 9.0f));
        }

        private static void ApplyShoostSoftPrismFracturePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.36f, 0.18f));
            SetVector4(element, "parameters1", new Vector4(0.30f, 0.46f, 10.0f, -8.0f));
            SetVector4(element, "parameters2", new Vector4(0.22f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostCenterPrismFracturePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.42f, 0.12f));
            SetVector4(element, "parameters1", new Vector4(0.58f, 0.74f, 15.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.38f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostRainbowPrismFracturePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.48f, 0.10f));
            SetVector4(element, "parameters1", new Vector4(0.78f, 1.0f, 20.0f, 14.0f));
            SetVector4(element, "parameters2", new Vector4(0.52f, 4.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostWhiteSpeedLinesPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.30f, 0.76f));
            SetVector4(element, "parameters1", new Vector4(74.0f, 0.22f, 0.68f, 0.18f));
            SetVector4(element, "parameters2", new Vector4(1.4f, 0.0f, 0.12f, 2.0f));
            SetVector4(element, "parameters3", new Vector4(0.35f, 0.10f, 8.0f, 0.12f));
        }

        private static void ApplyShoostBlackMangaSpeedLinesPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", Color.black);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.5f, 0.34f, 0.92f));
            SetVector4(element, "parameters1", new Vector4(112.0f, 0.24f, 0.86f, 0.04f));
            SetVector4(element, "parameters2", new Vector4(1.8f, 0.0f, 0.10f, 11.0f));
            SetVector4(element, "parameters3", new Vector4(0.72f, 0.0f, 7.0f, 0.10f));
        }

        private static void ApplyShoostLeftSkyGodRaysPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.22f, 0.99f, 181.0f, 1.08f));
            SetVector4(element, "parameters1", new Vector4(167.0f, 109.0f, 234.0f, -53.0f));
            SetVector4(element, "parameters2", new Vector4(1.04f, 146.0f, 3.0f, 3.0f));
            SetVector4(element, "parameters3", new Vector4(32.0f, 0.36f, 0.10f, 0.21f));
        }

        private static void ApplyShoostChromaticSkyGodRaysPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", Color.white);
            SetVector4(element, "parameters0", new Vector4(1.22f, 0.99f, 185.0f, 1.14f));
            SetVector4(element, "parameters1", new Vector4(160.0f, 104.0f, 250.0f, -58.0f));
            SetVector4(element, "parameters2", new Vector4(1.28f, 158.0f, 3.0f, 13.0f));
            SetVector4(element, "parameters3", new Vector4(34.0f, 0.34f, 0.10f, 0.28f));
        }

        private static void ApplyShoostWeatherPreset(SerializedProperty element, ShoostPostProcessEffect effect, int particle, Color color, Vector4 particleParams, Vector4 variationParams)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", color);
            SetVector4(element, "parameters0", new Vector4(particle, 1.0f, 1.0f, 0.85f));
            SetVector4(element, "parameters1", new Vector4(0.9f, 0.35f, 1.0f, particle == 0 ? 1.0f : 2.0f));
            SetVector4(element, "parameters2", particleParams);
            SetVector4(element, "parameters3", variationParams);
        }

        private static void ApplyShoostFilm60Preset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.0f, 1.0f, 0.15f));
            SetVector4(element, "parameters1", new Vector4(0.28f, 1.35f, 1.1f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, FilmInitMarker));
            SetObjectReference(element, "texture", LoadFilmLutTexture(0, 0));
        }

        private static void ApplyShoostFilm80Preset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(2.0f, 1.0f, 0.85f, 0.25f));
            SetVector4(element, "parameters1", new Vector4(0.22f, 1.0f, 0.65f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, FilmInitMarker));
            SetObjectReference(element, "texture", LoadFilmLutTexture(2, 1));
        }

        private static void ApplyShoostSoftVhsPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.2f, 0.25f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.8f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyShoostStrongVhsPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultVhsEdgeNoiseTexture());
            SetVector4(element, "parameters0", new Vector4(2.0f, 0.8f, 0.55f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.35f, 0.0f, 0.0f, 1.0f));
        }

        private static void ApplyShoostTube70Preset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadTubeLutTexture(1));
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.2f, 0.8f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
        }

        private static void ApplyShoostTube90Preset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadTubeLutTexture(3));
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.35f, 1.1f, 1.0f));
            SetVector4(element, "parameters2", new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker));
        }

        private static void ApplyShoostRgbCrtPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadCrtEffectsTexture(0));
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.85f, 3.0f, 0.0f));
        }

        private static void ApplyShoostLineCrtPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadCrtEffectsTexture(3));
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.65f, 1.5f, 0.0f));
        }

        private static void ApplyShoostMonoDitherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDitheringTexture(2));
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.55f, 2.0f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(4.0f, 32.0f, 32.0f, 32.0f));
        }

        private static void ApplyShoostColorDitherPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDitheringTexture(1));
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.45f, 1.0f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(4.0f, 8.0f, 8.0f, 8.0f));
            SetVector4(element, "parameters2", new Vector4(0.65f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostSoftIrisBlurPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(1.5f, 2.0f, 3.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.78f, 0.18f));
        }

        private static void ApplyShoostStrongIrisBlurPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters1", new Vector4(5.0f, 2.0f, 3.0f, 0.0f));
            SetVector4(element, "parameters2", new Vector4(0.5f, 0.5f, 0.45f, 0.25f));
        }

        private static void ApplyShoostSoftRgbBlurPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.12f, 0.12f, 0.12f, 0.0f));
        }

        private static void ApplyShoostChromaticRgbBlurPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.28f, 0.05f, 0.35f, 0.0f));
        }

        private static void ApplyShoostDiagonalRgbSplitPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 0.35f, 35.0f, RgbSplitInitMarker));
        }

        private static void ApplyShoostRadialRgbSplitPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.45f, 0.0f, RgbSplitInitMarker));
        }

        private static void ApplyShoostRedChannelPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostBlueChannelPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostSoftBokehZoomPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.45f, 0.3f, 0.25f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 3.0f, 1.0f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 1.2f, 0.0f));
        }

        private static void ApplyShoostStrongBokehZoomPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.15f, 1.25f));
            SetVector4(element, "parameters1", new Vector4(0.0f, 0.0f, 4.0f, 2.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.85f, 20.0f, 0.75f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 3.5f, 0.0f));
        }

        private static void ApplyShoostSoftAperturePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.55f, 0.45f, 0.35f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.25f, 0.7f, 0.0f, 1.0f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 1.8f, 0.0f));
        }

        private static void ApplyShoostHardAperturePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.8f, 0.35f, 0.15f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.45f, 1.0f, 0.0f, 2.0f));
            SetVector4(element, "parameters2", new Vector4(6.0f, 0.75f, 12.0f, 0.45f));
            SetVector4(element, "parameters3", new Vector4(0.0f, 0.0f, 4.5f, 0.0f));
        }

        private static void ApplyShoostBrightSunLensFlarePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(1.0f, 0.86f, 0.55f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(-0.38f, 0.32f, -18.0f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(0.070f, 0.38f, 1.0f, 7.0f));
            SetVector4(element, "parameters2", new Vector4(0.90f, 1.08f, 0.62f, 0.62f));
            SetVector4(element, "parameters3", new Vector4(0.90f, 2.7f, 0.0f, 0.0f));
        }

        private static void ApplyShoostCinematicLensFlarePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetColor(element, "color", new Color(0.78f, 0.88f, 1.0f, 1.0f));
            SetVector4(element, "parameters0", new Vector4(-0.55f, 0.18f, 0.0f, 1.35f));
            SetVector4(element, "parameters1", new Vector4(0.040f, 0.24f, 0.65f, 4.0f));
            SetVector4(element, "parameters2", new Vector4(0.55f, 1.20f, 0.38f, 0.85f));
            SetVector4(element, "parameters3", new Vector4(1.65f, 1.9f, 0.0f, 0.0f));
        }

        private static void ApplyShoostFineGrainPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.28f, 1.2f, 0.8f, 0.0f));
        }

        private static void ApplyShoostCoarseGrainPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.68f, 2.8f, 0.95f, 0.0f));
        }

        private static void ApplyShoostSoftVignettePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.45f, 0.9f, 0.35f));
        }

        private static void ApplyShoostStrongVignettePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.5f, 0.55f, 1.0f, 0.8f));
        }

        private static void ApplyShoostSoftPixelizePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 0.75f));
        }

        private static void ApplyShoostCoarsePixelizePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.0f, 1920.0f, 1080.0f, 0.32f));
        }

        private static void ApplyShoost12FpsPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(12.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoost24FpsPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(24.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostHeatDistortionPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
            SetVector4(element, "parameters0", new Vector4(3.0f, 0.08f, 0.03f, 0.08f));
            SetVector4(element, "parameters1", new Vector4(2.0f, 1.0f, -0.1f, -0.7f));
        }

        private static void ApplyShoostTurbulenceDistortionPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetObjectReference(element, "texture", LoadDefaultDistortionTexture());
            SetVector4(element, "parameters0", new Vector4(6.0f, 0.25f, 0.25f, 0.4f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.6f, -0.8f));
        }

        private static void ApplyShoostSoftFisheyePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.18f, 1.0f, 0.15f, 0.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostCircularFisheyePreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(0.65f, 0.82f, 0.08f, 1.0f));
            SetVector4(element, "parameters1", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostNeutralToonMapPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
            SetVector4(element, "parameters0", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
        }

        private static void ApplyShoostAcesToonMapPreset(SerializedProperty element, ShoostPostProcessEffect effect)
        {
            ApplyShoostDefaultPreset(element, effect);
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
