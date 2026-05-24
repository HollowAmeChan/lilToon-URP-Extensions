// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.AOV;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    internal sealed class HoSubsurfaceScatteringDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoSSS Debug");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public TextureHandle cameraColor;
            public TextureHandle sssTexture;
            public TextureHandle transmissionTexture;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public Material material;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
            public Vector4 transmissionParams;
            public Vector4 transmissionColor;
            public Vector4 transmissionShapeParams;
            public Vector4 debugParams;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetDebugRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetDebugRenderPassEvent();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.SourceTexture == null || renderTargets.TransmissionTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, renderTargets.TransmissionTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.CompositeSourceTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, renderTargets.CompositeSourceTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoAovRenderGraphResources aovResources = frameData.GetOrCreate<HoAovRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            TextureHandle sssTexture = sssResources.sourceTexture;
            TextureHandle transmissionTexture = sssResources.transmissionTexture;
            if (!cameraColor.IsValid() || !sssTexture.IsValid() || !transmissionTexture.IsValid() || !aovResources.HasRequiredTextures)
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(cameraColor);
            destinationDesc.name = "_lilHoSSSDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoSSS Debug", out PassData passData, ProfilingSampler))
            {
                passData.cameraColor = cameraColor;
                passData.sssTexture = sssTexture;
                passData.transmissionTexture = transmissionTexture;
                passData.maskIdTexture = aovResources.maskIdTexture;
                passData.normalDepthTexture = aovResources.normalDepthTexture;
                passData.surfaceDataTexture = aovResources.surfaceDataTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings);
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;
                passData.transmissionParams = CreateTransmissionParams(settings);
                passData.transmissionColor = settings.transmissionColor;
                passData.transmissionShapeParams = CreateTransmissionShapeParams(settings);
                passData.debugParams = CreateDebugParams(settings);

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(sssTexture, AccessFlags.Read);
                builder.UseTexture(transmissionTexture, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, data.color);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, data.transmissionParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, data.transmissionColor);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, data.transmissionShapeParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.DebugParamsId, data.debugParams);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.sssTexture);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, data.transmissionTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    Blitter.BlitTexture(context.cmd, data.cameraColor, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, settings.color);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, CreateTransmissionParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, settings.transmissionColor);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, CreateTransmissionShapeParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.DebugParamsId, CreateDebugParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0f, settings.strength), PackRadius(settings.radius, 24.0f), Mathf.Clamp((int)settings.quality, 1.0f, 32.0f), 0.0f);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateTransmissionParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.transmissionStrength),
                PackRadius(settings.transmissionRadius, 24.0f),
                Mathf.Clamp(settings.transmissionSamples, 2, 32),
                Mathf.Clamp01(settings.transmissionMainLightDirection));
        }

        private static Vector4 CreateTransmissionShapeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.transmissionDepthWeight),
                Mathf.Max(0.0f, settings.transmissionEdgeBoost),
                Mathf.Clamp01(settings.transmissionRimWeight),
                Mathf.Clamp01(settings.transmissionSmoothing));
        }

        private static Vector4 CreateDebugParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4((float)settings.debugMode, 0.0f, 0.0f, 0.0f);
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }
    }
}
