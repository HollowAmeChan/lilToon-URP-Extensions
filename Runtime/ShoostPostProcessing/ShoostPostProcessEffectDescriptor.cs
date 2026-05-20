using System.Collections.Generic;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal enum ShoostPostProcessEffectExecutionKind
    {
        SinglePass,
        MultiPass,
        Stateful,
        Removed
    }

    internal readonly struct ShoostPostProcessEffectDescriptor
    {
        private const int DefaultRuntimeOrder = int.MaxValue;
        private const string ShaderRoot = "Hidden/lilToon-Shoost/URP/Shoost/";

        private static readonly Dictionary<ShoostPostProcessEffect, ShoostPostProcessEffectDescriptor> Catalog = CreateCatalog();

        public readonly ShoostPostProcessEffect Effect;
        public readonly string DefaultShaderName;
        public readonly int RuntimeOrder;
        public readonly bool SupportsAovComposite;
        public readonly ShoostPostProcessEffectExecutionKind ExecutionKind;

        private ShoostPostProcessEffectDescriptor(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            bool supportsAovComposite,
            ShoostPostProcessEffectExecutionKind executionKind)
        {
            Effect = effect;
            DefaultShaderName = defaultShaderName;
            RuntimeOrder = runtimeOrder;
            SupportsAovComposite = supportsAovComposite;
            ExecutionKind = executionKind;
        }

        public bool IsRemoved => ExecutionKind == ShoostPostProcessEffectExecutionKind.Removed;

        public static ShoostPostProcessEffectDescriptor Get(ShoostPostProcessEffect effect)
        {
            return Catalog.TryGetValue(effect, out ShoostPostProcessEffectDescriptor descriptor)
                ? descriptor
                : Unknown(effect);
        }

        private static Dictionary<ShoostPostProcessEffect, ShoostPostProcessEffectDescriptor> CreateCatalog()
        {
            var catalog = new Dictionary<ShoostPostProcessEffect, ShoostPostProcessEffectDescriptor>(64);

            Add(catalog, SinglePass(ShoostPostProcessEffect.CustomMaterial, ShoostPostProcessShaderConstants.DefaultLayerShaderName, 42, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.AutoWhiteBalance, 1, false));
            Add(catalog, Stateful(ShoostPostProcessEffect.ChangeFrameRate, 38, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ColorGradingCustom, 4, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CRTEffects, 24, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Distortion, 39, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DitheringCustom, 25, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DownScaleResolution, DefaultRuntimeOrder, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.FilmBreathGateWeave, 21, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Fisheye, 40, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GateWeave, 43, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GrainCustom, 35, false));
            Add(catalog, MultiPass(ShoostPostProcessEffect.IrisBlur, 26, false));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot13));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LensDistortionCustom, 44, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LevelAdjustment, 3, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.MotionTrail, 45, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Pixelize, 37, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBBlur, 46, false));
            Add(catalog, MultiPass(ShoostPostProcessEffect.RGBBlurV2, 27, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBChannelSeparator, 29, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBSplit, 28, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenBefore, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen", 0, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenAfter, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen", 47, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Tube, 22, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VignetteCustom, 36, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProBleedCustom, 48, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProNoise2Custom, 49, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProOldFilm2Custom, 50, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProTVEffectCustom, 51, false));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot30));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot31));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot32));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Gradient, 7, false));
            Add(catalog, MultiPass(ShoostPostProcessEffect.Glow, 33, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Lighting, 8, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CenterColorCorrection, 9, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LED, 11, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Weather, 12, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Particle, 13, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraSwitcher, 19, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.TransparentBackground, 20, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VHS, 23, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraFlash, 41, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ToonMap, 34, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Kuwahara, 10, true));
            Add(catalog, SinglePass(ShoostPostProcessEffect.BokehZoomBlur, 30, false));
            Add(catalog, MultiPass(ShoostPostProcessEffect.ApertureBokeh, 31, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LensFlare, 32, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CinematicBars, 10000, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GlitchArt, 14, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.PrismFracture, 15, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SpeedLines, 16, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SkyGodRays, 17, false));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LogoOverlay, 2, false));

            return catalog;
        }

        private static void Add(
            Dictionary<ShoostPostProcessEffect, ShoostPostProcessEffectDescriptor> catalog,
            ShoostPostProcessEffectDescriptor descriptor)
        {
            catalog.Add(descriptor.Effect, descriptor);
        }

        private static ShoostPostProcessEffectDescriptor SinglePass(
            ShoostPostProcessEffect effect,
            int runtimeOrder,
            bool supportsAovComposite)
        {
            return SinglePass(effect, ShaderName(effect), runtimeOrder, supportsAovComposite);
        }

        private static ShoostPostProcessEffectDescriptor SinglePass(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            bool supportsAovComposite)
        {
            return Create(effect, defaultShaderName, runtimeOrder, supportsAovComposite, ShoostPostProcessEffectExecutionKind.SinglePass);
        }

        private static ShoostPostProcessEffectDescriptor MultiPass(
            ShoostPostProcessEffect effect,
            int runtimeOrder,
            bool supportsAovComposite)
        {
            return Create(effect, ShaderName(effect), runtimeOrder, supportsAovComposite, ShoostPostProcessEffectExecutionKind.MultiPass);
        }

        private static ShoostPostProcessEffectDescriptor Stateful(
            ShoostPostProcessEffect effect,
            int runtimeOrder,
            bool supportsAovComposite)
        {
            return Create(effect, ShaderName(effect), runtimeOrder, supportsAovComposite, ShoostPostProcessEffectExecutionKind.Stateful);
        }

        private static ShoostPostProcessEffectDescriptor Removed(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                DefaultRuntimeOrder,
                false,
                ShoostPostProcessEffectExecutionKind.Removed);
        }

        private static ShoostPostProcessEffectDescriptor Unknown(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                DefaultRuntimeOrder,
                true,
                ShoostPostProcessEffectExecutionKind.SinglePass);
        }

        private static ShoostPostProcessEffectDescriptor Create(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            bool supportsAovComposite,
            ShoostPostProcessEffectExecutionKind executionKind)
        {
            return new ShoostPostProcessEffectDescriptor(
                effect,
                defaultShaderName,
                runtimeOrder,
                supportsAovComposite,
                executionKind);
        }

        private static string ShaderName(ShoostPostProcessEffect effect)
        {
            return $"{ShaderRoot}{effect}";
        }
    }
}
