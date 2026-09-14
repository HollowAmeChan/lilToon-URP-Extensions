using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// ID / 覆盖率的生产 pass（规划 §5.4）。
    /// <list type="bullet">
    /// <item>自建 MSAA：采样数来自 feature 设置，**与相机 MSAA 解耦**（决策 7）；</item>
    /// <item>N = 1 时直接写 4 层结果；N &gt; 1 时逐样本写一个 16 bit ID，再由 resolve 数票；</item>
    /// <item>`_Surface` 走**自己的单采样 pass**，沿用 opaque 写深度 / transparent 只 ZTest 的两段式策略（决策 15）；</item>
    /// <item>ID pass 的 depth-stencil 是内部附件：不发布、不给任何 shader 采样（决策 16）。</item>
    /// </list>
    /// </summary>
    internal sealed class HoCharacterBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-CharacterBuffer Output");
        private static readonly ProfilingSampler ResolveProfilingSampler = new ProfilingSampler("Ho-CharacterBuffer MSAA Resolve");
        private static readonly ProfilingSampler SurfaceProfilingSampler = new ProfilingSampler("Ho-CharacterBuffer Surface");

        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private static readonly List<ShaderTagId> IdShaderTagIds = new List<ShaderTagId>
        {
            HoCharacterBufferShaderConstants.ShaderTagId
        };

        private static readonly List<ShaderTagId> SurfaceShaderTagIds = new List<ShaderTagId>
        {
            HoCharacterBufferShaderConstants.SurfaceShaderTagId
        };

        private const int FallbackPassLayers = 0;
        private const int FallbackPassMsaaInt = 1;
        private const int FallbackPassMsaaUnorm = 2;

        /// <summary>override 材质看不到源材质的 alpha/cutout，所以 fallback 只碰不透明队列（与 MetadataBuffer 同一取舍）。</summary>
        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;

        private const int SurfaceOpaqueMaxRenderQueue = (int)RenderQueue.GeometryLast;

        private readonly RTHandle[] idColorTargets = new RTHandle[3];
        private readonly RTHandle[] msaaColorTargets = new RTHandle[2];
        private readonly RenderTargetIdentifier[] resolveColorIdentifiers = new RenderTargetIdentifier[4];

        private HoCharacterBufferSettings settings;
        private HoCharacterBufferRenderTargets renderTargets;
        private Material fallbackMaterial;
        private Material resolveMaterial;
        private FilteringSettings fallbackFilteringSettings;
        private FilteringSettings idFilteringSettings;
        private FilteringSettings surfaceOpaqueFilteringSettings;
        private FilteringSettings surfaceTransparentFilteringSettings;
        private bool fallbackFilteringEnabled;
        private bool surfaceOpaqueFilteringEnabled;
        private bool surfaceTransparentFilteringEnabled;
        private int msaaSamples = 1;
        private bool selectionEnabled;
        private readonly RenderStateBlock renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        private readonly RenderStateBlock depthWriteStateBlock = new RenderStateBlock(RenderStateMask.Depth)
        {
            depthState = new DepthState(true, CompareFunction.LessEqual)
        };

        private sealed class IdPassData
        {
            public RendererListHandle fallbackRendererList;
            public RendererListHandle idRendererList;
            public bool drawFallback;
            public bool selectionEnabled;
            public float selectionLayerCount;
        }

        private sealed class ResolvePassData
        {
            public Material resolveMaterial;
            public int msaaSamples;
            public bool idFormatIsInteger;
            public bool selectionEnabled;
            public float selectionLayerCount;
        }

        private sealed class SurfacePassData
        {
            public RendererListHandle opaqueRendererList;
            public RendererListHandle transparentRendererList;
        }

        private sealed class ResetPassData
        {
        }

        public void Setup(
            HoCharacterBufferSettings settings,
            HoCharacterBufferRenderTargets renderTargets,
            Material fallbackMaterial,
            Material resolveMaterial,
            int samples,
            bool selectionEnabled)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.fallbackMaterial = fallbackMaterial;
            this.resolveMaterial = resolveMaterial;
            msaaSamples = Mathf.Max(1, samples);
            this.selectionEnabled = selectionEnabled;
            renderPassEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
            ConfigureFiltering();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (settings == null || renderTargets == null)
            {
                return;
            }

            HoCharacterBufferRegistry.EnsureBuilt();
            UpdateDerivedState();
            msaaSamples = HoCharacterBufferFormatUtility.GetSupportedSampleCount(
                cameraTextureDescriptor,
                settings.RequestedSampleCount,
                selectionEnabled);
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings, msaaSamples, settings.useMaterial0, selectionEnabled);

            if (renderTargets.UseMsaaResolve)
            {
                // MSAA：逐样本只写一个 ID；选择层同 pass，采样数必须一致。
                msaaColorTargets[0] = renderTargets.IdMsaaTexture;
                if (selectionEnabled && renderTargets.SelectionMsaaTexture != null)
                {
                    msaaColorTargets[1] = renderTargets.SelectionMsaaTexture;
                    ConfigureTarget(new[] { msaaColorTargets[0], msaaColorTargets[1] }, renderTargets.DepthMsaaTexture);
                }
                else
                {
                    ConfigureTarget(msaaColorTargets[0], renderTargets.DepthMsaaTexture);
                }
            }
            else
            {
                idColorTargets[0] = renderTargets.Id0Texture;
                idColorTargets[1] = renderTargets.Id1Texture;
                idColorTargets[2] = renderTargets.CoverageTexture;
                ConfigureTarget(idColorTargets, renderTargets.DepthTexture);
            }

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
                cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionLayerCountId, selectionEnabled ? settings.RequestedSelectionLayerCount : 0);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled)
                {
                    DrawingSettings fallbackDrawingSettings = CreateDrawingSettings(FallbackShaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
                    fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
                    fallbackDrawingSettings.overrideMaterialPassIndex = GetFallbackPassIndex();
                    context.DrawRenderers(renderingData.cullResults, ref fallbackDrawingSettings, ref fallbackFilteringSettings, ref renderStateBlock);
                }

                DrawingSettings idDrawingSettings = CreateDrawingSettings(IdShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref idDrawingSettings, ref idFilteringSettings, ref renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            if (renderTargets.UseMsaaResolve)
            {
                using (new ProfilingScope(cmd, ResolveProfilingSampler))
                {
                    ResolveMsaa(cmd);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }

            using (new ProfilingScope(cmd, SurfaceProfilingSampler))
            {
                DrawSurface(context, ref renderingData, cmd);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            PublishGlobals(cmd);
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

            // 表内容变了才重建（部件/选择由注册表编译），并保证 GraphicsBuffer 已上传。
            HoCharacterBufferRegistry.EnsureBuilt();
            UpdateDerivedState();

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoCharacterBufferRenderGraphResources resources = frameData.GetOrCreate<HoCharacterBufferRenderGraphResources>();

            RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
            // 采样数**只问平台**：相机把 MSAA 关掉时，覆盖率照样是 4x（决策 7，也是最初那个 bug 的场景）。
            msaaSamples = HoCharacterBufferFormatUtility.GetSupportedSampleCount(
                cameraDescriptor,
                settings.RequestedSampleCount,
                selectionEnabled);
            bool useMsaa = msaaSamples > 1;
            bool selection = selectionEnabled && HoCharacterBufferRegistry.SelectionCount > 0;

            TextureHandle id0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.Id0TextureName));
            TextureHandle id1Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.Id1TextureName));
            TextureHandle coverageTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.CoverageTextureName));
            TextureHandle surfaceTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetSurfaceGraphicsFormat(), HoCharacterBufferShaderConstants.SurfaceTextureName));
            TextureHandle depthTexture = renderGraph.CreateTexture(CreateDepthTextureDesc(cameraDescriptor, 1, false, HoCharacterBufferShaderConstants.Id0TextureName + "Depth"));

            TextureHandle material0Texture = TextureHandle.nullHandle;
            if (settings.useMaterial0)
            {
                material0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.Material0TextureName));
            }

            TextureHandle selectionTexture = TextureHandle.nullHandle;
            if (selection)
            {
                selectionTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.SelectionTextureName));
            }

            resources.id0Texture = id0Texture;
            resources.id1Texture = id1Texture;
            resources.coverageTexture = coverageTexture;
            resources.surfaceTexture = surfaceTexture;
            resources.material0Texture = material0Texture;
            resources.selectionTexture = selectionTexture;
            resources.depthTexture = depthTexture;

            TextureHandle idMsaaTexture = TextureHandle.nullHandle;
            TextureHandle selectionMsaaTexture = TextureHandle.nullHandle;
            TextureHandle depthMsaaTexture = TextureHandle.nullHandle;
            if (useMsaa)
            {
                HoCharacterBufferFormatUtility.TryGetIdGraphicsFormat(out GraphicsFormat idFormat, out _);
                idMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, idFormat, HoCharacterBufferShaderConstants.Id0TextureName + "MSAA", msaaSamples));                depthMsaaTexture = renderGraph.CreateTexture(CreateDepthTextureDesc(cameraDescriptor, msaaSamples, true, HoCharacterBufferShaderConstants.Id0TextureName + "DepthMSAA"));
                if (selection)
                {
                    selectionMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoCharacterBufferFormatUtility.GetLayerGraphicsFormat(), HoCharacterBufferShaderConstants.SelectionTextureName + "MSAA", msaaSamples));
                }
            }

            DrawingSettings fallbackDrawingSettings = RenderingUtils.CreateDrawingSettings(
                FallbackShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
            fallbackDrawingSettings.overrideMaterialPassIndex = GetFallbackPassIndex();
            DrawingSettings idDrawingSettings = RenderingUtils.CreateDrawingSettings(
                IdShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);
            DrawingSettings surfaceOpaqueDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SurfaceShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            DrawingSettings surfaceTransparentDrawingSettings = RenderingUtils.CreateDrawingSettings(
                SurfaceShaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            using (var builder = renderGraph.AddRasterRenderPass<IdPassData>("Ho-Character-Buffer ID", out IdPassData passData, ProfilingSampler))
            {
                passData.drawFallback = settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled;
                passData.fallbackRendererList = passData.drawFallback
                    ? CreateRendererList(renderGraph, renderingData.cullResults, fallbackDrawingSettings, fallbackFilteringSettings, renderStateBlock)
                    : default;
                passData.idRendererList = CreateRendererList(renderGraph, renderingData.cullResults, idDrawingSettings, idFilteringSettings, renderStateBlock);
                passData.selectionEnabled = selection;
                passData.selectionLayerCount = selection ? settings.RequestedSelectionLayerCount : 0;

                if (passData.fallbackRendererList.IsValid())
                {
                    builder.UseRendererList(passData.fallbackRendererList);
                }

                if (passData.idRendererList.IsValid())
                {
                    builder.UseRendererList(passData.idRendererList);
                }

                if (useMsaa)
                {
                    builder.SetRenderAttachment(idMsaaTexture, 0, AccessFlags.Write);
                    if (selection && selectionMsaaTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionMsaaTexture, 1, AccessFlags.Write);
                    }

                    builder.SetRenderAttachmentDepth(depthMsaaTexture, AccessFlags.Write);
                }
                else
                {
                    builder.SetRenderAttachment(id0Texture, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(id1Texture, 1, AccessFlags.Write);
                    builder.SetRenderAttachment(coverageTexture, 2, AccessFlags.Write);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionTexture, 3, AccessFlags.Write);
                    }

                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(id0Texture, HoCharacterBufferShaderConstants.Id0TextureId);
                    builder.SetGlobalTextureAfterPass(id1Texture, HoCharacterBufferShaderConstants.Id1TextureId);
                    builder.SetGlobalTextureAfterPass(coverageTexture, HoCharacterBufferShaderConstants.CoverageTextureId);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetGlobalTextureAfterPass(selectionTexture, HoCharacterBufferShaderConstants.SelectionTextureId);
                    }
                }

                if (settings.useMaterial0 && material0Texture.IsValid())
                {
                    // Material0（管线逐像素材质值）由 lilToon 侧的 pass 写；P1 只分配、不在这里挂 MRT——
                    // RG 的 attachment 索引必须从 0 连续，现在挂进去会在"有选择层/没选择层"两种布局下打架。
                    builder.SetGlobalTextureAfterPass(material0Texture, HoCharacterBufferShaderConstants.Material0TextureId);
                }

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (IdPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionLayerCountId, data.selectionLayerCount);
                    if (data.drawFallback && data.fallbackRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.fallbackRendererList);
                    }

                    if (data.idRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.idRendererList);
                    }
                });
            }

            if (useMsaa)
            {
                using (var builder = renderGraph.AddRasterRenderPass<ResolvePassData>("Ho-Character-Buffer MSAA Resolve", out ResolvePassData passData, ResolveProfilingSampler))
                {
                    passData.resolveMaterial = resolveMaterial;
                    passData.msaaSamples = msaaSamples;
                    passData.idFormatIsInteger = IsIdFormatInteger();
                    passData.selectionEnabled = selection;
                    passData.selectionLayerCount = selection ? settings.RequestedSelectionLayerCount : 0;

                    builder.UseTexture(idMsaaTexture, AccessFlags.Read);
                    builder.UseTexture(depthMsaaTexture, AccessFlags.Read);
                    if (selection && selectionMsaaTexture.IsValid())
                    {
                        builder.UseTexture(selectionMsaaTexture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(id0Texture, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(id1Texture, 1, AccessFlags.Write);
                    builder.SetRenderAttachment(coverageTexture, 2, AccessFlags.Write);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionTexture, 3, AccessFlags.Write);
                    }

                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(id0Texture, HoCharacterBufferShaderConstants.Id0TextureId);
                    builder.SetGlobalTextureAfterPass(id1Texture, HoCharacterBufferShaderConstants.Id1TextureId);
                    builder.SetGlobalTextureAfterPass(coverageTexture, HoCharacterBufferShaderConstants.CoverageTextureId);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetGlobalTextureAfterPass(selectionTexture, HoCharacterBufferShaderConstants.SelectionTextureId);
                    }

                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ResolvePassData data, RasterGraphContext context) =>
                    {
                        if (data.resolveMaterial == null)
                        {
                            return;
                        }

                        SetResolveKeywords(data.resolveMaterial, data.msaaSamples, data.idFormatIsInteger, data.selectionEnabled);
                        context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ActiveId, 1.0f);
                        context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionLayerCountId, data.selectionLayerCount);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.resolveMaterial, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }

            using (var builder = renderGraph.AddRasterRenderPass<SurfacePassData>("Ho-Character-Buffer Surface", out SurfacePassData passData, SurfaceProfilingSampler))
            {
                passData.opaqueRendererList = surfaceOpaqueFilteringEnabled
                    ? CreateRendererList(renderGraph, renderingData.cullResults, surfaceOpaqueDrawingSettings, surfaceOpaqueFilteringSettings, depthWriteStateBlock)
                    : default;
                passData.transparentRendererList = surfaceTransparentFilteringEnabled
                    ? CreateRendererList(renderGraph, renderingData.cullResults, surfaceTransparentDrawingSettings, surfaceTransparentFilteringSettings, renderStateBlock)
                    : default;

                if (passData.opaqueRendererList.IsValid())
                {
                    builder.UseRendererList(passData.opaqueRendererList);
                }

                if (passData.transparentRendererList.IsValid())
                {
                    builder.UseRendererList(passData.transparentRendererList);
                }

                builder.SetRenderAttachment(surfaceTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(surfaceTexture, HoCharacterBufferShaderConstants.SurfaceTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (SurfacePassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                    if (data.opaqueRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.opaqueRendererList);
                    }

                    if (data.transparentRendererList.IsValid())
                    {
                        context.cmd.DrawRendererList(data.transparentRendererList);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-Character-Buffer Valid", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, 1.0f);
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.PartCountId, HoCharacterBufferRegistry.PartRowCount);
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionCountId, HoCharacterBufferRegistry.SelectionCount);
                });
            }
        }

        public void ReleaseCompatibilityResources(bool resetGlobalState = false)
        {
            renderTargets?.Release();
            for (int i = 0; i < idColorTargets.Length; i++)
            {
                idColorTargets[i] = null;
            }

            for (int i = 0; i < msaaColorTargets.Length; i++)
            {
                msaaColorTargets[i] = null;
            }

            if (resetGlobalState)
            {
                ResetGlobalState();
            }
        }

        public static void ResetGlobalState()
        {
            Shader.SetGlobalFloat(HoCharacterBufferShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, 0.0f);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.Id0TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.Id1TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.CoverageTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.SurfaceTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.Material0TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoCharacterBufferShaderConstants.SelectionTextureId, Texture2D.blackTexture);
        }

        private void ResolveMsaa(CommandBuffer cmd)
        {
            if (resolveMaterial == null)
            {
                return;
            }

            SetResolveKeywords(resolveMaterial, msaaSamples, IsIdFormatInteger(), selectionEnabled);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.ResolveIdTextureMsId, renderTargets.IdMsaaTexture.nameID);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.ResolveDepthTextureMsId, renderTargets.DepthMsaaTexture.nameID);
            if (selectionEnabled && renderTargets.SelectionMsaaTexture != null)
            {
                cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.ResolveSelectionTextureMsId, renderTargets.SelectionMsaaTexture.nameID);
            }

            resolveColorIdentifiers[0] = renderTargets.Id0Texture.nameID;
            resolveColorIdentifiers[1] = renderTargets.Id1Texture.nameID;
            resolveColorIdentifiers[2] = renderTargets.CoverageTexture.nameID;
            resolveColorIdentifiers[3] = selectionEnabled && renderTargets.SelectionTexture != null
                ? renderTargets.SelectionTexture.nameID
                : renderTargets.CoverageTexture.nameID;   // 选择层关闭时该 MRT 不参与，写回覆盖率图是无害的占位

            CoreUtils.SetRenderTarget(cmd, resolveColorIdentifiers, renderTargets.DepthTexture, ClearFlag.None, Color.clear);
            CoreUtils.DrawFullScreen(cmd, resolveMaterial, shaderPassId: 0);
        }

        private void DrawSurface(ScriptableRenderContext context, ref RenderingData renderingData, CommandBuffer cmd)
        {
            cmd.SetRenderTarget(
                renderTargets.SurfaceTexture,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store,
                renderTargets.DepthTexture,
                RenderBufferLoadAction.DontCare,
                RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            if (surfaceOpaqueFilteringEnabled)
            {
                DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(SurfaceShaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
                context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings, ref surfaceOpaqueFilteringSettings, ref depthWriteStateBlock);
            }

            if (surfaceTransparentFilteringEnabled)
            {
                DrawingSettings transparentDrawingSettings = CreateDrawingSettings(SurfaceShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                context.DrawRenderers(renderingData.cullResults, ref transparentDrawingSettings, ref surfaceTransparentFilteringSettings, ref renderStateBlock);
            }
        }

        private void PublishGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, 1.0f);
            cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.PartCountId, HoCharacterBufferRegistry.PartRowCount);
            cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionCountId, HoCharacterBufferRegistry.SelectionCount);
            cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.SelectionLayerCountId, selectionEnabled ? settings.RequestedSelectionLayerCount : 0);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id0TextureId, renderTargets.Id0Texture.nameID);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id1TextureId, renderTargets.Id1Texture.nameID);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
            cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SurfaceTextureId, renderTargets.SurfaceTexture.nameID);
            if (renderTargets.Material0Texture != null)
            {
                cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Material0TextureId, renderTargets.Material0Texture.nameID);
            }

            if (selectionEnabled && renderTargets.SelectionTexture != null)
            {
                cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SelectionTextureId, renderTargets.SelectionTexture.nameID);
            }
        }

        private static bool? idFormatIsIntegerCache;

        /// <summary>逐样本 ID 目标是不是整数格式。格式支持在运行期不会变，缓存一次。</summary>
        private static bool IsIdFormatInteger()
        {
            if (!idFormatIsIntegerCache.HasValue)
            {
                idFormatIsIntegerCache = HoCharacterBufferFormatUtility.TryGetIdGraphicsFormat(out _, out bool isInteger) && isInteger;
            }

            return idFormatIsIntegerCache.Value;
        }

        private int GetFallbackPassIndex()
        {
            if (msaaSamples <= 1)
            {
                return FallbackPassLayers;
            }

            return IsIdFormatInteger() ? FallbackPassMsaaInt : FallbackPassMsaaUnorm;
        }

        private static void SetResolveKeywords(Material material, int samples, bool idFormatIsInteger, bool selectionEnabled)
        {
            if (material == null)
            {
                return;
            }

            material.DisableKeyword(HoCharacterBufferShaderConstants.Msaa2Keyword);
            material.DisableKeyword(HoCharacterBufferShaderConstants.Msaa4Keyword);
            material.EnableKeyword(samples <= 2 ? HoCharacterBufferShaderConstants.Msaa2Keyword : HoCharacterBufferShaderConstants.Msaa4Keyword);

            const string idUnormKeyword = "_HO_CHARACTER_BUFFER_ID_UNORM";
            if (idFormatIsInteger)
            {
                material.DisableKeyword(idUnormKeyword);
            }
            else
            {
                material.EnableKeyword(idUnormKeyword);
            }

            const string selectionKeyword = "_HO_CHARACTER_BUFFER_HAS_SELECTION";
            if (selectionEnabled)
            {
                material.EnableKeyword(selectionKeyword);
            }
            else
            {
                material.DisableKeyword(selectionKeyword);
            }
        }

        private void UpdateDerivedState()
        {
            // 选择层是否真的产出：设置要开，且注册表里确实注册了选择（"没有消费者不产出"）。
            selectionEnabled = settings != null &&
                settings.RequestedSelectionLayerCount > 0 &&
                HoCharacterBufferRegistry.SelectionCount > 0;
            ConfigureFiltering();
        }

        private void ConfigureFiltering()
        {
            int minQueue = settings != null ? settings.minRenderQueue : 0;
            int maxQueue = settings != null ? settings.maxRenderQueue : (int)RenderQueue.Overlay - 1;
            if (maxQueue < minQueue)
            {
                maxQueue = minQueue;
            }

            var idRenderQueueRange = new RenderQueueRange { lowerBound = minQueue, upperBound = maxQueue };
            int layerMask = settings != null ? settings.layerMask.value : -1;
            idFilteringSettings = new FilteringSettings(idRenderQueueRange, layerMask);

            int fallbackMaxQueue = Mathf.Min(maxQueue, FallbackMaxRenderQueue);
            fallbackFilteringEnabled = fallbackMaxQueue >= minQueue;
            var fallbackRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = fallbackFilteringEnabled ? fallbackMaxQueue : minQueue
            };
            fallbackFilteringSettings = new FilteringSettings(fallbackRenderQueueRange, fallbackFilteringEnabled ? layerMask : 0);

            int surfaceOpaqueMaxQueue = Mathf.Min(maxQueue, SurfaceOpaqueMaxRenderQueue);
            surfaceOpaqueFilteringEnabled = surfaceOpaqueMaxQueue >= minQueue;
            var surfaceOpaqueRenderQueueRange = new RenderQueueRange
            {
                lowerBound = minQueue,
                upperBound = surfaceOpaqueFilteringEnabled ? surfaceOpaqueMaxQueue : minQueue
            };
            surfaceOpaqueFilteringSettings = new FilteringSettings(surfaceOpaqueRenderQueueRange, surfaceOpaqueFilteringEnabled ? layerMask : 0);

            int surfaceTransparentMinQueue = Mathf.Max(minQueue, SurfaceOpaqueMaxRenderQueue + 1);
            surfaceTransparentFilteringEnabled = maxQueue >= surfaceTransparentMinQueue;
            var surfaceTransparentRenderQueueRange = new RenderQueueRange
            {
                lowerBound = surfaceTransparentMinQueue,
                upperBound = surfaceTransparentFilteringEnabled ? maxQueue : surfaceTransparentMinQueue
            };
            surfaceTransparentFilteringSettings = new FilteringSettings(surfaceTransparentRenderQueueRange, surfaceTransparentFilteringEnabled ? layerMask : 0);
        }

        private static RendererListHandle CreateRendererList(
            RenderGraph renderGraph,
            CullingResults cullingResults,
            DrawingSettings drawingSettings,
            FilteringSettings filteringSettings,
            RenderStateBlock stateBlock)
        {
            var tagValues = new NativeArray<ShaderTagId>(1, Allocator.Temp);
            var stateBlocks = new NativeArray<RenderStateBlock>(1, Allocator.Temp);
            try
            {
                tagValues[0] = ShaderTagId.none;
                stateBlocks[0] = stateBlock;
                var rendererListParams = new RendererListParams(cullingResults, drawingSettings, filteringSettings)
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

        private static TextureDesc CreateTextureDesc(RenderTextureDescriptor cameraTextureDescriptor, GraphicsFormat format, string name, int samples = 1)
        {
            var descriptor = new TextureDesc(Mathf.Max(1, cameraTextureDescriptor.width), Mathf.Max(1, cameraTextureDescriptor.height))
            {
                name = name,
                format = format != GraphicsFormat.None ? format : cameraTextureDescriptor.graphicsFormat,
                dimension = cameraTextureDescriptor.dimension,
                slices = cameraTextureDescriptor.volumeDepth,
                depthBufferBits = 0,
                msaaSamples = samples > 1 ? (MSAASamples)samples : MSAASamples.None,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                bindTextureMS = samples > 1,
                useDynamicScale = cameraTextureDescriptor.useDynamicScale,
                useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit,
                vrUsage = cameraTextureDescriptor.vrUsage
            };
            return descriptor;
        }

        private static TextureDesc CreateDepthTextureDesc(RenderTextureDescriptor cameraTextureDescriptor, int samples, bool bindMS, string name)
        {
            var descriptor = new TextureDesc(Mathf.Max(1, cameraTextureDescriptor.width), Mathf.Max(1, cameraTextureDescriptor.height))
            {
                name = name,
                format = GraphicsFormat.None,
                depthBufferBits = DepthBits.Depth32,
                dimension = cameraTextureDescriptor.dimension,
                slices = cameraTextureDescriptor.volumeDepth,
                msaaSamples = samples > 1 ? (MSAASamples)samples : MSAASamples.None,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                bindTextureMS = bindMS && samples > 1,
                useDynamicScale = cameraTextureDescriptor.useDynamicScale,
                useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit,
                vrUsage = cameraTextureDescriptor.vrUsage
            };
            return descriptor;
        }

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-Character-Buffer Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ActiveId, 0.0f);
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, 0.0f);
                });
            }
        }
    }
}
