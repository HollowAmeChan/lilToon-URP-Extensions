using System.Collections.Generic;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal enum ImageProcessEffectExecutionKind
    {
        SinglePass,
        MultiPass,
        Stateful,
        Removed
    }

    internal readonly struct ImageProcessEffectDescriptor
    {
        private const string ShaderRoot = "Hidden/lilToon/URP/ImageProcess/";

        private static readonly Dictionary<ImageProcessEffect, ImageProcessEffectDescriptor> Catalog = CreateCatalog();

        public readonly ImageProcessEffect Effect;
        public readonly string DefaultShaderName;
        public readonly ImageProcessEffectExecutionKind ExecutionKind;
        public readonly ImageProcessResourceRequest[] ResourceRequests;

        private ImageProcessEffectDescriptor(
            ImageProcessEffect effect,
            string defaultShaderName,
            ImageProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            Effect = effect;
            DefaultShaderName = defaultShaderName;
            ExecutionKind = executionKind;
            ResourceRequests = resourceRequests ?? NoResourceRequests;
        }

        public bool IsRemoved => ExecutionKind == ImageProcessEffectExecutionKind.Removed;

        public static ImageProcessEffectDescriptor Get(ImageProcessEffect effect)
        {
            return Catalog.TryGetValue(effect, out ImageProcessEffectDescriptor descriptor)
                ? descriptor
                : Unknown(effect);
        }

        private static Dictionary<ImageProcessEffect, ImageProcessEffectDescriptor> CreateCatalog()
        {
            var catalog = new Dictionary<ImageProcessEffect, ImageProcessEffectDescriptor>(64);

            Add(catalog, SinglePass(ImageProcessEffect.CustomMaterial, ImageProcessShaderConstants.DefaultLayerShaderName));
            Add(catalog, SinglePass(ImageProcessEffect.AutoWhiteBalance));
            Add(catalog, Stateful(
                ImageProcessEffect.ChangeFrameRate,
                Request(ImageProcessResourceKind.History, "stores the held frame across refresh intervals")));
            Add(catalog, SinglePass(ImageProcessEffect.ColorGradingCustom));
            Add(catalog, SinglePass(ImageProcessEffect.CRTEffects));
            Add(catalog, SinglePass(ImageProcessEffect.Distortion));
            Add(catalog, SinglePass(ImageProcessEffect.DitheringCustom));
            Add(catalog, SinglePass(ImageProcessEffect.DownScaleResolution));
            Add(catalog, SinglePass(ImageProcessEffect.FilmBreathGateWeave));
            Add(catalog, SinglePass(ImageProcessEffect.Fisheye));
            Add(catalog, SinglePass(ImageProcessEffect.GateWeave));
            Add(catalog, SinglePass(ImageProcessEffect.GrainCustom));
            Add(catalog, MultiPass(
                ImageProcessEffect.IrisBlur,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative iris blur")));
            Add(catalog, Removed(ImageProcessEffect.RemovedEffectSlot13));
            Add(catalog, SinglePass(ImageProcessEffect.LensDistortionCustom));
            Add(catalog, SinglePass(ImageProcessEffect.LevelAdjustment));
            Add(catalog, SinglePass(
                ImageProcessEffect.MotionTrail,
                Request(ImageProcessResourceKind.History, "temporal image-domain trail")));
            Add(catalog, SinglePass(ImageProcessEffect.Pixelize));
            Add(catalog, SinglePass(ImageProcessEffect.RGBBlur));
            Add(catalog, MultiPass(
                ImageProcessEffect.RGBBlurV2,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled iterative RGB blur")));
            Add(catalog, SinglePass(ImageProcessEffect.RGBChannelSeparator));
            Add(catalog, SinglePass(ImageProcessEffect.RGBSplit));
            Add(catalog, SinglePass(ImageProcessEffect.SharpenBefore, "Hidden/lilToon/URP/ImageProcess/Sharpen"));
            Add(catalog, SinglePass(ImageProcessEffect.SharpenAfter, "Hidden/lilToon/URP/ImageProcess/Sharpen"));
            Add(catalog, SinglePass(ImageProcessEffect.Tube));
            Add(catalog, SinglePass(ImageProcessEffect.VignetteCustom));
            Add(catalog, SinglePass(ImageProcessEffect.RetroLookProBleedCustom));
            Add(catalog, SinglePass(ImageProcessEffect.RetroLookProNoise2Custom));
            Add(catalog, SinglePass(ImageProcessEffect.RetroLookProOldFilm2Custom));
            Add(catalog, SinglePass(ImageProcessEffect.RetroLookProTVEffectCustom));
            Add(catalog, Removed(ImageProcessEffect.RemovedEffectSlot30));
            Add(catalog, Removed(ImageProcessEffect.RemovedEffectSlot31));
            Add(catalog, Removed(ImageProcessEffect.RemovedEffectSlot32));
            Add(catalog, SinglePass(ImageProcessEffect.Gradient));
            Add(catalog, MultiPass(
                ImageProcessEffect.Glow,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled glow blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with glow texture")));
            Add(catalog, SinglePass(ImageProcessEffect.Lighting));
            Add(catalog, SinglePass(ImageProcessEffect.CenterColorCorrection));
            Add(catalog, SinglePass(ImageProcessEffect.LED));
            Add(catalog, SinglePass(
                ImageProcessEffect.Weather,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied weather texture")));
            Add(catalog, SinglePass(
                ImageProcessEffect.Particle,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied particle texture")));
            Add(catalog, SinglePass(ImageProcessEffect.CameraSwitcher));
            Add(catalog, SinglePass(ImageProcessEffect.TransparentBackground));
            Add(catalog, SinglePass(ImageProcessEffect.VHS));
            Add(catalog, SinglePass(ImageProcessEffect.CameraFlash));
            Add(catalog, SinglePass(ImageProcessEffect.ToonMap));
            Add(catalog, SinglePass(ImageProcessEffect.Kuwahara));
            Add(catalog, SinglePass(
                ImageProcessEffect.BokehZoomBlur,
                Request(ImageProcessResourceKind.OriginalSource, "radial blur composite samples the original image")));
            Add(catalog, MultiPass(
                ImageProcessEffect.ApertureBokeh,
                Request(ImageProcessResourceKind.LocalPingPong, "downscaled bokeh blur chain"),
                Request(ImageProcessResourceKind.OriginalSource, "final composite blends original image with bokeh texture")));
            Add(catalog, SinglePass(
                ImageProcessEffect.LensFlare,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied flare texture")));
            Add(catalog, SinglePass(ImageProcessEffect.CinematicBars));
            Add(catalog, SinglePass(ImageProcessEffect.GlitchArt));
            Add(catalog, SinglePass(ImageProcessEffect.PrismFracture));
            Add(catalog, SinglePass(ImageProcessEffect.SpeedLines));
            Add(catalog, SinglePass(
                ImageProcessEffect.SkyGodRays,
                Request(ImageProcessResourceKind.OriginalSource, "image-domain ray composite")));
            Add(catalog, SinglePass(
                ImageProcessEffect.LogoOverlay,
                Request(ImageProcessResourceKind.ExternalTexture, "layer-supplied logo texture")));
            Add(catalog, SinglePass(ImageProcessEffect.BlueNoise));

            return catalog;
        }

        private static void Add(
            Dictionary<ImageProcessEffect, ImageProcessEffectDescriptor> catalog,
            ImageProcessEffectDescriptor descriptor)
        {
            catalog.Add(descriptor.Effect, descriptor);
        }

        private static ImageProcessEffectDescriptor SinglePass(
            ImageProcessEffect effect,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return SinglePass(effect, ShaderName(effect), resourceRequests);
        }

        private static ImageProcessEffectDescriptor SinglePass(
            ImageProcessEffect effect,
            string defaultShaderName,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, defaultShaderName, ImageProcessEffectExecutionKind.SinglePass, resourceRequests);
        }

        private static ImageProcessEffectDescriptor MultiPass(
            ImageProcessEffect effect,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), ImageProcessEffectExecutionKind.MultiPass, resourceRequests);
        }

        private static ImageProcessEffectDescriptor Stateful(
            ImageProcessEffect effect,
            params ImageProcessResourceRequest[] resourceRequests)
        {
            return Create(effect, ShaderName(effect), ImageProcessEffectExecutionKind.Stateful, resourceRequests);
        }

        private static ImageProcessEffectDescriptor Removed(ImageProcessEffect effect)
        {
            return Create(
                effect,
                ImageProcessShaderConstants.DefaultLayerShaderName,
                ImageProcessEffectExecutionKind.Removed,
                NoResourceRequests);
        }

        private static ImageProcessEffectDescriptor Unknown(ImageProcessEffect effect)
        {
            return Create(
                effect,
                ImageProcessShaderConstants.DefaultLayerShaderName,
                ImageProcessEffectExecutionKind.SinglePass,
                NoResourceRequests);
        }

        private static ImageProcessEffectDescriptor Create(
            ImageProcessEffect effect,
            string defaultShaderName,
            ImageProcessEffectExecutionKind executionKind,
            ImageProcessResourceRequest[] resourceRequests)
        {
            return new ImageProcessEffectDescriptor(
                effect,
                defaultShaderName,
                executionKind,
                resourceRequests);
        }

        private static string ShaderName(ImageProcessEffect effect)
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
