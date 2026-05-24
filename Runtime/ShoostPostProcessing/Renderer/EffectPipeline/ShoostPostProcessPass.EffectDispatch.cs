using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private delegate void CompatibilityLayerExecutor(
            ShoostPostProcessPass pass,
            CommandBuffer cmd,
            RenderTextureDescriptor cameraDescriptor,
            Camera camera,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer);

        private delegate TextureHandle RenderGraphLayerExecutor(
            ShoostPostProcessPass pass,
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex,
            ContextContainer frameData);

        private delegate void CompatibilitySinglePassExecutor(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer);

        private delegate TextureHandle RenderGraphSinglePassExecutor(
            ShoostPostProcessPass pass,
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex);

        private readonly struct EffectExecutor
        {
            public readonly CompatibilityLayerExecutor Execute;
            public readonly RenderGraphLayerExecutor Record;

            public EffectExecutor(CompatibilityLayerExecutor execute, RenderGraphLayerExecutor record)
            {
                Execute = execute;
                Record = record;
            }
        }

        private static readonly Dictionary<ShoostPostProcessEffect, EffectExecutor> EffectExecutors = CreateEffectExecutors();

        private void ExecuteEffectLayer(
            CommandBuffer cmd,
            RenderTextureDescriptor cameraDescriptor,
            Camera camera,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            if (EffectExecutors.TryGetValue(runtimeLayer.settings.effect, out EffectExecutor executor))
            {
                executor.Execute(this, cmd, cameraDescriptor, camera, source, destination, runtimeLayer);
                return;
            }

            ApplySinglePassLayer(cmd, source, destination, runtimeLayer);
        }

        private TextureHandle RecordEffectLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex,
            ContextContainer frameData)
        {
            if (EffectExecutors.TryGetValue(runtimeLayer.settings.effect, out EffectExecutor executor))
            {
                return executor.Record(this, renderGraph, source, destination, runtimeLayer, layerIndex, frameData);
            }

            return RecordSinglePassLayer(renderGraph, source, destination, runtimeLayer, layerIndex);
        }

        private static Dictionary<ShoostPostProcessEffect, EffectExecutor> CreateEffectExecutors()
        {
            var executors = new Dictionary<ShoostPostProcessEffect, EffectExecutor>();

            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CustomMaterial, ApplyCustomMaterialLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCustomMaterialLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.AutoWhiteBalance, ApplyAutoWhiteBalanceLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordAutoWhiteBalanceLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterEffect(
                executors,
                ShoostPostProcessEffect.ChangeFrameRate,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => pass.ApplyChangeFrameRateLayer(cmd, cameraDescriptor, camera, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) =>
                {
                    UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                    return pass.RecordChangeFrameRateLayer(renderGraph, source, destination, runtimeLayer, layerIndex, cameraData);
                });
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.ColorGradingCustom, ApplyColorGradingCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordColorGradingCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CRTEffects, ApplyCRTEffectsLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCRTEffectsLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Distortion, ApplyDistortionLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordDistortionLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.DitheringCustom, ApplyDitheringCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordDitheringCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.DownScaleResolution, ApplyDownScaleResolutionLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordDownScaleResolutionLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.FilmBreathGateWeave, ApplyFilmBreathGateWeaveLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordFilmBreathGateWeaveLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Fisheye, ApplyFisheyeLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordFisheyeLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.GateWeave, ApplyGateWeaveLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordGateWeaveLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.GrainCustom, ApplyGrainCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordGrainCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterEffect(
                executors,
                ShoostPostProcessEffect.IrisBlur,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => pass.ApplyIrisBlurLayer(cmd, cameraDescriptor, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) => pass.RecordIrisBlurLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.LensDistortionCustom, ApplyLensDistortionCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLensDistortionCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.LevelAdjustment, ApplyLevelAdjustmentLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLevelAdjustmentLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.MotionTrail, ApplyMotionTrailLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordMotionTrailLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Pixelize, ApplyPixelizeLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordPixelizeLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RGBBlur, ApplyRGBBlurLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRGBBlurLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterEffect(
                executors,
                ShoostPostProcessEffect.RGBBlurV2,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => pass.ApplyRGBBlurV2Layer(cmd, cameraDescriptor, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) => pass.RecordRGBBlurV2Layer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RGBChannelSeparator, ApplyRGBChannelSeparatorLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRGBChannelSeparatorLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RGBSplit, ApplyRGBSplitLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRGBSplitLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.SharpenBefore, ApplySharpenBeforeLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordSharpenBeforeLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.SharpenAfter, ApplySharpenAfterLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordSharpenAfterLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Tube, ApplyTubeLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordTubeLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.VignetteCustom, ApplyVignetteCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordVignetteCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RetroLookProBleedCustom, ApplyRetroLookProBleedCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRetroLookProBleedCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RetroLookProNoise2Custom, ApplyRetroLookProNoise2CustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRetroLookProNoise2CustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RetroLookProOldFilm2Custom, ApplyRetroLookProOldFilm2CustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRetroLookProOldFilm2CustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.RetroLookProTVEffectCustom, ApplyRetroLookProTVEffectCustomLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordRetroLookProTVEffectCustomLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Gradient, ApplyGradientLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordGradientLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterEffect(
                executors,
                ShoostPostProcessEffect.Glow,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => pass.ApplyGlowLayer(cmd, cameraDescriptor, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) => pass.RecordGlowLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Lighting, ApplyLightingLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLightingLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CenterColorCorrection, ApplyCenterColorCorrectionLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCenterColorCorrectionLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.LED, ApplyLEDLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLEDLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Weather, ApplyWeatherLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordWeatherLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Particle, ApplyParticleLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordParticleLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CameraSwitcher, ApplyCameraSwitcherLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCameraSwitcherLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.TransparentBackground, ApplyTransparentBackgroundLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordTransparentBackgroundLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.VHS, ApplyVHSLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordVHSLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CameraFlash, ApplyCameraFlashLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCameraFlashLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.ToonMap, ApplyToonMapLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordToonMapLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.Kuwahara, ApplyKuwaharaLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordKuwaharaLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.BokehZoomBlur, ApplyBokehZoomBlurLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordBokehZoomBlurLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterEffect(
                executors,
                ShoostPostProcessEffect.ApertureBokeh,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => pass.ApplyApertureBokehLayer(cmd, cameraDescriptor, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) => pass.RecordApertureBokehLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.LensFlare, ApplyLensFlareLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLensFlareLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.CinematicBars, ApplyCinematicBarsLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordCinematicBarsLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.GlitchArt, ApplyGlitchArtLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordGlitchArtLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.PrismFracture, ApplyPrismFractureLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordPrismFractureLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.SpeedLines, ApplySpeedLinesLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordSpeedLinesLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.SkyGodRays, ApplySkyGodRaysLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordSkyGodRaysLayer(renderGraph, source, destination, runtimeLayer, layerIndex));
            RegisterSinglePassEffect(executors, ShoostPostProcessEffect.LogoOverlay, ApplyLogoOverlayLayer, (pass, renderGraph, source, destination, runtimeLayer, layerIndex) => pass.RecordLogoOverlayLayer(renderGraph, source, destination, runtimeLayer, layerIndex));

            return executors;
        }

        private static void RegisterSinglePassEffect(
            Dictionary<ShoostPostProcessEffect, EffectExecutor> executors,
            ShoostPostProcessEffect effect,
            CompatibilitySinglePassExecutor execute,
            RenderGraphSinglePassExecutor record)
        {
            RegisterEffect(
                executors,
                effect,
                (pass, cmd, cameraDescriptor, camera, source, destination, runtimeLayer) => execute(cmd, source, destination, runtimeLayer),
                (pass, renderGraph, source, destination, runtimeLayer, layerIndex, frameData) => record(pass, renderGraph, source, destination, runtimeLayer, layerIndex));
        }

        private static void RegisterEffect(
            Dictionary<ShoostPostProcessEffect, EffectExecutor> executors,
            ShoostPostProcessEffect effect,
            CompatibilityLayerExecutor execute,
            RenderGraphLayerExecutor record)
        {
            executors.Add(effect, new EffectExecutor(execute, record));
        }

        private static void ApplySinglePassLayer(
            CommandBuffer cmd,
            RTHandle source,
            RTHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ApplyLayerProperties(runtimeLayer.settings, runtimeLayer.material);
            Blitter.BlitCameraTexture(cmd, source, destination, runtimeLayer.material, Mathf.Max(0, runtimeLayer.settings.passIndex));
        }

        private TextureHandle RecordSinglePassLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Layer {layerIndex}", out PassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.layer = runtimeLayer.settings;
                passData.material = runtimeLayer.material;
                passData.passIndex = Mathf.Max(0, runtimeLayer.settings.passIndex);

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }
    }
}
