namespace lilToon.URP.Extensions.PostProcessing
{
    internal static class ShoostPostProcessEffectRegistry
    {
        public static string GetDefaultShaderName(ShoostPostProcessEffect effect)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.CustomMaterial:
                    return ShoostPostProcessShaderConstants.DefaultLayerShaderName;
                case ShoostPostProcessEffect.AutoWhiteBalance:
                    return "Hidden/lilToon-Shoost/URP/Shoost/AutoWhiteBalance";
                case ShoostPostProcessEffect.BokehZoomBlur:
                    return "Hidden/lilToon-Shoost/URP/Shoost/BokehZoomBlur";
                case ShoostPostProcessEffect.ApertureBokeh:
                    return "Hidden/lilToon-Shoost/URP/Shoost/ApertureBokeh";
                case ShoostPostProcessEffect.LensFlare:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LensFlare";
                case ShoostPostProcessEffect.LogoOverlay:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LogoOverlay";
                case ShoostPostProcessEffect.ChangeFrameRate:
                    return "Hidden/lilToon-Shoost/URP/Shoost/ChangeFrameRate";
                case ShoostPostProcessEffect.CenterColorCorrection:
                    return "Hidden/lilToon-Shoost/URP/Shoost/CenterColorCorrection";
                case ShoostPostProcessEffect.CinematicBars:
                    return "Hidden/lilToon-Shoost/URP/Shoost/CinematicBars";
                case ShoostPostProcessEffect.ColorGradingCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/ColorGradingCustom";
                case ShoostPostProcessEffect.CRTEffects:
                    return "Hidden/lilToon-Shoost/URP/Shoost/CRTEffects";
                case ShoostPostProcessEffect.Distortion:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Distortion";
                case ShoostPostProcessEffect.DitheringCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/DitheringCustom";
                case ShoostPostProcessEffect.DownScaleResolution:
                    return "Hidden/lilToon-Shoost/URP/Shoost/DownScaleResolution";
                case ShoostPostProcessEffect.FilmBreathGateWeave:
                    return "Hidden/lilToon-Shoost/URP/Shoost/FilmBreathGateWeave";
                case ShoostPostProcessEffect.Fisheye:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Fisheye";
                case ShoostPostProcessEffect.GateWeave:
                    return "Hidden/lilToon-Shoost/URP/Shoost/GateWeave";
                case ShoostPostProcessEffect.GrainCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/GrainCustom";
                case ShoostPostProcessEffect.Gradient:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Gradient";
                case ShoostPostProcessEffect.Glow:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Glow";
                case ShoostPostProcessEffect.GlitchArt:
                    return "Hidden/lilToon-Shoost/URP/Shoost/GlitchArt";
                case ShoostPostProcessEffect.IrisBlur:
                    return "Hidden/lilToon-Shoost/URP/Shoost/IrisBlur";
                case ShoostPostProcessEffect.Kuwahara:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Kuwahara";
                case ShoostPostProcessEffect.LensDistortionCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LensDistortionCustom";
                case ShoostPostProcessEffect.LevelAdjustment:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LevelAdjustment";
                case ShoostPostProcessEffect.MotionTrail:
                    return "Hidden/lilToon-Shoost/URP/Shoost/MotionTrail";
                case ShoostPostProcessEffect.Pixelize:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Pixelize";
                case ShoostPostProcessEffect.PrismFracture:
                    return "Hidden/lilToon-Shoost/URP/Shoost/PrismFracture";
                case ShoostPostProcessEffect.SpeedLines:
                    return "Hidden/lilToon-Shoost/URP/Shoost/SpeedLines";
                case ShoostPostProcessEffect.SkyGodRays:
                    return "Hidden/lilToon-Shoost/URP/Shoost/SkyGodRays";
                case ShoostPostProcessEffect.RGBBlur:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RGBBlur";
                case ShoostPostProcessEffect.RGBBlurV2:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RGBBlurV2";
                case ShoostPostProcessEffect.RGBChannelSeparator:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RGBChannelSeparator";
                case ShoostPostProcessEffect.RGBSplit:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RGBSplit";
                case ShoostPostProcessEffect.SharpenBefore:
                case ShoostPostProcessEffect.SharpenAfter:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Sharpen";
                case ShoostPostProcessEffect.Tube:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Tube";
                case ShoostPostProcessEffect.ToonMap:
                    return "Hidden/lilToon-Shoost/URP/Shoost/ToonMap";
                case ShoostPostProcessEffect.VHS:
                    return "Hidden/lilToon-Shoost/URP/Shoost/VHS";
                case ShoostPostProcessEffect.Weather:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Weather";
                case ShoostPostProcessEffect.VignetteCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/VignetteCustom";
                case ShoostPostProcessEffect.RetroLookProBleedCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RetroLookProBleedCustom";
                case ShoostPostProcessEffect.RetroLookProNoise2Custom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RetroLookProNoise2Custom";
                case ShoostPostProcessEffect.RetroLookProOldFilm2Custom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RetroLookProOldFilm2Custom";
                case ShoostPostProcessEffect.RetroLookProTVEffectCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/RetroLookProTVEffectCustom";
                default:
                    return ShoostPostProcessShaderConstants.DefaultLayerShaderName;
            }
        }
    }
}
