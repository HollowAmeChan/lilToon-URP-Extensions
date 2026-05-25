using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.AOV;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal sealed class HoMetadataBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-MetadataBuffer Output");
        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private static readonly List<ShaderTagId> AovShaderTagIds = new List<ShaderTagId>
        {
            HoAovShaderConstants.ShaderTagId
        };

        private static readonly List<ShaderTagId> SssShaderTagIds = new List<ShaderTagId>
        {
            HoAovShaderConstants.SssShaderTagId
        };

        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;

        private readonly RTHandle[] colorTargets = new RTHandle[HoMetadataBufferAttachmentLayout.ColorTargetCount];
        private HoMetadataBufferSettings settings;
        private HoMetadataBufferRenderTargets renderTargets;
        private Material clearMaterial;
        private Material fallbackMaterial;
        private FilteringSettings aovFilteringSettings;
        private FilteringSettings fallbackFilteringSettings;
        private bool fallbackFilteringEnabled;
        private RenderStateBlock renderStateBlock;

        private sealed class PassData
        {
            public RendererListHandle fallbackRendererList;
            public RendererListHandle aovRendererList;
            public bool drawFallback;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public TextureHandle objectCustom0Texture;
            public TextureHandle objectCustom1Texture;
            public TextureHandle sssTexture;
            public float systemChannelMask;
        }

        private sealed class ClearPassData
        {
            public Material clearMaterial;
        }

        private sealed class ResetPassData
        {
        }

        public HoMetadataBufferPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoMetadataBufferSettings settings,
            HoMetadataBufferRenderTargets renderTargets,
            Material clearMaterial,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.clearMaterial = clearMaterial;
            this.fallbackMaterial = fallbackMaterial;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public void SetupRenderGraph(
            HoMetadataBufferSettings settings,
            HoMetadataBufferRenderTargets renderTargets,
            Material clearMaterial,
            Material fallbackMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.clearMaterial = clearMaterial;
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
            colorTargets[HoMetadataBufferAttachmentLayout.MaskId] = renderTargets.MaskIdTexture;
            colorTargets[HoMetadataBufferAttachmentLayout.NormalDepth] = renderTargets.NormalDepthTexture;
            colorTargets[HoMetadataBufferAttachmentLayout.SurfaceData] = renderTargets.SurfaceDataTexture;
            colorTargets[HoMetadataBufferAttachmentLayout.Custom0] = renderTargets.Custom0Texture;
            colorTargets[HoMetadataBufferAttachmentLayout.ObjectCustom0] = renderTargets.ObjectCustom0Texture;
            colorTargets[HoMetadataBufferAttachmentLayout.ObjectCustom1] = renderTargets.ObjectCustom1Texture;

            ConfigureTarget(colorTargets, renderTargets.DepthTexture);
            ConfigureClear(ClearFlag.All, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            ApplyFallbackMaterialProperties();
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                ClearAovTargets(cmd);
                SetGlobalTextures(cmd);
                cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
                SetDefaultSubjectProperties(cmd);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled)
                {
                    DrawingSettings fallbackDrawingSettings = CreateDrawingSettings(FallbackShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
                    fallbackDrawingSettings.overrideMaterialPassIndex = 0;
                    context.DrawRenderers(renderingData.cullResults, ref fallbackDrawingSettings, ref fallbackFilteringSettings, ref renderStateBlock);
                }

                DrawingSettings aovDrawingSettings = CreateDrawingSettings(AovShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref aovDrawingSettings, ref aovFilteringSettings, ref renderStateBlock);

                cmd.SetRenderTarget(
                    renderTargets.SssTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    renderTargets.DepthTexture,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings sssDrawingSettings = CreateDrawingSettings(SssShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref sssDrawingSettings, ref aovFilteringSettings, ref renderStateBlock);
                cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null)
            {
                AddResetPass(renderGraph);
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();

            TextureHandle maskIdTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetMaskGraphicsFormat(), HoAovShaderConstants.MaskIdTextureName));
            TextureHandle normalDepthTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.NormalDepthTextureName));
            TextureHandle surfaceDataTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.SurfaceDataTextureName));
            TextureHandle custom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.Custom0TextureName));
            TextureHandle objectCustom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.ObjectCustom0TextureName));
            TextureHandle objectCustom1Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.ObjectCustom1TextureName));
            TextureHandle sssTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoAovShaderConstants.SssTextureName));
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoMetadataBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoAovShaderConstants.DepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            ApplyFallbackMaterialProperties();
            metadataResources.maskIdTexture = maskIdTexture;
            metadataResources.surfaceDataTexture = surfaceDataTexture;
            metadataResources.custom0Texture = custom0Texture;
            metadataResources.objectCustom0Texture = objectCustom0Texture;
            metadataResources.objectCustom1Texture = objectCustom1Texture;
            metadataResources.sssTexture = sssTexture;

            bool drawFallback = settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled;
            DrawingSettings fallbackDrawingSettings = RenderingUtils.CreateDrawingSettings(
                FallbackShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
            fallbackDrawingSettings.overrideMaterialPassIndex = 0;

            DrawingSettings aovDrawingSettings = RenderingUtils.CreateDrawingSettings(
                AovShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            RendererListParams fallbackRendererListParams = new RendererListParams(
                renderingData.cullResults,
                fallbackDrawingSettings,
                fallbackFilteringSettings);
            RendererListParams aovRendererListParams = new RendererListParams(
                renderingData.cullResults,
                aovDrawingSettings,
                aovFilteringSettings);
            DrawingSettings sssDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SssShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            RendererListParams sssRendererListParams = new RendererListParams(
                renderingData.cullResults,
                sssDrawingSettings,
                aovFilteringSettings);

            AddClearPass(
                renderGraph,
                maskIdTexture,
                normalDepthTexture,
                surfaceDataTexture,
                custom0Texture,
                objectCustom0Texture,
                objectCustom1Texture,
                depthTexture,
                clearMaterial);

            AddSssClearPass(renderGraph, sssTexture);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-MetadataBuffer Output", out PassData passData, ProfilingSampler))
            {
                passData.drawFallback = drawFallback;
                passData.fallbackRendererList = drawFallback ? renderGraph.CreateRendererList(fallbackRendererListParams) : default;
                passData.aovRendererList = renderGraph.CreateRendererList(aovRendererListParams);
                passData.maskIdTexture = maskIdTexture;
                passData.normalDepthTexture = normalDepthTexture;
                passData.surfaceDataTexture = surfaceDataTexture;
                passData.custom0Texture = custom0Texture;
                passData.objectCustom0Texture = objectCustom0Texture;
                passData.objectCustom1Texture = objectCustom1Texture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (drawFallback && passData.fallbackRendererList.IsValid())
                {
                    builder.UseRendererList(passData.fallbackRendererList);
                }

                if (passData.aovRendererList.IsValid())
                {
                    builder.UseRendererList(passData.aovRendererList);
                }

                builder.SetRenderAttachment(maskIdTexture, HoMetadataBufferAttachmentLayout.MaskId, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(normalDepthTexture, HoMetadataBufferAttachmentLayout.NormalDepth, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(surfaceDataTexture, HoMetadataBufferAttachmentLayout.SurfaceData, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(custom0Texture, HoMetadataBufferAttachmentLayout.Custom0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom0Texture, HoMetadataBufferAttachmentLayout.ObjectCustom0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom1Texture, HoMetadataBufferAttachmentLayout.ObjectCustom1, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(maskIdTexture, HoAovShaderConstants.MaskIdTextureId);
                builder.SetGlobalTextureAfterPass(surfaceDataTexture, HoAovShaderConstants.SurfaceDataTextureId);
                builder.SetGlobalTextureAfterPass(custom0Texture, HoAovShaderConstants.Custom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom0Texture, HoAovShaderConstants.ObjectCustom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom1Texture, HoAovShaderConstants.ObjectCustom1TextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.drawFallback && data.fallbackRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.fallbackRendererList);
                    }

                    if (data.aovRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.aovRendererList);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoAOV SSS", out PassData passData, ProfilingSampler))
            {
                passData.aovRendererList = renderGraph.CreateRendererList(sssRendererListParams);
                passData.sssTexture = sssTexture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (passData.aovRendererList.IsValid())
                {
                    builder.UseRendererList(passData.aovRendererList);
                }

                builder.SetRenderAttachment(sssTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(sssTexture, HoAovShaderConstants.SssTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.aovRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.aovRendererList);
                    }
                });
            }
        }

        private static void AddClearPass(
            RenderGraph renderGraph,
            TextureHandle maskIdTexture,
            TextureHandle normalDepthTexture,
            TextureHandle surfaceDataTexture,
            TextureHandle custom0Texture,
            TextureHandle objectCustom0Texture,
            TextureHandle objectCustom1Texture,
            TextureHandle depthTexture,
            Material clearMaterial)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ClearPassData>("lilToon-HoAOV Clear", out ClearPassData passData, ProfilingSampler))
            {
                passData.clearMaterial = clearMaterial;
                builder.SetRenderAttachment(maskIdTexture, HoMetadataBufferAttachmentLayout.MaskId, AccessFlags.WriteAll);
                builder.SetRenderAttachment(normalDepthTexture, HoMetadataBufferAttachmentLayout.NormalDepth, AccessFlags.WriteAll);
                builder.SetRenderAttachment(surfaceDataTexture, HoMetadataBufferAttachmentLayout.SurfaceData, AccessFlags.WriteAll);
                builder.SetRenderAttachment(custom0Texture, HoMetadataBufferAttachmentLayout.Custom0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom0Texture, HoMetadataBufferAttachmentLayout.ObjectCustom0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom1Texture, HoMetadataBufferAttachmentLayout.ObjectCustom1, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ClearPassData data, RasterGraphContext context) =>
                {
                    ClearAovTargets(context.cmd, data.clearMaterial);
                });
            }
        }

        private static void AddSssClearPass(RenderGraph renderGraph, TextureHandle sssTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("lilToon-HoAOV SSS Clear", out _, ProfilingSampler))
            {
                builder.SetRenderAttachment(sssTexture, 0, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                });
            }
        }

        private void ClearAovTargets(CommandBuffer cmd)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (clearMaterial != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, clearMaterial, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private static void ClearAovTargets(RasterCommandBuffer cmd, Material material)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (material != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private void SetInactive(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 0.0f);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("lilToon-HoAOV Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 0.0f);
                });
            }
        }

        private static TextureDesc CreateTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoMetadataBufferSettings settings,
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

        private static float GetSystemChannelMask(HoMetadataBufferSettings settings)
        {
            return settings != null ? (float)settings.systemChannels : (float)HoMetadataBufferChannelMask.Default;
        }

        private void SetGlobalTextures(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, renderTargets.MaskIdTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, renderTargets.ObjectCustom0Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, renderTargets.ObjectCustom1Texture.nameID);
            cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
        }

        private void ApplyFallbackMaterialProperties()
        {
            if (fallbackMaterial == null)
            {
                return;
            }

            fallbackMaterial.SetFloat(HoAovShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
        }

        private static void SetDefaultSubjectProperties(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoAovShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.SystemWriteMaskId, (float)HoMetadataBufferChannelMask.Default);
            cmd.SetGlobalFloat(HoAovShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.TransmittanceHintId, 0.0f);
            cmd.SetGlobalVector(HoAovShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoAovShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectCustomMaskId, 0.0f);
        }

        private static void SetDefaultSubjectProperties(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoAovShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.SystemWriteMaskId, (float)HoMetadataBufferChannelMask.Default);
            cmd.SetGlobalFloat(HoAovShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoAovShaderConstants.TransmittanceHintId, 0.0f);
            cmd.SetGlobalVector(HoAovShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoAovShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoAovShaderConstants.ObjectCustomMaskId, 0.0f);
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
            aovFilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            // The override fallback material cannot see the source material alpha/cutout data.
            // Keep it away from alpha-test and transparent queues; native HoAOV passes cover those.
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
