#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferSkyPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GeometryBuffer Sky");

        private HoGeometryBufferSettings settings;
        private HoGeometryBufferRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private Material skyCaptureMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle normalDepthTexture;
            public Material skyCaptureMaterial;
        }

        public void Setup(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material skyCaptureMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.skyCaptureMaterial = skyCaptureMaterial;
            renderPassEvent = settings != null ? settings.skyPassEvent : RenderPassEvent.AfterRenderingSkybox;
            ConfigureInput(ScriptableRenderPassInput.Color);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material skyCaptureMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.skyCaptureMaterial = skyCaptureMaterial;
            renderPassEvent = settings != null ? settings.skyPassEvent : RenderPassEvent.AfterRenderingSkybox;
            ConfigureInput(ScriptableRenderPassInput.Color);
            requiresIntermediateTexture = true;
        }

        public void ReleaseCompatibilityResources()
        {
            cameraColorTarget = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!ShouldRender())
            {
                return;
            }

            renderTargets.ReAllocateSkyIfNeeded(renderingData.cameraData.cameraTargetDescriptor, settings);
            ConfigureTarget(renderTargets.SkyTexture);
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!ShouldRender() ||
                cameraColorTarget == null ||
                renderTargets.NormalDepthTexture == null ||
                renderTargets.SkyTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.SkyTexture, skyCaptureMaterial, 0);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, renderTargets.SkyTexture.nameID);
                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 1.0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (!ShouldRender())
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || !geometryResources.normalDepthTexture.IsValid())
            {
                return;
            }

            TextureHandle skyTexture = renderGraph.CreateTexture(CreateSkyTextureDesc(
                cameraData.cameraTargetDescriptor,
                settings,
                HoGeometryBufferShaderConstants.SkyTextureName));
            geometryResources.skyTexture = skyTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-GeometryBuffer Sky", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.skyCaptureMaterial = skyCaptureMaterial;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(skyTexture, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(skyTexture, HoGeometryBufferShaderConstants.SkyTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.skyCaptureMaterial, 0);
                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 1.0f);
                });
            }
        }

        private bool ShouldRender()
        {
            return settings != null &&
                settings.enableSkyBuffer &&
                renderTargets != null &&
                skyCaptureMaterial != null;
        }

        private static TextureDesc CreateSkyTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoGeometryBufferSettings settings,
            string name)
        {
            int divisor = Mathf.Max(1, (int)settings.skyRenderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = GetSkyGraphicsFormat(cameraTextureDescriptor.graphicsFormat);
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        private static GraphicsFormat GetSkyGraphicsFormat(GraphicsFormat fallback)
        {
            GraphicsFormat highPrecision = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            return highPrecision != GraphicsFormat.None ? highPrecision : fallback;
        }
    }
}
