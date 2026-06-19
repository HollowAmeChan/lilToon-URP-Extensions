using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.TransparentPass
{
    internal sealed class HoTransparentPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-Transparent");

        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>(1);
        private readonly List<HoTransparentRendererListRequest> rendererListRequests = new List<HoTransparentRendererListRequest>(4);
        private HoTransparentSettings settings;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;
        private RTHandle cameraColorTarget;
        private RTHandle cameraDepthTarget;

        private sealed class PassData
        {
            public RendererListHandle[] rendererLists;
            public bool publishActiveFlag;
        }

        public HoTransparentPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            profilingSampler = ProfilingSampler;
        }

        public void Setup(HoTransparentSettings settings, RTHandle cameraColorTarget, RTHandle cameraDepthTarget)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.cameraDepthTarget = cameraDepthTarget;
            ConfigureFiltering();
            renderPassEvent = settings != null ? settings.drawPassEvent : RenderPassEvent.BeforeRenderingTransparents;
        }

        public void SetupRenderGraph(HoTransparentSettings settings)
        {
            this.settings = settings;
            ConfigureFiltering();
            renderPassEvent = settings != null ? settings.drawPassEvent : RenderPassEvent.BeforeRenderingTransparents;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (cameraColorTarget != null && cameraDepthTarget != null)
            {
                ConfigureTarget(cameraColorTarget, cameraDepthTarget);
            }
            else if (cameraColorTarget != null)
            {
                ConfigureTarget(cameraColorTarget);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || !settings.HasActivePasses)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                if (settings.publishActiveFlag)
                {
                    cmd.SetGlobalFloat(HoTransparentShaderConstants.ActiveId, 1.0f);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                BuildCompatibilityRendererListRequests(ref renderingData);

                for (int i = 0; i < rendererListRequests.Count; i++)
                {
                    HoTransparentRendererListRequest request = rendererListRequests[i];
                    DrawingSettings drawingSettings = request.DrawingSettings;
                    context.DrawRenderers(
                        renderingData.cullResults,
                        ref drawingSettings,
                        ref filteringSettings,
                        ref renderStateBlock);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || !settings.HasActivePasses)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle colorTarget = resourceData.activeColorTexture;
            if (!colorTarget.IsValid())
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            BuildRendererListRequests(renderingData, cameraData, lightData);

            if (rendererListRequests.Count == 0)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-Transparent", out PassData passData, ProfilingSampler))
            {
                builder.UseAllGlobalTextures(true);

                passData.publishActiveFlag = settings.publishActiveFlag;
                passData.rendererLists = new RendererListHandle[rendererListRequests.Count];

                for (int i = 0; i < rendererListRequests.Count; i++)
                {
                    RendererListParams rendererListParams = new RendererListParams(
                        renderingData.cullResults,
                        rendererListRequests[i].DrawingSettings,
                        filteringSettings);
                    passData.rendererLists[i] = renderGraph.CreateRendererList(rendererListParams);
                    if (passData.rendererLists[i].IsValid())
                    {
                        builder.UseRendererList(passData.rendererLists[i]);
                    }
                }

                builder.SetRenderAttachment(colorTarget, 0, AccessFlags.Write);
                if (resourceData.activeDepthTexture.IsValid())
                {
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                }

                TextureHandle mainShadowsTexture = resourceData.mainShadowsTexture;
                if (mainShadowsTexture.IsValid())
                {
                    builder.UseTexture(mainShadowsTexture, AccessFlags.Read);
                }

                TextureHandle additionalShadowsTexture = resourceData.additionalShadowsTexture;
                if (additionalShadowsTexture.IsValid())
                {
                    builder.UseTexture(additionalShadowsTexture, AccessFlags.Read);
                }

                TextureHandle ssaoTexture = resourceData.ssaoTexture;
                if (ssaoTexture.IsValid())
                {
                    builder.UseTexture(ssaoTexture, AccessFlags.Read);
                }

                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    if (data.publishActiveFlag)
                    {
                        context.cmd.SetGlobalFloat(HoTransparentShaderConstants.ActiveId, 1.0f);
                    }

                    for (int i = 0; i < data.rendererLists.Length; i++)
                    {
                        if (data.rendererLists[i].IsValid())
                        {
                            context.cmd.DrawRendererList(data.rendererLists[i]);
                        }
                    }
                });
            }
        }

        private void ConfigureFiltering()
        {
            filteringSettings = settings != null
                ? new FilteringSettings(settings.RenderQueueRange, settings.layerMask)
                : new FilteringSettings(RenderQueueRange.transparent, -1);
        }

        private void BuildRendererListRequests(
            UniversalRenderingData renderingData,
            UniversalCameraData cameraData,
            UniversalLightData lightData)
        {
            rendererListRequests.Clear();
            HoTransparentPassDescriptor[] passes = settings != null ? settings.passes : null;
            if (passes == null)
            {
                return;
            }

            for (int i = 0; i < passes.Length; i++)
            {
                HoTransparentPassDescriptor pass = passes[i];
                if (pass == null || !pass.IsValid)
                {
                    continue;
                }

                shaderTagIds.Clear();
                shaderTagIds.Add(new ShaderTagId(pass.lightMode));
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    shaderTagIds,
                    renderingData,
                    cameraData,
                    lightData,
                    SortingCriteria.CommonTransparent);
                rendererListRequests.Add(new HoTransparentRendererListRequest(drawingSettings));
            }
        }

        private void BuildCompatibilityRendererListRequests(ref RenderingData renderingData)
        {
            rendererListRequests.Clear();
            HoTransparentPassDescriptor[] passes = settings != null ? settings.passes : null;
            if (passes == null)
            {
                return;
            }

            for (int i = 0; i < passes.Length; i++)
            {
                HoTransparentPassDescriptor pass = passes[i];
                if (pass == null || !pass.IsValid)
                {
                    continue;
                }

                shaderTagIds.Clear();
                shaderTagIds.Add(new ShaderTagId(pass.lightMode));
                DrawingSettings drawingSettings = CreateDrawingSettings(
                    shaderTagIds,
                    ref renderingData,
                    SortingCriteria.CommonTransparent);
                rendererListRequests.Add(new HoTransparentRendererListRequest(drawingSettings));
            }
        }

        private readonly struct HoTransparentRendererListRequest
        {
            public readonly DrawingSettings DrawingSettings;

            public HoTransparentRendererListRequest(DrawingSettings drawingSettings)
            {
                DrawingSettings = drawingSettings;
            }
        }
    }
}
