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
                return;
            }

            EnsureMaterial();
            if (compositeMaterial == null)
            {
                return;
            }

            clearPass.Setup(settings, renderTargets);

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
                Debug.LogWarning($"lilToon Weighted OIT could not find composite shader '{WeightedOITShaderConstants.CompositeShaderName}'.");
                return;
            }

            compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
        }
    }

    internal sealed class WeightedOITResetPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Reset");

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
    }

    internal sealed class WeightedOITOpaqueCopyPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon Weighted OIT Opaque Copy");
        private RTHandle cameraColorTarget;
        private WeightedOITRenderTargets renderTargets;

        public void Setup(WeightedOITSettings settings, RTHandle cameraColorTarget, WeightedOITRenderTargets renderTargets)
        {
            this.cameraColorTarget = cameraColorTarget;
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
            cameraTextureDescriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref opaqueTexture, cameraTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: WeightedOITShaderConstants.OpaqueTextureName);
        }

        public void ReAllocateCompositeSource(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.depthBufferBits = 0;
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
