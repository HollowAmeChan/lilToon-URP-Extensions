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
        private const string ShaderRoot = "Hidden/lilToon-Shoost/URP/Shoost/";

        private static readonly Dictionary<ShoostPostProcessEffect, ShoostPostProcessEffectDescriptor> Catalog = CreateCatalog();

        public readonly ShoostPostProcessEffect Effect;
        public readonly string DefaultShaderName;
        public readonly ShoostPostProcessEffectExecutionKind ExecutionKind;
        public readonly ImageProcessResourceRequest[] ResourceRequests;

        private ShoostPostProcessEffectDescriptor(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            ShoostPostProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            Effect = effect;
            DefaultShaderName = defaultShaderName;
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

            Add(catalog, SinglePass(ShoostPostProcessEffect.CustomMaterial, ShoostPostProcessShaderConstants.DefaultLayerShaderName));
            Add(catalog, SinglePass(ShoostPostProcessEffect.AutoWhiteBalance));
            Add(catalog, Stateful(
                ShoostPostProcessEffect.ChangeFrameRate,
                Request(ImageProcessResourceKind.History, "stores the held frame across refresh intervals")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ColorGradingCustom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CRTEffects));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Distortion));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DitheringCustom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.DownScaleResolution));
            Add(catalog, SinglePass(ShoostPostProcessEffect.FilmBreathGateWeave));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Fisheye));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GateWeave));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GrainCustom));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.IrisBlur,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative iris blur")));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot13));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LensDistortionCustom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LevelAdjustment));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.MotionTrail,
                Request(ImageProcessResourceKind.History, "temporal image-domain trail")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Pixelize));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBBlur));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.RGBBlurV2,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative RGB blur")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBChannelSeparator));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RGBSplit));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenBefore, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen"));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SharpenAfter, "Hidden/lilToon-Shoost/URP/Shoost/Sharpen"));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Tube));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VignetteCustom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProBleedCustom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProNoise2Custom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProOldFilm2Custom));
            Add(catalog, SinglePass(ShoostPostProcessEffect.RetroLookProTVEffectCustom));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot30));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot31));
            Add(catalog, Removed(ShoostPostProcessEffect.RemovedEffectSlot32));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Gradient));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.Glow,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled glow blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with glow texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Lighting));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CenterColorCorrection));
            Add(catalog, SinglePass(ShoostPostProcessEffect.LED));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.Weather,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied weather texture")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.Particle,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied particle texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraSwitcher));
            Add(catalog, SinglePass(ShoostPostProcessEffect.TransparentBackground));
            Add(catalog, SinglePass(ShoostPostProcessEffect.VHS));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CameraFlash));
            Add(catalog, SinglePass(ShoostPostProcessEffect.ToonMap));
            Add(catalog, SinglePass(ShoostPostProcessEffect.Kuwahara));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.BokehZoomBlur,
                Request(ImageProcessResourceKind.OriginalSource, "radial blur composite samples the original image")));
            Add(catalog, MultiPass(
                ShoostPostProcessEffect.ApertureBokeh,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled bokeh blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with bokeh texture")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.LensFlare,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied flare texture")));
            Add(catalog, SinglePass(ShoostPostProcessEffect.CinematicBars));
            Add(catalog, SinglePass(ShoostPostProcessEffect.GlitchArt));
            Add(catalog, SinglePass(ShoostPostProcessEffect.PrismFracture));
            Add(catalog, SinglePass(ShoostPostProcessEffect.SpeedLines));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.SkyGodRays,
                Request(ImageProcessResourceKind.OriginalSource, "image-domain ray composite")));
            Add(catalog, SinglePass(
                ShoostPostProcessEffect.LogoOverlay,
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
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return SinglePass(effect, ShaderName(effect), resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor SinglePass(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, defaultShaderName, ShoostPostProcessEffectExecutionKind.SinglePass, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor MultiPass(
            ShoostPostProcessEffect effect,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), ShoostPostProcessEffectExecutionKind.MultiPass, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Stateful(
            ShoostPostProcessEffect effect,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), ShoostPostProcessEffectExecutionKind.Stateful, resourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Removed(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                ShoostPostProcessEffectExecutionKind.Removed,
                NoResourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Unknown(ShoostPostProcessEffect effect)
        {
            return Create(
                effect,
                ShoostPostProcessShaderConstants.DefaultLayerShaderName,
                ShoostPostProcessEffectExecutionKind.SinglePass,
                NoResourceRequests);
        }

        private static ShoostPostProcessEffectDescriptor Create(
            ShoostPostProcessEffect effect,
            string defaultShaderName,
            ShoostPostProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            return new ShoostPostProcessEffectDescriptor(
                effect,
                defaultShaderName,
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
