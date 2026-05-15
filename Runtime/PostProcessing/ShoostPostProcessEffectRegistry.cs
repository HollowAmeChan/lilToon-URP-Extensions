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
                case ShoostPostProcessEffect.ChangeFrameRate:
                    return "Hidden/lilToon-Shoost/URP/Shoost/ChangeFrameRate";
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
                case ShoostPostProcessEffect.IrisBlur:
                    return "Hidden/lilToon-Shoost/URP/Shoost/IrisBlur";
                case ShoostPostProcessEffect.KawaseBlur:
                    return "Hidden/lilToon-Shoost/URP/Shoost/KawaseBlur";
                case ShoostPostProcessEffect.LensDistortionCustom:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LensDistortionCustom";
                case ShoostPostProcessEffect.LevelAdjustment:
                    return "Hidden/lilToon-Shoost/URP/Shoost/LevelAdjustment";
                case ShoostPostProcessEffect.MotionTrail:
                    return "Hidden/lilToon-Shoost/URP/Shoost/MotionTrail";
                case ShoostPostProcessEffect.Pixelize:
                    return "Hidden/lilToon-Shoost/URP/Shoost/Pixelize";
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

        public static ShoostPostProcessInjectionPoint GetDefaultInjectionPoint(ShoostPostProcessEffect effect)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.CRTEffects:
                case ShoostPostProcessEffect.ColorGradingCustom:
                case ShoostPostProcessEffect.LevelAdjustment:
                case ShoostPostProcessEffect.RGBChannelSeparator:
                case ShoostPostProcessEffect.SharpenAfter:
                case ShoostPostProcessEffect.RetroLookProTVEffectCustom:
                    return ShoostPostProcessInjectionPoint.AfterURPPostProcessing;
                default:
                    return ShoostPostProcessInjectionPoint.BeforeURPPostProcessing;
            }
        }
    }
}
