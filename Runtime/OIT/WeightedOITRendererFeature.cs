using System.Collections.Generic;
// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.OIT
{
    [DisallowMultipleRendererFeature("Ho-WeightedOIT")]
    public sealed class WeightedOITRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private WeightedOITSettings settings = new WeightedOITSettings();

        private WeightedOITResetPass resetPass;
        private WeightedOITClearPass clearPass;
        private WeightedOITOpaqueCopyPass opaqueCopyPass;
        private WeightedOITAccumulationPass accumulationPass;
        private WeightedOITCompositePass compositePass;
        private readonly WeightedOITRenderTargets renderTargets = new WeightedOITRenderTargets();
        private Material compositeMaterial;
        private bool registeredCameraReset;

        public WeightedOITSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            resetPass = new WeightedOITResetPass();
            clearPass = new WeightedOITClearPass();
            opaqueCopyPass = new WeightedOITOpaqueCopyPass();
            accumulationPass = new WeightedOITAccumulationPass();
            compositePass = new WeightedOITCompositePass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                renderTargets.Release();
                return;
            }

            clearPass.Setup(settings, renderTargets);
            opaqueCopyPass.Setup(settings, renderer.cameraColorTargetHandle, renderTargets);
            accumulationPass.Setup(settings, renderTargets, renderer.cameraDepthTargetHandle);
            compositePass.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, compositeMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview)
            {
                resetPass.Setup();
                renderer.EnqueuePass(resetPass);
                return;
            }

            if (!ShouldRender(in renderingData))
            {
                renderTargets.Release();
                return;
            }

            EnsureMaterial();
            if (compositeMaterial == null)
            {
                renderTargets.Release();
                return;
            }

            clearPass.Setup(settings, renderTargets);
            opaqueCopyPass.SetupRenderGraph(settings, renderTargets);
            accumulationPass.SetupRenderGraph(settings, renderTargets);
            compositePass.SetupRenderGraph(settings, renderTargets, compositeMaterial);

            renderer.EnqueuePass(clearPass);
            renderer.EnqueuePass(opaqueCopyPass);
            renderer.EnqueuePass(accumulationPass);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            CoreUtils.Destroy(compositeMaterial);
            resetPass = null;
            clearPass = null;
            opaqueCopyPass = null;
            accumulationPass = null;
            compositePass = null;
            compositeMaterial = null;
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetOITState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetOITState;
            registeredCameraReset = false;
        }

        private static void ResetOITState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private void EnsureMaterial()
        {
            if (compositeMaterial != null)
            {
                return;
            }

            Shader shader = settings.compositeShader != null
                ? settings.compositeShader
                : Shader.Find(WeightedOITShaderConstants.CompositeShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"Ho-WeightedOIT could not find composite shader '{WeightedOITShaderConstants.CompositeShaderName}'.");
                return;
            }

            compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed class WeightedOITResetPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-WeightedOIT Reset");

        private sealed class PassData
        {
        }

        public void Setup()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-WeightedOIT Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
                });
            }
        }
    }

    internal sealed class WeightedOITOpaqueCopyPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-WeightedOIT OpaqueCopy");
        private RTHandle cameraColorTarget;
        private WeightedOITRenderTargets renderTargets;

        private sealed class PassData
        {
            public TextureHandle source;
            public Vector4 texelSize;
        }

        public void Setup(WeightedOITSettings settings, RTHandle cameraColorTarget, WeightedOITRenderTargets renderTargets)
        {
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            renderPassEvent = GetOpaqueCopyPassEvent(settings.accumulationPassEvent);
        }

        public void SetupRenderGraph(WeightedOITSettings settings, WeightedOITRenderTargets renderTargets)
        {
            this.renderTargets = renderTargets;
            renderPassEvent = GetOpaqueCopyPassEvent(settings.accumulationPassEvent);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTargets.ReAllocateOpaqueTexture(renderingData.cameraData.cameraTargetDescriptor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.OpaqueTexture, 0, true);
                SetCameraOpaqueTexture(cmd, renderTargets.OpaqueTexture);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            cameraColorTarget = null;
            renderTargets?.Release();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            WeightedOITRenderGraphResources oitResources = frameData.GetOrCreate<WeightedOITRenderGraphResources>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = source.GetDescriptor(renderGraph);
            destinationDesc.name = WeightedOITShaderConstants.OpaqueTextureName;
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            destinationDesc.msaaSamples = MSAASamples.None;
            destinationDesc.filterMode = FilterMode.Bilinear;
            destinationDesc.wrapMode = TextureWrapMode.Clamp;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            oitResources.opaqueTexture = destination;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-WeightedOIT OpaqueCopy", out var passData, ProfilingSampler))
            {
                passData.source = source;
                passData.texelSize = GetTexelSize(destinationDesc);
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetGlobalTextureAfterPass(destination, WeightedOITShaderConstants.OpaqueTextureId);
                builder.SetGlobalTextureAfterPass(destination, WeightedOITShaderConstants.CameraOpaqueTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0.0f, false);
                    context.cmd.SetGlobalVector(WeightedOITShaderConstants.CameraOpaqueTextureTexelSizeId, data.texelSize);
                });
            }
        }

        private static RenderPassEvent GetOpaqueCopyPassEvent(RenderPassEvent accumulationPassEvent)
        {
            int afterSkybox = (int)RenderPassEvent.AfterRenderingSkybox + 1;
            int accumulation = (int)accumulationPassEvent;
            if (accumulation <= afterSkybox)
            {
                return accumulationPassEvent;
            }

            int beforeAccumulation = accumulation - 1;
            return (RenderPassEvent)Mathf.Max(afterSkybox, beforeAccumulation);
        }

        public static void SetCameraOpaqueTexture(CommandBuffer cmd, RTHandle opaqueTexture)
        {
            if (opaqueTexture == null)
            {
                return;
            }

            cmd.SetGlobalTexture(WeightedOITShaderConstants.OpaqueTextureId, opaqueTexture.nameID);
            cmd.SetGlobalTexture(WeightedOITShaderConstants.CameraOpaqueTextureId, opaqueTexture.nameID);

            RenderTexture rt = opaqueTexture.rt;
            if (rt != null)
            {
                cmd.SetGlobalVector(
                    WeightedOITShaderConstants.CameraOpaqueTextureTexelSizeId,
                    new Vector4(1.0f / rt.width, 1.0f / rt.height, rt.width, rt.height));
            }
        }

        private static Vector4 GetTexelSize(RTHandle texture)
        {
            RenderTexture rt = texture != null ? texture.rt : null;
            return rt != null
                ? new Vector4(1.0f / rt.width, 1.0f / rt.height, rt.width, rt.height)
                : Vector4.zero;
        }

        private static Vector4 GetTexelSize(TextureDesc textureDesc)
        {
            return textureDesc.width > 0 && textureDesc.height > 0
                ? new Vector4(1.0f / textureDesc.width, 1.0f / textureDesc.height, textureDesc.width, textureDesc.height)
                : Vector4.zero;
        }
    }

    internal sealed class WeightedOITClearPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-WeightedOIT Clear");
        private readonly RTHandle[] colorTargets = new RTHandle[2];
        private WeightedOITSettings settings;
        private WeightedOITRenderTargets renderTargets;

        public void Setup(WeightedOITSettings settings, WeightedOITRenderTargets renderTargets)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            renderPassEvent = settings.accumulationPassEvent;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings);
            colorTargets[0] = renderTargets.AccumulationTexture;
            colorTargets[1] = renderTargets.RevealageTexture;
            ConfigureTarget(colorTargets);
            ConfigureClear(ClearFlag.None, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                CoreUtils.SetRenderTarget(cmd, renderTargets.AccumulationTexture, ClearFlag.Color, Color.clear);
                CoreUtils.SetRenderTarget(cmd, renderTargets.RevealageTexture, ClearFlag.Color, Color.white);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            renderTargets?.Release();
            for (int i = 0; i < colorTargets.Length; i++)
            {
                colorTargets[i] = null;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            WeightedOITRenderGraphResources oitResources = frameData.GetOrCreate<WeightedOITRenderGraphResources>();

            TextureDesc accumulationDesc = WeightedOITRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                GraphicsFormat.R16G16B16A16_SFloat,
                WeightedOITShaderConstants.AccumulationTextureName,
                Color.clear);
            TextureDesc revealageDesc = WeightedOITRenderGraphResources.CreateDescriptor(
                cameraData.cameraTargetDescriptor,
                settings,
                GraphicsFormat.R8_UNorm,
                WeightedOITShaderConstants.RevealageTextureName,
                Color.white);

            TextureHandle accumulation = renderGraph.CreateTexture(accumulationDesc);
            TextureHandle revealage = renderGraph.CreateTexture(revealageDesc);
            oitResources.accumulationTexture = accumulation;
            oitResources.revealageTexture = revealage;
        }
    }

    internal sealed class WeightedOITAccumulationPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-WeightedOIT Accumulation");
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId> { WeightedOITShaderConstants.ShaderTagId };
        private readonly RTHandle[] colorTargets = new RTHandle[2];
        private WeightedOITSettings settings;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;
        private WeightedOITRenderTargets renderTargets;
        private RTHandle cameraDepthTarget;

        private sealed class PassData
        {
            public RendererListHandle rendererList;
            public TextureHandle opaqueTexture;
            public float weight;
            public float alphaClipThreshold;
        }

        public WeightedOITAccumulationPass()
        {
            renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        }

        public void Setup(WeightedOITSettings settings, WeightedOITRenderTargets renderTargets, RTHandle cameraDepthTarget)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraDepthTarget = cameraDepthTarget;
            RenderQueueRange renderQueueRange = new RenderQueueRange
            {
                lowerBound = settings.minRenderQueue,
                upperBound = settings.maxRenderQueue
            };
            filteringSettings = new FilteringSettings(renderQueueRange, settings.layerMask);
            renderPassEvent = settings.accumulationPassEvent;
        }

        public void SetupRenderGraph(WeightedOITSettings settings, WeightedOITRenderTargets renderTargets)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            RenderQueueRange renderQueueRange = new RenderQueueRange
            {
                lowerBound = settings.minRenderQueue,
                upperBound = settings.maxRenderQueue
            };
            filteringSettings = new FilteringSettings(renderQueueRange, settings.layerMask);
            renderPassEvent = settings.accumulationPassEvent;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings);
            colorTargets[0] = renderTargets.AccumulationTexture;
            colorTargets[1] = renderTargets.RevealageTexture;
            if (CanUseDepthTarget(renderTargets.AccumulationTexture, cameraDepthTarget))
            {
                ConfigureTarget(colorTargets, cameraDepthTarget);
            }
            else
            {
                ConfigureTarget(colorTargets);
            }
        }

        private static bool CanUseDepthTarget(RTHandle colorTarget, RTHandle depthTarget)
        {
            RenderTexture color = colorTarget != null ? colorTarget.rt : null;
            RenderTexture depth = depthTarget != null ? depthTarget.rt : null;
            if (color == null || depth == null)
            {
                return false;
            }

            return color.width == depth.width &&
                   color.height == depth.height &&
                   color.volumeDepth == depth.volumeDepth &&
                   color.antiAliasing == depth.antiAliasing;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortingCriteria);
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 1.0f);
                cmd.SetGlobalFloat("_lilOITWeight", settings.weight);
                cmd.SetGlobalFloat("_lilOITAlphaClipThreshold", settings.alphaClipThreshold);
                WeightedOITOpaqueCopyPass.SetCameraOpaqueTexture(cmd, renderTargets.OpaqueTexture);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            cameraDepthTarget = null;
            renderTargets?.Release();
            for (int i = 0; i < colorTargets.Length; i++)
            {
                colorTargets[i] = null;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            WeightedOITRenderGraphResources oitResources = frameData.GetOrCreate<WeightedOITRenderGraphResources>();

            TextureHandle accumulation = oitResources.accumulationTexture;
            TextureHandle revealage = oitResources.revealageTexture;
            if (!accumulation.IsValid() || !revealage.IsValid())
            {
                return;
            }

            TextureHandle opaque = oitResources.opaqueTexture;

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                shaderTagIds,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            RendererListParams rendererListParams = new RendererListParams(
                renderingData.cullResults,
                drawingSettings,
                filteringSettings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-WeightedOIT Accumulation", out var passData, ProfilingSampler))
            {
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);
                passData.opaqueTexture = opaque;
                passData.weight = settings.weight;
                passData.alphaClipThreshold = settings.alphaClipThreshold;

                if (!passData.rendererList.IsValid())
                {
                    return;
                }

                builder.UseRendererList(passData.rendererList);
                if (opaque.IsValid())
                {
                    builder.UseTexture(opaque, AccessFlags.Read);
                }

                builder.SetRenderAttachment(accumulation, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachment(revealage, 1, AccessFlags.ReadWrite);
                if (CanUseDepthTarget(renderGraph, accumulation, resourceData.activeDepthTexture, settings))
                {
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                }

                builder.SetGlobalTextureAfterPass(accumulation, WeightedOITShaderConstants.AccumulationTextureId);
                builder.SetGlobalTextureAfterPass(revealage, WeightedOITShaderConstants.RevealageTextureId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 1.0f);
                    context.cmd.SetGlobalFloat("_lilOITWeight", data.weight);
                    context.cmd.SetGlobalFloat("_lilOITAlphaClipThreshold", data.alphaClipThreshold);
                    if (data.opaqueTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(WeightedOITShaderConstants.OpaqueTextureId, data.opaqueTexture);
                        context.cmd.SetGlobalTexture(WeightedOITShaderConstants.CameraOpaqueTextureId, data.opaqueTexture);
                    }
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }
        }

        private static bool CanUseDepthTarget(RenderGraph renderGraph, TextureHandle colorTarget, TextureHandle depthTarget, WeightedOITSettings settings)
        {
            if (settings.renderScale != WeightedOITRenderScale.Full || !colorTarget.IsValid() || !depthTarget.IsValid())
            {
                return false;
            }

            TextureDesc colorDesc = colorTarget.GetDescriptor(renderGraph);
            TextureDesc depthDesc = depthTarget.GetDescriptor(renderGraph);
            return colorDesc.width == depthDesc.width &&
                   colorDesc.height == depthDesc.height &&
                   colorDesc.slices == depthDesc.slices &&
                   colorDesc.msaaSamples == depthDesc.msaaSamples;
        }
    }

    internal sealed class WeightedOITCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-WeightedOIT Composite");
        private RTHandle cameraColorTarget;
        private WeightedOITRenderTargets renderTargets;
        private Material compositeMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle accumulationTexture;
            public TextureHandle revealageTexture;
            public Material compositeMaterial;
        }

        private sealed class ResetPassData
        {
        }

        public void Setup(
            WeightedOITSettings settings,
            RTHandle cameraColorTarget,
            WeightedOITRenderTargets renderTargets,
            Material compositeMaterial)
        {
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.compositeMaterial = compositeMaterial;
            renderPassEvent = settings.compositePassEvent;
        }

        public void SetupRenderGraph(
            WeightedOITSettings settings,
            WeightedOITRenderTargets renderTargets,
            Material compositeMaterial)
        {
            this.renderTargets = renderTargets;
            this.compositeMaterial = compositeMaterial;
            renderPassEvent = settings.compositePassEvent;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            if (compositeMaterial == null)
            {
                cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            if (renderTargets.AccumulationTexture == null || renderTargets.RevealageTexture == null)
            {
                cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                renderTargets.ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.CompositeSourceTexture, 0, true);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
                Blitter.BlitCameraTexture(cmd, renderTargets.CompositeSourceTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, compositeMaterial, 0);
                cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            cameraColorTarget = null;
            renderTargets?.Release();
            if (compositeMaterial == null)
            {
                AddResetPass(renderGraph);
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            WeightedOITRenderGraphResources oitResources = frameData.GetOrCreate<WeightedOITRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle accumulation = oitResources.accumulationTexture;
            TextureHandle revealage = oitResources.revealageTexture;
            if (!source.IsValid() || !accumulation.IsValid() || !revealage.IsValid())
            {
                AddResetPass(renderGraph);
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_lilOITCompositeColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-WeightedOIT Composite", out var passData, ProfilingSampler))
            {
                passData.source = source;
                passData.accumulationTexture = accumulation;
                passData.revealageTexture = revealage;
                passData.compositeMaterial = compositeMaterial;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(accumulation, AccessFlags.Read);
                builder.UseTexture(revealage, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, data.accumulationTexture);
                    context.cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, data.revealageTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.compositeMaterial, 0);
                    context.cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void AddResetPass(RenderGraph renderGraph)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ResetPassData>("Ho-WeightedOIT Reset", out _, ProfilingSampler))
            {
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ResetPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(WeightedOITShaderConstants.OITActiveId, 0.0f);
                });
            }
        }
    }

    internal sealed class WeightedOITRenderGraphResources : ContextItem
    {
        public TextureHandle accumulationTexture = TextureHandle.nullHandle;
        public TextureHandle revealageTexture = TextureHandle.nullHandle;
        public TextureHandle opaqueTexture = TextureHandle.nullHandle;

        public override void Reset()
        {
            accumulationTexture = TextureHandle.nullHandle;
            revealageTexture = TextureHandle.nullHandle;
            opaqueTexture = TextureHandle.nullHandle;
        }

        public static TextureDesc CreateDescriptor(
            RenderTextureDescriptor cameraTextureDescriptor,
            WeightedOITSettings settings,
            GraphicsFormat format,
            string name,
            Color clearColor)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            TextureDesc descriptor = new TextureDesc(
                Mathf.Max(1, cameraTextureDescriptor.width / divisor),
                Mathf.Max(1, cameraTextureDescriptor.height / divisor));
            descriptor.name = name;
            descriptor.format = format;
            descriptor.dimension = cameraTextureDescriptor.dimension;
            descriptor.slices = cameraTextureDescriptor.volumeDepth;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = divisor == 1
                ? (MSAASamples)cameraTextureDescriptor.msaaSamples
                : MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = clearColor;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.bindTextureMS = cameraTextureDescriptor.bindMS && divisor == 1;
            descriptor.useDynamicScale = cameraTextureDescriptor.useDynamicScale;
            descriptor.useDynamicScaleExplicit = cameraTextureDescriptor.useDynamicScaleExplicit;
            descriptor.vrUsage = cameraTextureDescriptor.vrUsage;
            return descriptor;
        }
    }

    internal sealed class WeightedOITRenderTargets
    {
        private RTHandle accumulationTexture;
        private RTHandle revealageTexture;
        private RTHandle opaqueTexture;
        private RTHandle compositeSourceTexture;

        public RTHandle AccumulationTexture => accumulationTexture;

        public RTHandle RevealageTexture => revealageTexture;

        public RTHandle OpaqueTexture => opaqueTexture;

        public RTHandle CompositeSourceTexture => compositeSourceTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, WeightedOITSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor accumulationDescriptor = cameraTextureDescriptor;
            accumulationDescriptor.depthBufferBits = 0;
            accumulationDescriptor.depthStencilFormat = GraphicsFormat.None;
            accumulationDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            accumulationDescriptor.width = Mathf.Max(1, accumulationDescriptor.width / divisor);
            accumulationDescriptor.height = Mathf.Max(1, accumulationDescriptor.height / divisor);
            if (divisor > 1)
            {
                accumulationDescriptor.msaaSamples = 1;
            }

            RenderTextureDescriptor revealageDescriptor = accumulationDescriptor;
            revealageDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref accumulationTexture, accumulationDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.AccumulationTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref revealageTexture, revealageDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.RevealageTextureName);
        }

        public void ReAllocateOpaqueTexture(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
            cameraTextureDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraTextureDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref opaqueTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.OpaqueTextureName);
        }

        public void ReAllocateCompositeSource(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
            cameraTextureDescriptor.depthStencilFormat = GraphicsFormat.None;
            cameraTextureDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref compositeSourceTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.CompositeSourceTextureName);
        }

        public void Release()
        {
            accumulationTexture?.Release();
            revealageTexture?.Release();
            opaqueTexture?.Release();
            compositeSourceTexture?.Release();
            accumulationTexture = null;
            revealageTexture = null;
            opaqueTexture = null;
            compositeSourceTexture = null;
        }
    }
}
