#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GeometryBuffer Output");
        private static readonly List<ShaderTagId> GeometryShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId(HoGeometryBufferShaderConstants.ShaderPassName)
        };

        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;

        private HoGeometryBufferSettings settings;
        private HoGeometryBufferRenderTargets renderTargets;
        private Material fallbackMaterial;
        private FilteringSettings geometryFilteringSettings;
        private FilteringSettings fallbackFilteringSettings;
        private bool fallbackFilteringEnabled;
        private RenderStateBlock renderStateBlock;

        private sealed class PassData
        {
            public RendererListHandle geometryRendererList;
            public RendererListHandle fallbackRendererList;
            public bool drawFallback;
        }

        public HoGeometryBufferPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings);
            ConfigureTarget(renderTargets.NormalDepthTexture, renderTargets.DepthTexture);
            ConfigureClear(ClearFlag.All, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetRenderTarget(
                    renderTargets.NormalDepthTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    renderTargets.DepthTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (fallbackMaterial != null && fallbackFilteringEnabled)
                {
                    DrawingSettings fallbackDrawingSettings = CreateDrawingSettings(FallbackShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
                    fallbackDrawingSettings.overrideMaterialPassIndex = 0;
                    context.DrawRenderers(renderingData.cullResults, ref fallbackDrawingSettings, ref fallbackFilteringSettings, ref renderStateBlock);
                }

                DrawingSettings geometryDrawingSettings = CreateDrawingSettings(GeometryShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref geometryDrawingSettings, ref geometryFilteringSettings, ref renderStateBlock);

                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, renderTargets.DepthTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();

            TextureHandle normalDepthTexture = renderGraph.CreateTexture(CreateTextureDesc(
                cameraData.cameraTargetDescriptor,
                settings,
                HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat(),
                "_lilHoAovNormalDepthTexture"));
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoGeometryBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                "_lilHoAovDepthTexture",
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            geometryResources.normalDepthTexture = normalDepthTexture;
            geometryResources.depthTexture = depthTexture;

            bool drawFallback = fallbackMaterial != null && fallbackFilteringEnabled;
            DrawingSettings geometryDrawingSettings = RenderingUtils.CreateDrawingSettings(
                GeometryShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            DrawingSettings fallbackDrawingSettings = RenderingUtils.CreateDrawingSettings(
                FallbackShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
            fallbackDrawingSettings.overrideMaterialPassIndex = 0;

            RendererListParams geometryRendererListParams = new RendererListParams(
                renderingData.cullResults,
                geometryDrawingSettings,
                geometryFilteringSettings);
            RendererListParams fallbackRendererListParams = new RendererListParams(
                renderingData.cullResults,
                fallbackDrawingSettings,
                fallbackFilteringSettings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-GeometryBuffer Output", out PassData passData, ProfilingSampler))
            {
                passData.geometryRendererList = renderGraph.CreateRendererList(geometryRendererListParams);
                passData.drawFallback = drawFallback;
                passData.fallbackRendererList = drawFallback ? renderGraph.CreateRendererList(fallbackRendererListParams) : default;

                if (passData.geometryRendererList.IsValid())
                {
                    builder.UseRendererList(passData.geometryRendererList);
                }

                if (drawFallback && passData.fallbackRendererList.IsValid())
                {
                    builder.UseRendererList(passData.fallbackRendererList);
                }

                builder.SetRenderAttachment(normalDepthTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(normalDepthTexture, HoGeometryBufferShaderConstants.NormalDepthTextureId);
                builder.SetGlobalTextureAfterPass(depthTexture, HoGeometryBufferShaderConstants.DepthTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                    if (data.drawFallback && data.fallbackRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.fallbackRendererList);
                    }

                    if (data.geometryRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.geometryRendererList);
                    }
                });
            }
        }

        private static TextureDesc CreateTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoGeometryBufferSettings settings,
            GraphicsFormat format,
            string name)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = format != GraphicsFormat.None ? format : cameraTextureDescriptor.graphicsFormat;
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = divisor == 1
                ? (MSAASamples)cameraTextureDescriptor.msaaSamples
                : MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = cameraTextureDescriptor.bindMS && divisor == 1;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        private void ConfigureFiltering()
        {
            int minQueue = settings != null ? settings.minRenderQueue : 0;
            int maxQueue = settings != null ? settings.maxRenderQueue : (int)RenderQueue.Overlay - 1;
            if (maxQueue < minQueue)
            {
                maxQueue = minQueue;
            }

            RenderQueueRange renderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = maxQueue
            };
            int layerMask = settings != null ? settings.layerMask.value : -1;
            geometryFilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            int fallbackMaxQueue = Mathf.Min(maxQueue, FallbackMaxRenderQueue);
            fallbackFilteringEnabled = fallbackMaxQueue >= minQueue;
            RenderQueueRange fallbackRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = fallbackFilteringEnabled ? fallbackMaxQueue : minQueue
            };
            fallbackFilteringSettings = new FilteringSettings(fallbackRenderQueueRange, fallbackFilteringEnabled ? layerMask : 0);
        }
    }
}
