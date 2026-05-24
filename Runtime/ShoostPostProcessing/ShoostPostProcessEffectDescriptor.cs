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
        public readonly ShoostPostProcessEffectExecutionKind ExecutionKind;
        public readonly ImageProcessResourceRequest[] ResourceRequests;

        private ShoostPostProcessEffectDescriptor(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            ShoostPostProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            Effect = effect;
            DefaultShaderName = defaultShaderName;
            RuntimeOrder = runtimeOrder;
            ExecutionKind = executionKind;
            ResourceRequests = resourceRequests ?? NoResourceRequests;
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

            Add(catalog, SinglePass(ShoostPostProcessEffect.CustomMaterial, ShoostPostProcessShaderConstants.DefaultLayerShaderName, 42));
            Add(catalog, SinglePass(ShoostPostProcessEffect.AutoWhiteBalance, 1));
            Add(catalog, Stateful(
                ShoostPostProcessEffect.ChangeFrameRate,
                38,
                Request(ImageProcessResourceKind.History, "stores the held frame across refresh intervals")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ColorGradingCustom, 4));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CRTEffects, 24));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Distortion, 39));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DitheringCustom, 25));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DownScaleResolution, DefaultRuntimeOrder));
            Add(catalog, SinglePass(ShoostPostProcessEffect.FilmBreathGateWeave, 21));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Fisheye, 40));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GateWeave, 43));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GrainCustom, 35));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.IrisBlur,
                26,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative iris blur")));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot13));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LensDistortionCustom, 44));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LevelAdjustment, 3));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.MotionTrail,
                45,
                Request(ImageProcessResourceKind.History, "temporal image-domain trail")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Pixelize, 37));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBBlur, 46));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.RGBBlurV2,
                27,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative RGB blur")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBChannelSeparator, 29));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBSplit, 28));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenBefore, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen", 0));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenAfter, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen", 47));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Tube, 22));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VignetteCustom, 36));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProBleedCustom, 48));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProNoise2Custom, 49));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProOldFilm2Custom, 50));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProTVEffectCustom, 51));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot30));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot31));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot32));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Gradient, 7));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.Glow,
                33,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled glow blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with glow texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Lighting, 8));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CenterColorCorrection, 9));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LED, 11));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.Weather,
                12,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied weather texture")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.Particle,
                13,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied particle texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraSwitcher, 19));
            Add(catalog, SinglePass(ShoostPostProcessEffect.TransparentBackground, 20));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VHS, 23));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraFlash, 41));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ToonMap, 34));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Kuwahara, 10));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.BokehZoomBlur,
                30,
                Request(ImageProcessResourceKind.OriginalSource, "radial blur composite samples the original image")));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.ApertureBokeh,
                31,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled bokeh blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with bokeh texture")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.LensFlare,
                32,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied flare texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CinematicBars, 10000));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GlitchArt, 14));
            Add(catalog, SinglePass(ShoostPostProcessEffect.PrismFracture, 15));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SpeedLines, 16));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.SkyGodRays,
                17,
                Request(ImageProcessResourceKind.OriginalSource, "image-domain ray composite")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.LogoOverlay,
                2,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied logo texture")));

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
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return SinglePass(effect, ShaderName(effect), runtimeOrder, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor SinglePass(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, defaultShaderName, runtimeOrder, ShoostPostProcessEffectExecutionKind.SinglePass, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor MultiPass(
            ShoostPostProcessEffect effect,
            int runtimeOrder,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), runtimeOrder, ShoostPostProcessEffectExecutionKind.MultiPass, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Stateful(
            ShoostPostProcessEffect effect,
            int runtimeOrder,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), runtimeOrder, ShoostPostProcessEffectExecutionKind.Stateful, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Removed(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                DefaultRuntimeOrder,
                ShoostPostProcessEffectExecutionKind.Removed,
                NoResourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Unknown(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                DefaultRuntimeOrder,
                ShoostPostProcessEffectExecutionKind.SinglePass,
                NoResourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Create(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            int runtimeOrder,
            ShoostPostProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            return new ShoostPostProcessEffectDescriptor(
                effect,
                defaultShaderName,
                runtimeOrder,
                executionKind,
                resourceRequests);
        }

        private static string ShaderName(ShoostPostProcessEffect effect)
        {
            return $"{ShaderRoot}{effect}";
        }

        private static readonly ImageProcessResourceRequest[] NoResourceRequests = new ImageProcessResourceRequest[0];

        private static ImageProcessResourceRequest Request(ImageProcessResourceKind kind, string reason)
        {
            return new ImageProcessResourceRequest(kind, reason);
        }
    }
}
