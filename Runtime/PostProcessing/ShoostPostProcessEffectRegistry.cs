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
                    return "Hidden/lilToon/URP/Shoost/AutoWhiteBalance";
                case ShoostPostProcessEffect.ChangeFrameRate:
                    return "Hidden/lilToon/URP/Shoost/ChangeFrameRate";
                case ShoostPostProcessEffect.ColorGradingCustom:
                    return "Hidden/lilToon/URP/Shoost/ColorGradingCustom";
                case ShoostPostProcessEffect.CRTEffects:
                    return "Hidden/lilToon/URP/Shoost/CRTEffects";
                case ShoostPostProcessEffect.Distortion:
                    return "Hidden/lilToon/URP/Shoost/Distortion";
                case ShoostPostProcessEffect.DitheringCustom:
                    return "Hidden/lilToon/URP/Shoost/DitheringCustom";
                case ShoostPostProcessEffect.DownScaleResolution:
                    return "Hidden/lilToon/URP/Shoost/DownScaleResolution";
                case ShoostPostProcessEffect.FilmBreathGateWeave:
                    return "Hidden/lilToon/URP/Shoost/FilmBreathGateWeave";
                case ShoostPostProcessEffect.Fisheye:
                    return "Hidden/lilToon/URP/Shoost/Fisheye";
                case ShoostPostProcessEffect.GateWeave:
                    return "Hidden/lilToon/URP/Shoost/GateWeave";
                case ShoostPostProcessEffect.GrainCustom:
                    return "Hidden/lilToon/URP/Shoost/GrainCustom";
                case ShoostPostProcessEffect.IrisBlur:
                    return "Hidden/lilToon/URP/Shoost/IrisBlur";
                case ShoostPostProcessEffect.KawaseBlur:
                    return "Hidden/lilToon/URP/Shoost/KawaseBlur";
                case ShoostPostProcessEffect.LensDistortionCustom:
                    return "Hidden/lilToon/URP/Shoost/LensDistortionCustom";
                case ShoostPostProcessEffect.LevelAdjustment:
                    return "Hidden/lilToon/URP/Shoost/LevelAdjustment";
                case ShoostPostProcessEffect.LUTColorGrading:
                    return "Hidden/lilToon/URP/Shoost/LUTColorGrading";
                case ShoostPostProcessEffect.MotionTrail:
                    return "Hidden/lilToon/URP/Shoost/MotionTrail";
                case ShoostPostProcessEffect.Pixelize:
                    return "Hidden/lilToon/URP/Shoost/Pixelize";
                case ShoostPostProcessEffect.RGBBlur:
                    return "Hidden/lilToon/URP/Shoost/RGBBlur";
                case ShoostPostProcessEffect.RGBBlurV2:
                    return "Hidden/lilToon/URP/Shoost/RGBBlurV2";
                case ShoostPostProcessEffect.RGBChannelSeparator:
                    return "Hidden/lilToon/URP/Shoost/RGBChannelSeparator";
                case ShoostPostProcessEffect.RGBSplit:
                    return "Hidden/lilToon/URP/Shoost/RGBSplit";
                case ShoostPostProcessEffect.SharpenBefore:
                    return "Hidden/lilToon/URP/Shoost/SharpenBefore";
                case ShoostPostProcessEffect.SharpenAfter:
                    return "Hidden/lilToon/URP/Shoost/SharpenAfter";
                case ShoostPostProcessEffect.Tube:
                    return "Hidden/lilToon/URP/Shoost/Tube";
                case ShoostPostProcessEffect.VignetteCustom:
                    return "Hidden/lilToon/URP/Shoost/VignetteCustom";
                case ShoostPostProcessEffect.RetroLookProBleedCustom:
                    return "Hidden/lilToon/URP/Shoost/RetroLookProBleedCustom";
                case ShoostPostProcessEffect.RetroLookProNoise2Custom:
                    return "Hidden/lilToon/URP/Shoost/RetroLookProNoise2Custom";
                case ShoostPostProcessEffect.RetroLookProOldFilm2Custom:
                    return "Hidden/lilToon/URP/Shoost/RetroLookProOldFilm2Custom";
                case ShoostPostProcessEffect.RetroLookProTVEffectCustom:
                    return "Hidden/lilToon/URP/Shoost/RetroLookProTVEffectCustom";
                default:
                    return ShoostPostProcessShaderConstants.DefaultLayerShaderName;
            }
        }

        public static ShoostPostProcessInjectionPoint GetDefaultInjectionPoint(ShoostPostProcessEffect effect)
        {
            switch (effect)
            {
                case ShoostPostProcessEffect.CRTEffects:
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
