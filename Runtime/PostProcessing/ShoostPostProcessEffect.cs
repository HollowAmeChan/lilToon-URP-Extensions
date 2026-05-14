namespace lilToon.URP.Extensions.PostProcessing
{
    public enum ShoostPostProcessEffect
    {
        CustomMaterial = 0,
        AutoWhiteBalance,
        ChangeFrameRate,
        ColorGradingCustom,
        CRTEffects,
        Distortion,
        DitheringCustom,
        DownScaleResolution,
        FilmBreathGateWeave,
        Fisheye,
        GateWeave,
        GrainCustom,
        IrisBlur,
        KawaseBlur,
        LensDistortionCustom,
        LevelAdjustment,
        LUTColorGrading,
        MotionTrail,
        Pixelize,
        RGBBlur,
        RGBBlurV2,
        RGBChannelSeparator,
        RGBSplit,
        SharpenBefore,
        SharpenAfter,
        Tube,
        VignetteCustom,
        RetroLookProBleedCustom,
        RetroLookProNoise2Custom,
        RetroLookProOldFilm2Custom,
        RetroLookProTVEffectCustom
    }

    public enum ShoostPostProcessBlendMode
    {
        Normal = 0,
        Add = 1,
        Multiply = 2,
        Screen = 3
    }

    public enum ShoostPostProcessInjectionPoint
    {
        EffectDefault = 0,
        BeforeURPPostProcessing = 1,
        AfterURPPostProcessing = 2,
        AfterRendering = 3
    }
}
