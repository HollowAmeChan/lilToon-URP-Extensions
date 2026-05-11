using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.OIT
{
    [DisallowMultipleRendererFeature("lilToon Weighted OIT")]
    public sealed class WeightedOITRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private WeightedOITSettings settings = new WeightedOITSettings();

        private WeightedOITClearPass clearPass;
        private WeightedOITAccumulationPass accumulationPass;
        private WeightedOITCompositePass compositePass;
        private readonly WeightedOITRenderTargets renderTargets = new WeightedOITRenderTargets();
        private Material compositeMaterial;

        public WeightedOITSettings Settings => settings;

        public override void Create()
        {
            clearPass = new WeightedOITClearPass();
            accumulationPass = new WeightedOITAccumulationPass();
            compositePass = new WeightedOITCompositePass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            clearPass.Setup(settings, renderTargets);
            accumulationPass.Setup(settings, renderTargets, renderer.cameraDepthTargetHandle);
            compositePass.Setup(settings, renderer.cameraColorTargetHandle, renderTargets, compositeMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterial();
            clearPass.Setup(settings, renderTargets);

            renderer.EnqueuePass(clearPass);
            renderer.EnqueuePass(accumulationPass);
            renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing)
        {
            renderTargets.Release();
            CoreUtils.Destroy(compositeMaterial);
            clearPass = null;
            accumulationPass = null;
            compositePass = null;
            compositeMaterial = null;
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
                Debug.LogWarning($"lilToon Weighted OIT could not find composite shader '{WeightedOITShaderConstants.CompositeShaderName}'.");
                return;
            }

            compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed class WeightedOITClearPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Clear");
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
            ConfigureClear(ClearFlag.Color, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class WeightedOITAccumulationPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Accumulation");
        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId> { WeightedOITShaderConstants.ShaderTagId };
        private readonly RTHandle[] colorTargets = new RTHandle[2];
        private WeightedOITSettings settings;
        private FilteringSettings filteringSettings;
        private RenderStateBlock renderStateBlock;
        private WeightedOITRenderTargets renderTargets;
        private RTHandle cameraDepthTarget;

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

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            renderTargets.ReAllocateIfNeeded(cameraTextureDescriptor, settings);
            colorTargets[0] = renderTargets.AccumulationTexture;
            colorTargets[1] = renderTargets.RevealageTexture;
            ConfigureTarget(colorTargets, cameraDepthTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortingCriteria);
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalFloat("_lilOITWeight", settings.weight);
                cmd.SetGlobalFloat("_lilOITAlphaClipThreshold", settings.alphaClipThreshold);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class WeightedOITCompositePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Composite");
        private RTHandle cameraColorTarget;
        private WeightedOITRenderTargets renderTargets;
        private Material compositeMaterial;

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

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (compositeMaterial == null)
            {
                return;
            }

            if (renderTargets.AccumulationTexture == null || renderTargets.RevealageTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalTexture(WeightedOITShaderConstants.AccumulationTextureId, renderTargets.AccumulationTexture.nameID);
                cmd.SetGlobalTexture(WeightedOITShaderConstants.RevealageTextureId, renderTargets.RevealageTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, cameraColorTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, compositeMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    internal sealed class WeightedOITRenderTargets
    {
        public RTHandle AccumulationTexture { get; private set; }

        public RTHandle RevealageTexture { get; private set; }

        public void ReAllocateIfNeeded(RenderTextureDescriptor cameraTextureDescriptor, WeightedOITSettings settings)
        {
            int divisor = Mathf.Max(1, (int)settings.renderScale);
            RenderTextureDescriptor accumulationDescriptor = cameraTextureDescriptor;
            accumulationDescriptor.depthBufferBits = 0;
            accumulationDescriptor.msaaSamples = 1;
            accumulationDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            accumulationDescriptor.width = Mathf.Max(1, accumulationDescriptor.width / divisor);
            accumulationDescriptor.height = Mathf.Max(1, accumulationDescriptor.height / divisor);

            RenderTextureDescriptor revealageDescriptor = accumulationDescriptor;
            revealageDescriptor.graphicsFormat = GraphicsFormat.R8_UNorm;

            RenderingUtils.ReAllocateIfNeeded(ref AccumulationTexture, accumulationDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.AccumulationTextureName);
            RenderingUtils.ReAllocateIfNeeded(ref RevealageTexture, revealageDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.RevealageTextureName);
        }

        public void Release()
        {
            AccumulationTexture?.Release();
            RevealageTexture?.Release();
            AccumulationTexture = null;
            RevealageTexture = null;
        }
    }
}
