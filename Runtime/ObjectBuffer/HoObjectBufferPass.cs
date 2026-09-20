using System.Collections.Generic;
#pragma warning disable CS0618, CS0672

using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// ID / 覆盖率的生产 pass（规划 §5.4）。
    /// <list type="bullet">
    /// <item>自建 MSAA：采样数来自 feature 设置，**与相机 MSAA 解耦**（决策 7）；</item>
    /// <item>N = 1 时直接写 4 层结果；N &gt; 1 时逐样本写一个 16 bit ID，再由 resolve 数票；</item>
    /// <item>**只管身份与覆盖率**：线性表面色、roughness / metallic / thickness 等表面数值已搬到
    /// `Ho-SurfaceBuffer`（决策 20），所以这里没有独立的材质 pass，也没有 `_Surface`；</item>
    /// <item>ID pass 的 depth-stencil 是内部附件：不发布、不给任何 shader 采样（决策 16）。</item>
    /// </list>
    /// </summary>
    internal sealed class HoObjectBufferPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ObjectBuffer Output");
        private static readonly ProfilingSampler ResolveProfilingSampler = new ProfilingSampler("Ho-ObjectBuffer MSAA Resolve");

        private static readonly List<ShaderTagId> FallbackShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private static readonly List<ShaderTagId> IdShaderTagIds = new List<ShaderTagId>
        {
            HoObjectBufferShaderConstants.ShaderTagId
        };

        private const int FallbackPassLayers = 0;
        private const int FallbackPassMsaaInt = 1;
        private const int FallbackPassMsaaUnorm = 2;

        /// <summary>override 材质看不到源材质的 alpha/cutout，所以 fallback 只碰不透明队列（与 MetadataBuffer 同一取舍）。</summary>
        private const int FallbackMaxRenderQueue = (int)RenderQueue.AlphaTest - 1;

        private readonly RTHandle[] idColorTargets = new RTHandle[3];
        private readonly RTHandle[] msaaColorTargets = new RTHandle[2];
        private readonly RenderTargetIdentifier[] resolveColorIdentifiers = new RenderTargetIdentifier[4];

        private HoObjectBufferSettings settings;
        private HoObjectBufferRenderTargets renderTargets;
        private Material fallbackMaterial;
        private Material resolveMaterial;
        private FilteringSettings fallbackFilteringSettings;
        private FilteringSettings idFilteringSettings;
        private bool fallbackFilteringEnabled;
        private int msaaSamples = 1;
        private bool selectionEnabled;
        // 注意：这两个状态块**不能是 readonly**——DrawRenderers 要的是 ref，
        // 而 readonly 字段不能当 ref 实参（CS0192）。
        private RenderStateBlock renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        private RenderStateBlock depthWriteStateBlock = new RenderStateBlock(RenderStateMask.Depth)
        {
            depthState = new DepthState(true, CompareFunction.LessEqual)
        };

        private sealed class IdPassData
        {
            public RendererListHandle fallbackRendererList;
            public RendererListHandle idRendererList;
            public bool drawFallback;
            public bool selectionEnabled;
            public bool useMsaa;
            public float selectionLayerCount;
            public float requestedSamples;
            public float actualSamples;
        }

        private sealed class ResolvePassData
        {
            public Material resolveMaterial;
            public int msaaSamples;
            public bool idFormatIsInteger;
            public bool selectionEnabled;
            public float selectionLayerCount;
            // resolve 的**输入**（MSAA 目标）：RenderGraph 路径必须在 resolve 里把它们绑成全局，
            // 否则 resolve shader 采样的是未绑定纹理 ⇒ 归约结果恒为 0 ⇒ 调试视图只剩背景灰。
            public TextureHandle idMsaaTexture;
            public TextureHandle depthMsaaTexture;
            public TextureHandle selectionMsaaTexture;
        }

        private sealed class ResetPassData
        {
        }
        public void Setup(
            HoObjectBufferSettings settings,
            HoObjectBufferRenderTargets renderTargets,
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

            HoObjectBufferRegistry.EnsureBuilt();
            UpdateDerivedState();
            msaaSamples = HoObjectBufferFormatUtility.GetSupportedSampleCount(
                cameraTextureDescriptor,
                settings.RequestedSampleCount,
                selectionEnabled);
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, msaaSamples, selectionEnabled);

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
                cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActiveId, 1.0f);
                cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionLayerCountId, selectionEnabled ? settings.RequestedSelectionLayerCount : 0);
                cmd.SetGlobalFloat(HoObjectBufferShaderConstants.RequestedSamplesId, settings.RequestedSampleCount);
                cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActualSamplesId, msaaSamples);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled)
                {
                    DrawingSettings fallbackDrawingSettings = CreateDrawingSettings(FallbackShaderTagIds, ref renderingData, SortingCriteria.CommonOpaque);
                    fallbackDrawingSettings.overrideMaterial = fallbackMaterial;
                    fallbackDrawingSettings.overrideMaterialPassIndex = GetFallbackPassIndex();
                    context.DrawRenderers(renderingData.cullResults, ref fallbackDrawingSettings, ref fallbackFilteringSettings, ref renderStateBlock);
                }

                if (renderTargets.UseMsaaResolve)
                {
                    DrawingSettings idDrawingSettings = CreateDrawingSettings(IdShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    context.DrawRenderers(renderingData.cullResults, ref idDrawingSettings, ref idFilteringSettings, ref renderStateBlock);
                }
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
            HoObjectBufferRegistry.EnsureBuilt();
            UpdateDerivedState();

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            HoObjectBufferRenderGraphResources resources = frameData.GetOrCreate<HoObjectBufferRenderGraphResources>();

            RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
            // 采样数**只问平台**：相机把 MSAA 关掉时，覆盖率照样是 4x（决策 7，也是最初那个 bug 的场景）。
            msaaSamples = HoObjectBufferFormatUtility.GetSupportedSampleCount(
                cameraDescriptor,
                settings.RequestedSampleCount,
                selectionEnabled);
            bool useMsaa = msaaSamples > 1;
            bool selection = selectionEnabled && HoObjectBufferRegistry.SelectionCount > 0;

            // 0.3.2：协商结果低于请求值时**必须看得见**，不静默降级。只在值变化时打一次。
            LastActualSamples = msaaSamples;
            if (msaaSamples != settings.RequestedSampleCount && msaaSamples != warnedSampleCount)
            {
                warnedSampleCount = msaaSamples;
                Debug.LogWarning(
                    $"[Ho-ObjectBuffer] 自建 MSAA 降级：请求 {settings.RequestedSampleCount}x，平台给到 {msaaSamples}x " +
                    "（覆盖率精度随之下调：1x 只有 0/1）。相机 AA 设置无关，这是平台/格式能力。");
            }

            TextureHandle id0Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoObjectBufferFormatUtility.GetLayerGraphicsFormat(), HoObjectBufferShaderConstants.Id0TextureName));
            TextureHandle id1Texture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoObjectBufferFormatUtility.GetLayerGraphicsFormat(), HoObjectBufferShaderConstants.Id1TextureName));
            TextureHandle coverageTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoObjectBufferFormatUtility.GetLayerGraphicsFormat(), HoObjectBufferShaderConstants.CoverageTextureName));
            // depth 纹理走 UniversalRenderer 的辅助函数（与 GB / MetadataBuffer 同一路径）：
            // 直接用 TextureDesc 造深度附件容易在 format/depthBufferBits 上写错。
            TextureHandle depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                HoObjectBufferFormatUtility.CreateDepthDescriptor(cameraDescriptor, 1, false),
                HoObjectBufferShaderConstants.Id0TextureName + "Depth",
                true,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            TextureHandle selectionTexture = TextureHandle.nullHandle;
            if (selection)
            {
                selectionTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoObjectBufferFormatUtility.GetLayerGraphicsFormat(), HoObjectBufferShaderConstants.SelectionTextureName));
            }

            resources.id0Texture = id0Texture;
            resources.id1Texture = id1Texture;
            resources.coverageTexture = coverageTexture;
            resources.selectionTexture = selectionTexture;
            resources.depthTexture = depthTexture;

            TextureHandle idMsaaTexture = TextureHandle.nullHandle;
            TextureHandle selectionMsaaTexture = TextureHandle.nullHandle;
            TextureHandle depthMsaaTexture = TextureHandle.nullHandle;
            if (useMsaa)
            {
                HoObjectBufferFormatUtility.TryGetIdGraphicsFormat(out GraphicsFormat idFormat, out _);
                idMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, idFormat, HoObjectBufferShaderConstants.Id0TextureName + "MSAA", msaaSamples));
                depthMsaaTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    HoObjectBufferFormatUtility.CreateDepthDescriptor(cameraDescriptor, msaaSamples, true),
                    HoObjectBufferShaderConstants.Id0TextureName + "DepthMSAA",
                    true,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                if (selection)
                {
                    selectionMsaaTexture = renderGraph.CreateTexture(CreateTextureDesc(cameraDescriptor, HoObjectBufferFormatUtility.GetLayerGraphicsFormat(), HoObjectBufferShaderConstants.SelectionTextureName + "MSAA", msaaSamples));
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

            using (var builder = renderGraph.AddRasterRenderPass<IdPassData>("Ho-Object-Buffer ID", out IdPassData passData, ProfilingSampler))
            {
                passData.drawFallback = settings.useFallbackMaterial && fallbackMaterial != null && fallbackFilteringEnabled;
                passData.fallbackRendererList = passData.drawFallback
                    ? CreateRendererList(renderGraph, renderingData.cullResults, fallbackDrawingSettings, fallbackFilteringSettings, renderStateBlock)
                    : default;
                // lilToon R1 的自有 pass 固定写 R16_UNorm 逐 sample ID。平台降级到 1x 时
                // 只跑 opaque fallback，避免把 scalar ID 错写进非 MSAA 的 RGBA8 层图。
                passData.idRendererList = useMsaa
                    ? CreateRendererList(renderGraph, renderingData.cullResults, idDrawingSettings, idFilteringSettings, renderStateBlock)
                    : default;
                passData.selectionEnabled = selection;
                passData.selectionLayerCount = selection ? settings.RequestedSelectionLayerCount : 0;
                passData.useMsaa = useMsaa;
                // 0.3.2：请求值与实际协商值都要发布，降级必须看得见（这里进全局量，
                // 「Sample Count」调试视图把它们画出来）。
                passData.requestedSamples = settings.RequestedSampleCount;
                passData.actualSamples = msaaSamples;

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
                    builder.SetRenderAttachment(idMsaaTexture, 0, AccessFlags.WriteAll);
                    if (selection && selectionMsaaTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionMsaaTexture, 1, AccessFlags.WriteAll);
                    }

                    builder.SetRenderAttachmentDepth(depthMsaaTexture, AccessFlags.WriteAll);
                }
                else
                {
                    builder.SetRenderAttachment(id0Texture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(id1Texture, 1, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(coverageTexture, 2, AccessFlags.WriteAll);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionTexture, 3, AccessFlags.WriteAll);
                    }

                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(id0Texture, HoObjectBufferShaderConstants.Id0TextureId);
                    builder.SetGlobalTextureAfterPass(id1Texture, HoObjectBufferShaderConstants.Id1TextureId);
                    builder.SetGlobalTextureAfterPass(coverageTexture, HoObjectBufferShaderConstants.CoverageTextureId);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetGlobalTextureAfterPass(selectionTexture, HoObjectBufferShaderConstants.SelectionTextureId);
                    }
                }

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (IdPassData data, RasterGraphContext context) =>
                {
                    // 自己清：附件用 WriteAll 时原生 pass 的 load action 是 DontCare（RG 不会替我们清），
                    // 而"没被任何 draw 覆盖的像素必须是 0"是这条通道的硬契约（0 = 背景）。
                    // GeometryBuffer 的输出 pass 也是这个写法，两边保持一致。
                    context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActiveId, 1.0f);
                    // shader 只声明"实际绑定"的 target 数：绑定选择层时才声明 SV_Target3/1，
                    // 否则声明的 SV_Target 索引会超过绑定数，D3D 会**丢弃整个 draw**。
                    // MSAA 下绑定的是逐样本身份图（1 个目标），材质侧的 pass 也要跟着换布局。
                    if (data.selectionEnabled)
                    {
                        Shader.EnableKeyword("_HO_OBJECT_BUFFER_SELECTION");
                    }
                    else
                    {
                        Shader.DisableKeyword("_HO_OBJECT_BUFFER_SELECTION");
                    }

                    if (data.useMsaa)
                    {
                        Shader.EnableKeyword("_HO_OBJECT_BUFFER_MSAA");
                    }
                    else
                    {
                        Shader.DisableKeyword("_HO_OBJECT_BUFFER_MSAA");
                    }

                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionLayerCountId, data.selectionLayerCount);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.RequestedSamplesId, data.requestedSamples);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActualSamplesId, data.actualSamples);
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
                using (var builder = renderGraph.AddRasterRenderPass<ResolvePassData>("Ho-Object-Buffer MSAA Resolve", out ResolvePassData passData, ResolveProfilingSampler))
                {
                    passData.resolveMaterial = resolveMaterial;
                    passData.msaaSamples = msaaSamples;
                    passData.idFormatIsInteger = IsIdFormatInteger();
                    passData.selectionEnabled = selection;
                    passData.selectionLayerCount = selection ? settings.RequestedSelectionLayerCount : 0;
                    passData.idMsaaTexture = idMsaaTexture;
                    passData.depthMsaaTexture = depthMsaaTexture;
                    passData.selectionMsaaTexture = selectionMsaaTexture;

                    builder.UseTexture(idMsaaTexture, AccessFlags.Read);
                    builder.UseTexture(depthMsaaTexture, AccessFlags.Read);
                    if (selection && selectionMsaaTexture.IsValid())
                    {
                        builder.UseTexture(selectionMsaaTexture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(id0Texture, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(id1Texture, 1, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(coverageTexture, 2, AccessFlags.WriteAll);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetRenderAttachment(selectionTexture, 3, AccessFlags.WriteAll);
                    }

                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(id0Texture, HoObjectBufferShaderConstants.Id0TextureId);
                    builder.SetGlobalTextureAfterPass(id1Texture, HoObjectBufferShaderConstants.Id1TextureId);
                    builder.SetGlobalTextureAfterPass(coverageTexture, HoObjectBufferShaderConstants.CoverageTextureId);
                    if (selection && selectionTexture.IsValid())
                    {
                        builder.SetGlobalTextureAfterPass(selectionTexture, HoObjectBufferShaderConstants.SelectionTextureId);
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
                        // resolve 的输入必须在这里绑成全局：RenderGraph 路径不会自动把 UseTexture 的句柄
                        // 暴露成全局名，少了这三行 resolve 就一直在采样未绑定纹理（归约出全 0）。
                        context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveIdTextureMsId, data.idMsaaTexture);
                        context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveDepthTextureMsId, data.depthMsaaTexture);
                        if (data.selectionMsaaTexture.IsValid())
                        {
                            context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveSelectionTextureMsId, data.selectionMsaaTexture);
                        }

                        context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActiveId, 1.0f);
                        context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionLayerCountId, data.selectionLayerCount);
                        // 全屏三角形本来就铺满，但显式清一次把它和"只画了一半"的失败模式区分开：
                        // 清理值就是契约里的"背景 = ID 0、覆盖率 0"。
                        context.cmd.ClearRenderTarget(RTClearFlags.ColorDepth, Color.clear, 1.0f, 0);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.resolveMaterial, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }

            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-Object-Buffer Valid", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, 1.0f);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.PartCountId, HoObjectBufferRegistry.PartRowCount);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionCountId, HoObjectBufferRegistry.SelectionCount);
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
            Shader.SetGlobalFloat(HoObjectBufferShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, 0.0f);
            Shader.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(HoObjectBufferShaderConstants.SelectionTextureId, Texture2D.blackTexture);
        }

        private void ResolveMsaa(CommandBuffer cmd)
        {
            if (resolveMaterial == null)
            {
                return;
            }

            SetResolveKeywords(resolveMaterial, msaaSamples, IsIdFormatInteger(), selectionEnabled);
            cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveIdTextureMsId, renderTargets.IdMsaaTexture.nameID);
            cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveDepthTextureMsId, renderTargets.DepthMsaaTexture.nameID);
            if (selectionEnabled && renderTargets.SelectionMsaaTexture != null)
            {
                cmd.SetGlobalTexture(HoObjectBufferShaderConstants.ResolveSelectionTextureMsId, renderTargets.SelectionMsaaTexture.nameID);
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

        private void PublishGlobals(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, 1.0f);
            cmd.SetGlobalFloat(HoObjectBufferShaderConstants.PartCountId, HoObjectBufferRegistry.PartRowCount);
            cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionCountId, HoObjectBufferRegistry.SelectionCount);
            cmd.SetGlobalFloat(HoObjectBufferShaderConstants.SelectionLayerCountId, selectionEnabled ? settings.RequestedSelectionLayerCount : 0);
            cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, renderTargets.Id0Texture.nameID);
            cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, renderTargets.Id1Texture.nameID);
            cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
            if (selectionEnabled && renderTargets.SelectionTexture != null)
            {
                cmd.SetGlobalTexture(HoObjectBufferShaderConstants.SelectionTextureId, renderTargets.SelectionTexture.nameID);
            }
        }

        private static bool? idFormatIsIntegerCache;

        /// <summary>本帧实际协商到的采样数（0.3.2 的 actual-N；降级可见的诊断/回归读它）。</summary>
        public static int LastActualSamples { get; private set; }

        private static int warnedSampleCount;

        /// <summary>逐样本 ID 目标是不是整数格式。格式支持在运行期不会变，缓存一次。</summary>
        private static bool IsIdFormatInteger()
        {
            if (!idFormatIsIntegerCache.HasValue)
            {
                idFormatIsIntegerCache = HoObjectBufferFormatUtility.TryGetIdGraphicsFormat(out _, out bool isInteger) && isInteger;
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

            material.DisableKeyword(HoObjectBufferShaderConstants.Msaa2Keyword);
            material.DisableKeyword(HoObjectBufferShaderConstants.Msaa4Keyword);
            material.EnableKeyword(samples <= 2 ? HoObjectBufferShaderConstants.Msaa2Keyword : HoObjectBufferShaderConstants.Msaa4Keyword);

            const string idUnormKeyword = "_HO_OBJECT_BUFFER_ID_UNORM";
            if (idFormatIsInteger)
            {
                material.DisableKeyword(idUnormKeyword);
            }
            else
            {
                material.EnableKeyword(idUnormKeyword);
            }

            const string selectionKeyword = "_HO_OBJECT_BUFFER_HAS_SELECTION";
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
                HoObjectBufferRegistry.SelectionCount > 0;
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

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-Object-Buffer Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ActiveId, 0.0f);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, 0.0f);
                });
            }
        }
    }
}
