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

        private static readonly List<ShaderTagId> OutlineNormalDepthShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId(HoGeometryBufferShaderConstants.OutlineNormalDepthShaderPassName)
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
        private Material resolveMaterial;
        private FilteringSettings geometryFilteringSettings;
        private FilteringSettings fallbackFilteringSettings;
        private bool fallbackFilteringEnabled;
        private RenderStateBlock renderStateBlock;
        private readonly RenderTargetIdentifier[] resolveColorIdentifiers = new RenderTargetIdentifier[2];

        private sealed class PassData
        {
            public RendererListHandle geometryRendererList;
            public RendererListHandle fallbackRendererList;
            public bool drawFallback;
        }

        private sealed class OutlineNormalDepthPassData
        {
            public RendererListHandle rendererList;
        }

        private sealed class ResolvePassData
        {
            public TextureHandle normalDepthMsaaTexture;
            public TextureHandle depthMsaaTexture;
            public Material resolveMaterial;
            public int msaaSamples;
        }

        private sealed class OutlineResolvePassData
        {
            public TextureHandle outlineNormalDepthMsaaTexture;
            public Material resolveMaterial;
            public int msaaSamples;
        }

        public HoGeometryBufferPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material fallbackMaterial,
            Material resolveMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.fallbackMaterial = fallbackMaterial;
            this.resolveMaterial = resolveMaterial;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public void SetupRenderGraph(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material fallbackMaterial,
            Material resolveMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.fallbackMaterial = fallbackMaterial;
            this.resolveMaterial = resolveMaterial;
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

            int msaaSamples = resolveMaterial != null
                ? HoGeometryBufferRenderTargets.GetSupportedMsaaSampleCount(cameraTextureDescriptor, settings)
                : 1;
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings, msaaSamples);
            RTHandle normalDepthTarget = renderTargets.UseMsaaResolve
                ? renderTargets.NormalDepthMsaaTexture
                : renderTargets.NormalDepthTexture;
            RTHandle depthTarget = renderTargets.UseMsaaResolve
                ? renderTargets.DepthMsaaTexture
                : renderTargets.DepthTexture;
            ConfigureTarget(normalDepthTarget, depthTarget);
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
                bool useMsaaResolve = renderTargets.UseMsaaResolve && resolveMaterial != null;
                RTHandle normalDepthTarget = useMsaaResolve
                    ? renderTargets.NormalDepthMsaaTexture
                    : renderTargets.NormalDepthTexture;
                RTHandle depthTarget = useMsaaResolve
                    ? renderTargets.DepthMsaaTexture
                    : renderTargets.DepthTexture;
                RTHandle outlineNormalDepthTarget = useMsaaResolve
                    ? renderTargets.OutlineNormalDepthMsaaTexture
                    : renderTargets.OutlineNormalDepthTexture;

                cmd.SetRenderTarget(
                    normalDepthTarget,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    depthTarget,
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

                cmd.SetRenderTarget(
                    outlineNormalDepthTarget,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store,
                    depthTarget,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store);
                cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings outlineNormalDepthDrawingSettings = CreateDrawingSettings(OutlineNormalDepthShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref outlineNormalDepthDrawingSettings, ref geometryFilteringSettings, ref renderStateBlock);

                if (useMsaaResolve)
                {
                    ResolveGeometryBuffer(cmd);
                    ResolveOutlineNormalDepth(cmd);
                }

                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, renderTargets.DepthTexture.nameID);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, renderTargets.OutlineNormalDepthTexture.nameID);
                if (useMsaaResolve)
                {
                    cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
                    cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineCoverageTextureId, renderTargets.OutlineCoverageTexture.nameID);
                }

                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 1.0f);
                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, useMsaaResolve ? 1.0f : 0.0f);
                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, useMsaaResolve ? 1.0f : 0.0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            int msaaSamples = resolveMaterial != null
                ? HoGeometryBufferRenderTargets.GetSupportedMsaaSampleCount(cameraData.cameraTargetDescriptor, settings)
                : 1;
            bool useMsaaResolve = msaaSamples > 1;

            TextureHandle normalDepthTexture = renderGraph.CreateTexture(CreateTextureDesc(
                cameraData.cameraTargetDescriptor,
                settings,
                HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat(),
                HoGeometryBufferShaderConstants.NormalDepthTextureName));
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoGeometryBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings),
                HoGeometryBufferShaderConstants.DepthTextureName,
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            TextureHandle outlineNormalDepthTexture = renderGraph.CreateTexture(CreateOutlineNormalDepthDesc(
                cameraData.cameraTargetDescriptor,
                settings));
            TextureHandle coverageTexture = TextureHandle.nullHandle;
            TextureHandle outlineCoverageTexture = TextureHandle.nullHandle;
            TextureHandle normalDepthMsaaTexture = TextureHandle.nullHandle;
            TextureHandle depthMsaaTexture = TextureHandle.nullHandle;
            TextureHandle outlineNormalDepthMsaaTexture = TextureHandle.nullHandle;
            if (useMsaaResolve)
            {
                normalDepthMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat(),
                    HoGeometryBufferShaderConstants.NormalDepthTextureName + "MSAA",
                    msaaSamples,
                    true));
                depthMsaaTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    HoGeometryBufferRenderTargets.CreateDepthDescriptor(cameraData.cameraTargetDescriptor, settings, msaaSamples, true),
                    HoGeometryBufferShaderConstants.DepthTextureName + "MSAA",
                    true,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                outlineNormalDepthMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat(),
                    HoGeometryBufferShaderConstants.OutlineNormalDepthTextureName + "MSAA",
                    msaaSamples,
                    true));
                coverageTexture = renderGraph.CreateTexture(CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    HoGeometryBufferFormatUtility.GetCoverageGraphicsFormat(),
                    HoGeometryBufferShaderConstants.CoverageTextureName));
                outlineCoverageTexture = renderGraph.CreateTexture(CreateTextureDesc(
                    cameraData.cameraTargetDescriptor,
                    settings,
                    HoGeometryBufferFormatUtility.GetCoverageGraphicsFormat(),
                    HoGeometryBufferShaderConstants.OutlineCoverageTextureName));
            }

            geometryResources.normalDepthTexture = normalDepthTexture;
            geometryResources.depthTexture = depthTexture;
            geometryResources.outlineNormalDepthTexture = outlineNormalDepthTexture;
            geometryResources.coverageTexture = coverageTexture;
            geometryResources.outlineCoverageTexture = outlineCoverageTexture;

            TextureHandle geometryColorTarget = useMsaaResolve ? normalDepthMsaaTexture : normalDepthTexture;
            TextureHandle geometryDepthTarget = useMsaaResolve ? depthMsaaTexture : depthTexture;
            TextureHandle outlineColorTarget = useMsaaResolve ? outlineNormalDepthMsaaTexture : outlineNormalDepthTexture;

            bool drawFallback = fallbackMaterial != null && fallbackFilteringEnabled;
            DrawingSettings geometryDrawingSettings = RenderingUtils.CreateDrawingSettings(
                GeometryShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            DrawingSettings outlineNormalDepthDrawingSettings = RenderingUtils.CreateDrawingSettings(
                OutlineNormalDepthShaderTagIds,
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
            RendererListParams outlineNormalDepthRendererListParams = new RendererListParams(
                renderingData.cullResults,
                outlineNormalDepthDrawingSettings,
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

                builder.SetRenderAttachment(geometryColorTarget, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(geometryDepthTarget, AccessFlags.WriteAll);
                if (!useMsaaResolve)
                {
                    builder.SetGlobalTextureAfterPass(normalDepthTexture, HoGeometryBufferShaderConstants.NormalDepthTextureId);
                    builder.SetGlobalTextureAfterPass(depthTexture, HoGeometryBufferShaderConstants.DepthTextureId);
                }

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

                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 1.0f);
                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, 0.0f);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<OutlineNormalDepthPassData>("Ho-GeometryBuffer Outline NormalDepth", out OutlineNormalDepthPassData outlinePassData, ProfilingSampler))
            {
                outlinePassData.rendererList = renderGraph.CreateRendererList(outlineNormalDepthRendererListParams);
                if (outlinePassData.rendererList.IsValid())
                {
                    builder.UseRendererList(outlinePassData.rendererList);
                }

                builder.SetRenderAttachment(outlineColorTarget, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(geometryDepthTarget, AccessFlags.Read);
                if (!useMsaaResolve)
                {
                    builder.SetGlobalTextureAfterPass(outlineNormalDepthTexture, HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId);
                }

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (OutlineNormalDepthPassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                    if (data.rendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.rendererList);
                    }

                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, 0.0f);
                });
            }

            if (useMsaaResolve)
            {
                using (var builder = renderGraph.AddRasterRenderPass<ResolvePassData>("Ho-GeometryBuffer MSAA Resolve", out ResolvePassData resolvePassData, ProfilingSampler))
                {
                    resolvePassData.normalDepthMsaaTexture = normalDepthMsaaTexture;
                    resolvePassData.depthMsaaTexture = depthMsaaTexture;
                    resolvePassData.resolveMaterial = resolveMaterial;
                    resolvePassData.msaaSamples = msaaSamples;

                    builder.UseTexture(resolvePassData.normalDepthMsaaTexture, AccessFlags.Read);
                    builder.UseTexture(resolvePassData.depthMsaaTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(normalDepthTexture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(coverageTexture, 1, AccessFlags.WriteAll);
                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(normalDepthTexture, HoGeometryBufferShaderConstants.NormalDepthTextureId);
                    builder.SetGlobalTextureAfterPass(depthTexture, HoGeometryBufferShaderConstants.DepthTextureId);
                    builder.SetGlobalTextureAfterPass(coverageTexture, HoGeometryBufferShaderConstants.CoverageTextureId);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ResolvePassData data, RasterGraphContext context) =>
                    {
                        SetResolveKeywords(data.resolveMaterial, data.msaaSamples);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveNormalDepthTextureMsId, data.normalDepthMsaaTexture);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveDepthTextureMsId, data.depthMsaaTexture);
                        context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.resolveMaterial, 0, MeshTopology.Triangles, 3, 1);
                        context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 1.0f);
                        context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, 1.0f);
                    });
                }

                using (var builder = renderGraph.AddRasterRenderPass<OutlineResolvePassData>("Ho-GeometryBuffer Outline MSAA Resolve", out OutlineResolvePassData outlineResolvePassData, ProfilingSampler))
                {
                    outlineResolvePassData.outlineNormalDepthMsaaTexture = outlineNormalDepthMsaaTexture;
                    outlineResolvePassData.resolveMaterial = resolveMaterial;
                    outlineResolvePassData.msaaSamples = msaaSamples;

                    builder.UseTexture(outlineResolvePassData.outlineNormalDepthMsaaTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(outlineNormalDepthTexture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(outlineCoverageTexture, 1, AccessFlags.WriteAll);
                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(outlineNormalDepthTexture, HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId);
                    builder.SetGlobalTextureAfterPass(outlineCoverageTexture, HoGeometryBufferShaderConstants.OutlineCoverageTextureId);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (OutlineResolvePassData data, RasterGraphContext context) =>
                    {
                        SetResolveKeywords(data.resolveMaterial, data.msaaSamples);
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveNormalDepthTextureMsId, data.outlineNormalDepthMsaaTexture);
                        context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1.0f, 0);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.resolveMaterial, 1, MeshTopology.Triangles, 3, 1);
                        context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, 1.0f);
                    });
                }
            }
        }

        public void ReleaseCompatibilityResources(bool resetGlobalState = false)
        {
            renderTargets?.Release();
            if (resetGlobalState)
            {
                ResetGlobalState();
            }
        }

        public static void ResetGlobalState()
        {
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.DepthTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.CoverageTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineCoverageTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, Texture2D.blackTexture);
            Shader.SetGlobalFloat(HoGeometryBufferShaderConstants.ValidId, 0.0f);
            Shader.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, 0.0f);
            Shader.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, 0.0f);
            Shader.SetGlobalFloat(HoGeometryBufferShaderConstants.SkyTextureValidId, 0.0f);
        }

        private void ResolveGeometryBuffer(CommandBuffer cmd)
        {
            SetResolveKeywords(resolveMaterial, renderTargets.MsaaSamples);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveNormalDepthTextureMsId, renderTargets.NormalDepthMsaaTexture.nameID);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveDepthTextureMsId, renderTargets.DepthMsaaTexture.nameID);
            resolveColorIdentifiers[0] = renderTargets.NormalDepthTexture.nameID;
            resolveColorIdentifiers[1] = renderTargets.CoverageTexture.nameID;
            CoreUtils.SetRenderTarget(cmd, resolveColorIdentifiers, renderTargets.DepthTexture, ClearFlag.All, Color.clear);
            cmd.DrawProcedural(Matrix4x4.identity, resolveMaterial, 0, MeshTopology.Triangles, 3, 1);
        }

        private void ResolveOutlineNormalDepth(CommandBuffer cmd)
        {
            SetResolveKeywords(resolveMaterial, renderTargets.MsaaSamples);
            cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.ResolveNormalDepthTextureMsId, renderTargets.OutlineNormalDepthMsaaTexture.nameID);
            resolveColorIdentifiers[0] = renderTargets.OutlineNormalDepthTexture.nameID;
            resolveColorIdentifiers[1] = renderTargets.OutlineCoverageTexture.nameID;
            CoreUtils.SetRenderTarget(cmd, resolveColorIdentifiers, renderTargets.DepthTexture, ClearFlag.Color, Color.clear);
            cmd.DrawProcedural(Matrix4x4.identity, resolveMaterial, 1, MeshTopology.Triangles, 3, 1);
        }

        private static void SetResolveKeywords(Material material, int msaaSamples)
        {
            CoreUtils.SetKeyword(material, "_HO_GEOMETRY_BUFFER_MSAA_2", msaaSamples == 2);
            CoreUtils.SetKeyword(material, "_HO_GEOMETRY_BUFFER_MSAA_4", msaaSamples == 4);
            CoreUtils.SetKeyword(material, "_HO_GEOMETRY_BUFFER_MSAA_8", msaaSamples >= 8);
        }

        private static TextureDesc CreateTextureDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoGeometryBufferSettings settings,
            GraphicsFormat format,
            string name,
            int msaaSamples = 1,
            bool bindTextureMS = false)
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
            descriptor.msaaSamples = ToRenderGraphMsaaSamples(msaaSamples);
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = bindTextureMS && msaaSamples > 1;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }

        private static MSAASamples ToRenderGraphMsaaSamples(int msaaSamples)
        {
            if (msaaSamples >= 8)
            {
                return MSAASamples.MSAA8x;
            }

            if (msaaSamples >= 4)
            {
                return MSAASamples.MSAA4x;
            }

            if (msaaSamples >= 2)
            {
                return MSAASamples.MSAA2x;
            }

            return MSAASamples.None;
        }

        private static TextureDesc CreateOutlineNormalDepthDesc(
            RenderTextureDescriptor cameraTextureDescriptor,
            HoGeometryBufferSettings settings)
        {
            // Outline normal/depth is sampled as the same packed RGBA contract as
            // the physical NormalDepth buffer. Keep the RenderGraph path in sync
            // with the compatibility RT allocation so alpha depth is preserved.
            GraphicsFormat format = HoGeometryBufferFormatUtility.GetHighPrecisionGraphicsFormat();
            return CreateTextureDesc(
                cameraTextureDescriptor,
                settings,
                format,
                HoGeometryBufferShaderConstants.OutlineNormalDepthTextureName);
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
