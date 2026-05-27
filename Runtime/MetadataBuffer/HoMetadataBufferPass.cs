using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using Unity.Collections;
using lilToon.URP.Extensions.MetadataBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal sealed class HoMetadataBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-MetadataBuffer Output");
        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private static readonly List<ShaderTagId> MetadataShaderTagIds = new List<ShaderTagId>
        {
            HoMetadataBufferShaderConstants.ShaderTagId
        };

        private static readonly List<ShaderTagId> SurfaceColorShaderTagIds = new List<ShaderTagId>
        {
            HoMetadataBufferShaderConstants.SurfaceColorShaderTagId
        };

        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;
        private const int SurfaceColorOpaqueMaxRenderQueue = (int)RenderQueue.GeometryLast;

        private readonly RTHandle[] colorTargets = new RTHandle[HoMetadataBufferAttachmentLayout.ColorTargetCount];
        private HoMetadataBufferSettings settings;
        private HoMetadataBufferRenderTargets renderTargets;
        private Material clearMaterial;
        private Material fallbackMaterial;
        private FilteringSettings metadataFilteringSettings;
        private FilteringSettings fallbackFilteringSettings;
        private FilteringSettings surfaceColorOpaqueFilteringSettings;
        private FilteringSettings surfaceColorTransparentFilteringSettings;
        private bool fallbackFilteringEnabled;
        private bool surfaceColorOpaqueFilteringEnabled;
        private bool surfaceColorTransparentFilteringEnabled;
        private RenderStateBlock renderStateBlock;
        private RenderStateBlock mBufferDepthWriteStateBlock;

        private sealed class PassData
        {
            public RendererListHandle fallbackRendererList;
            public RendererListHandle metadataRendererList;
            public RendererListHandle surfaceColorOpaqueRendererList;
            public RendererListHandle surfaceColorTransparentRendererList;
            public bool drawFallback;
            public TextureHandle maskIdTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public TextureHandle objectCustom0Texture;
            public TextureHandle objectCustom1Texture;
            public TextureHandle surfaceColorTexture;
            public TextureHandle mBufferDepthTexture;
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
            mBufferDepthWriteStateBlock = new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(true, CompareFunction.LessEqual)
            };
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
                ClearMetadataTargets(cmd);
                SetGlobalTextures(cmd);
                cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
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

                DrawingSettings metadataDrawingSettings = CreateDrawingSettings(MetadataShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref metadataDrawingSettings, ref metadataFilteringSettings, ref renderStateBlock);

                cmd.SetRenderTarget(
                    renderTargets.SurfaceColorTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    renderTargets.MBufferDepthTexture,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (surfaceColorOpaqueFilteringEnabled)
                {
                    DrawingSettings surfaceColorOpaqueDrawingSettings = CreateDrawingSettings(SurfaceColorShaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
                    context.DrawRenderers(renderingData.cullResults, ref surfaceColorOpaqueDrawingSettings, ref surfaceColorOpaqueFilteringSettings, ref mBufferDepthWriteStateBlock);
                }

                if (surfaceColorTransparentFilteringEnabled)
                {
                    cmd.SetRenderTarget(
                        renderTargets.SurfaceColorTexture,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store,
                        renderTargets.MBufferDepthTexture,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    DrawingSettings surfaceColorTransparentDrawingSettings = CreateDrawingSettings(SurfaceColorShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    context.DrawRenderers(renderingData.cullResults, ref surfaceColorTransparentDrawingSettings, ref surfaceColorTransparentFilteringSettings, ref renderStateBlock);
                }

                cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, renderTargets.SurfaceColorTexture.nameID);
                cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MBufferDepthTextureId, renderTargets.MBufferDepthTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null)
            {
                AddResetPass(renderGraph);
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();

            TextureHandle maskIdTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetMaskGraphicsFormat(), HoMetadataBufferShaderConstants.MaskIdTextureName));
            TextureHandle surfaceDataTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoMetadataBufferShaderConstants.SurfaceDataTextureName));
            TextureHandle custom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoMetadataBufferShaderConstants.Custom0TextureName));
            TextureHandle objectCustom0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoMetadataBufferShaderConstants.ObjectCustom0TextureName));
            TextureHandle objectCustom1Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoMetadataBufferShaderConstants.ObjectCustom1TextureName));
            TextureHandle surfaceColorTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraData.cameraTargetDescriptor, settings, HoMetadataBufferFormatUtility.GetHighPrecisionGraphicsFormat(), HoMetadataBufferShaderConstants.SurfaceColorTextureName));
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoMetadataBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoMetadataBufferShaderConstants.DepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            TextureHandle mBufferDepthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoMetadataBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoMetadataBufferShaderConstants.MBufferDepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            ApplyFallbackMaterialProperties();
            metadataResources.maskIdTexture = maskIdTexture;
            metadataResources.surfaceDataTexture = surfaceDataTexture;
            metadataResources.custom0Texture = custom0Texture;
            metadataResources.objectCustom0Texture = objectCustom0Texture;
            metadataResources.objectCustom1Texture = objectCustom1Texture;
            metadataResources.surfaceColorTexture = surfaceColorTexture;
            metadataResources.mBufferDepthTexture = mBufferDepthTexture;

            bool drawFallback = settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled;
            DrawingSettings fallbackDrawingSettings = RenderingUtils.CreateDrawingSettings(
                FallbackShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
            fallbackDrawingSettings.overrideMaterialPassIndex = 0;

            DrawingSettings metadataDrawingSettings = RenderingUtils.CreateDrawingSettings(
                MetadataShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            RendererListParams fallbackRendererListParams = new RendererListParams(
                renderingData.cullResults,
                fallbackDrawingSettings,
                fallbackFilteringSettings);
            RendererListParams metadataRendererListParams = new RendererListParams(
                renderingData.cullResults,
                metadataDrawingSettings,
                metadataFilteringSettings);
            DrawingSettings surfaceColorOpaqueDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SurfaceColorShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            DrawingSettings surfaceColorTransparentDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SurfaceColorShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            RendererListParams surfaceColorTransparentRendererListParams = new RendererListParams(
                renderingData.cullResults,
                surfaceColorTransparentDrawingSettings,
                surfaceColorTransparentFilteringSettings);

            AddClearPass(
                renderGraph,
                maskIdTexture,
                surfaceDataTexture,
                custom0Texture,
                objectCustom0Texture,
                objectCustom1Texture,
                depthTexture,
                clearMaterial);

            AddSurfaceColorClearPass(renderGraph, surfaceColorTexture, mBufferDepthTexture);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-MetadataBuffer Output", out PassData passData, ProfilingSampler))
            {
                passData.drawFallback = drawFallback;
                passData.fallbackRendererList = drawFallback ? renderGraph.CreateRendererList(fallbackRendererListParams) : default;
                passData.metadataRendererList = renderGraph.CreateRendererList(metadataRendererListParams);
                passData.maskIdTexture = maskIdTexture;
                passData.surfaceDataTexture = surfaceDataTexture;
                passData.custom0Texture = custom0Texture;
                passData.objectCustom0Texture = objectCustom0Texture;
                passData.objectCustom1Texture = objectCustom1Texture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (drawFallback && passData.fallbackRendererList.IsValid())
                {
                    builder.UseRendererList(passData.fallbackRendererList);
                }

                if (passData.metadataRendererList.IsValid())
                {
                    builder.UseRendererList(passData.metadataRendererList);
                }

                builder.SetRenderAttachment(maskIdTexture, HoMetadataBufferAttachmentLayout.MaskId, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(surfaceDataTexture, HoMetadataBufferAttachmentLayout.SurfaceData, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(custom0Texture, HoMetadataBufferAttachmentLayout.Custom0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom0Texture, HoMetadataBufferAttachmentLayout.ObjectCustom0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(objectCustom1Texture, HoMetadataBufferAttachmentLayout.ObjectCustom1, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(maskIdTexture, HoMetadataBufferShaderConstants.MaskIdTextureId);
                builder.SetGlobalTextureAfterPass(surfaceDataTexture, HoMetadataBufferShaderConstants.SurfaceDataTextureId);
                builder.SetGlobalTextureAfterPass(custom0Texture, HoMetadataBufferShaderConstants.Custom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom0Texture, HoMetadataBufferShaderConstants.ObjectCustom0TextureId);
                builder.SetGlobalTextureAfterPass(objectCustom1Texture, HoMetadataBufferShaderConstants.ObjectCustom1TextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.drawFallback && data.fallbackRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.fallbackRendererList);
                    }

                    if (data.metadataRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.metadataRendererList);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-MetadataBuffer SurfaceColor Opaque", out PassData passData, ProfilingSampler))
            {
                passData.surfaceColorOpaqueRendererList = surfaceColorOpaqueFilteringEnabled
                    ? CreateRendererListWithRenderStateBlock(
                        renderGraph,
                        renderingData.cullResults,
                        surfaceColorOpaqueDrawingSettings,
                        surfaceColorOpaqueFilteringSettings,
                        mBufferDepthWriteStateBlock)
                    : default;
                passData.surfaceColorTexture = surfaceColorTexture;
                passData.mBufferDepthTexture = mBufferDepthTexture;
                passData.systemChannelMask = GetSystemChannelMask(settings);

                if (passData.surfaceColorOpaqueRendererList.IsValid())
                {
                    builder.UseRendererList(passData.surfaceColorOpaqueRendererList);
                }

                builder.SetRenderAttachment(surfaceColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(mBufferDepthTexture, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(surfaceColorTexture, HoMetadataBufferShaderConstants.SurfaceColorTextureId);
                builder.SetGlobalTextureAfterPass(mBufferDepthTexture, HoMetadataBufferShaderConstants.MBufferDepthTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                    SetDefaultSubjectProperties(context.cmd);
                    if (data.surfaceColorOpaqueRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.surfaceColorOpaqueRendererList);
                    }
                });
            }

            if (surfaceColorTransparentFilteringEnabled)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-MetadataBuffer SurfaceColor Transparent", out PassData passData, ProfilingSampler))
                {
                    passData.surfaceColorTransparentRendererList = renderGraph.CreateRendererList(surfaceColorTransparentRendererListParams);
                    passData.surfaceColorTexture = surfaceColorTexture;
                    passData.mBufferDepthTexture = mBufferDepthTexture;
                    passData.systemChannelMask = GetSystemChannelMask(settings);

                    if (passData.surfaceColorTransparentRendererList.IsValid())
                    {
                        builder.UseRendererList(passData.surfaceColorTransparentRendererList);
                    }

                    builder.SetRenderAttachment(surfaceColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(mBufferDepthTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(surfaceColorTexture, HoMetadataBufferShaderConstants.SurfaceColorTextureId);
                    builder.SetGlobalTextureAfterPass(mBufferDepthTexture, HoMetadataBufferShaderConstants.MBufferDepthTextureId);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 1.0f);
                        context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemChannelMaskId, data.systemChannelMask);
                        SetDefaultSubjectProperties(context.cmd);
                        if (data.surfaceColorTransparentRendererList.IsValid())
                        {
                            context.cmd.DrawRendererList(data.surfaceColorTransparentRendererList);
                        }
                    });
                }
            }
        }

        public void ReleaseCompatibilityResources(bool resetGlobalState = false)
        {
            renderTargets?.Release();
            for (int i = 0; i < colorTargets.Length; i++)
            {
                colorTargets[i] = null;
            }

            if (resetGlobalState)
            {
                ResetGlobalState();
            }
        }

        public static void ResetGlobalState()
        {
            Shader.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoMetadataBufferShaderConstants.MBufferDepthTextureId, Texture2D.blackTexture);
        }

        private static void AddClearPass(
            RenderGraph renderGraph,
            TextureHandle maskIdTexture,
            TextureHandle surfaceDataTexture,
            TextureHandle custom0Texture,
            TextureHandle objectCustom0Texture,
            TextureHandle objectCustom1Texture,
            TextureHandle depthTexture,
            Material clearMaterial)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ClearPassData>("Ho-MetadataBuffer Clear", out ClearPassData passData, ProfilingSampler))
            {
                passData.clearMaterial = clearMaterial;
                builder.SetRenderAttachment(maskIdTexture, HoMetadataBufferAttachmentLayout.MaskId, AccessFlags.WriteAll);
                builder.SetRenderAttachment(surfaceDataTexture, HoMetadataBufferAttachmentLayout.SurfaceData, AccessFlags.WriteAll);
                builder.SetRenderAttachment(custom0Texture, HoMetadataBufferAttachmentLayout.Custom0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom0Texture, HoMetadataBufferAttachmentLayout.ObjectCustom0, AccessFlags.WriteAll);
                builder.SetRenderAttachment(objectCustom1Texture, HoMetadataBufferAttachmentLayout.ObjectCustom1, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ClearPassData data, RasterGraphContext context) =>
                {
                    ClearMetadataTargets(context.cmd, data.clearMaterial);
                });
            }
        }

        private static void AddSurfaceColorClearPass(RenderGraph renderGraph, TextureHandle surfaceColorTexture, TextureHandle mBufferDepthTexture)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-MetadataBuffer SurfaceColorClear", out _, ProfilingSampler))
            {
                builder.SetRenderAttachment(surfaceColorTexture, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(mBufferDepthTexture, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(surfaceColorTexture, HoMetadataBufferShaderConstants.SurfaceColorTextureId);
                builder.SetGlobalTextureAfterPass(mBufferDepthTexture, HoMetadataBufferShaderConstants.MBufferDepthTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                });
            }
        }

        private void ClearMetadataTargets(CommandBuffer cmd)
        {
            cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1.0f, 0);
            if (clearMaterial != null)
            {
                cmd.DrawProcedural(Matrix4x4.identity, clearMaterial, 0, MeshTopology.Triangles, 3, 1);
                return;
            }

            cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
        }

        private static void ClearMetadataTargets(RasterCommandBuffer cmd, Material material)
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
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 0.0f);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-MetadataBuffer Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ActiveId, 0.0f);
                });
            }
        }

        private static RendererListHandle CreateRendererListWithRenderStateBlock(
            RenderGraph renderGraph,
            CullingResults cullingResults,
            DrawingSettings drawingSettings,
            FilteringSettings filteringSettings,
            RenderStateBlock stateBlock)
        {
            NativeArray<ShaderTagId> tagValues = new NativeArray<ShaderTagId>(1, Allocator.Temp);
            NativeArray<RenderStateBlock> stateBlocks = new NativeArray<RenderStateBlock>(1, Allocator.Temp);
            try
            {
                tagValues[0] = ShaderTagId.none;
                stateBlocks[0] = stateBlock;
                RendererListParams rendererListParams = new RendererListParams(cullingResults, drawingSettings, filteringSettings)
                {
                    tagValues = tagValues,
                    stateBlocks = stateBlocks,
                    isPassTagName = false
                };
                return renderGraph.CreateRendererList(rendererListParams);
            }
            finally
            {
                tagValues.Dispose();
                stateBlocks.Dispose();
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
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MaskIdTextureId, renderTargets.MaskIdTexture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom0TextureId, renderTargets.ObjectCustom0Texture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.ObjectCustom1TextureId, renderTargets.ObjectCustom1Texture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.SurfaceColorTextureId, renderTargets.SurfaceColorTexture.nameID);
            cmd.SetGlobalTexture(HoMetadataBufferShaderConstants.MBufferDepthTextureId, renderTargets.MBufferDepthTexture.nameID);
        }

        private void ApplyFallbackMaterialProperties()
        {
            if (fallbackMaterial == null)
            {
                return;
            }

            fallbackMaterial.SetFloat(HoMetadataBufferShaderConstants.SystemChannelMaskId, GetSystemChannelMask(settings));
        }

        private static void SetDefaultSubjectProperties(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemWriteMaskId, (float)HoMetadataBufferChannelMask.Default);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.TransmittanceHintId, 0.0f);
            cmd.SetGlobalVector(HoMetadataBufferShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoMetadataBufferShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ObjectCustomMaskId, 0.0f);
        }

        private static void SetDefaultSubjectProperties(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.MaskWeightId, 1.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.SystemWriteMaskId, (float)HoMetadataBufferChannelMask.Default);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.CustomWriteMaskId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.GroupIdId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ObjectIdId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.MaterialClassId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.FlagsId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ThicknessId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.CurvatureId, 0.0f);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.TransmittanceHintId, 0.0f);
            cmd.SetGlobalVector(HoMetadataBufferShaderConstants.DebugColorId, Vector4.one);
            cmd.SetGlobalVector(HoMetadataBufferShaderConstants.CustomValues0Id, Vector4.zero);
            cmd.SetGlobalFloat(HoMetadataBufferShaderConstants.ObjectCustomMaskId, 0.0f);
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
            metadataFilteringSettings = new FilteringSettings(renderQueueRange, layerMask);

            // The override fallback material cannot see the source material alpha/cutout data.
            // Keep it away from alpha-test and transparent queues; native metadata passes cover those.
            int fallbackMaxQueue = Mathf.Min(maxQueue, FallbackMaxRenderQueue);
            fallbackFilteringEnabled = fallbackMaxQueue >= minQueue;
            RenderQueueRange fallbackRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = fallbackFilteringEnabled ? fallbackMaxQueue : minQueue
            };
            fallbackFilteringSettings = new FilteringSettings(fallbackRenderQueueRange, fallbackFilteringEnabled ? layerMask : 0);

            int surfaceColorOpaqueMaxQueue = Mathf.Min(maxQueue, SurfaceColorOpaqueMaxRenderQueue);
            surfaceColorOpaqueFilteringEnabled = surfaceColorOpaqueMaxQueue >= minQueue;
            RenderQueueRange surfaceColorOpaqueRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = surfaceColorOpaqueFilteringEnabled ? surfaceColorOpaqueMaxQueue : minQueue
            };
            surfaceColorOpaqueFilteringSettings = new FilteringSettings(surfaceColorOpaqueRenderQueueRange, surfaceColorOpaqueFilteringEnabled ? layerMask : 0);

            int surfaceColorTransparentMinQueue = Mathf.Max(minQueue, SurfaceColorOpaqueMaxRenderQueue + 1);
            surfaceColorTransparentFilteringEnabled = maxQueue >= surfaceColorTransparentMinQueue;
            RenderQueueRange surfaceColorTransparentRenderQueueRange = new RenderQueueRange
            {
                lowerBound = surfaceColorTransparentMinQueue,
                upperBound = surfaceColorTransparentFilteringEnabled ? maxQueue : surfaceColorTransparentMinQueue
            };
            surfaceColorTransparentFilteringSettings = new FilteringSettings(surfaceColorTransparentRenderQueueRange, surfaceColorTransparentFilteringEnabled ? layerMask : 0);
        }

    }

}
