using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass : ScriptableRenderPass
    {
        private const float LogoOverlayInitMarker = -12347.0f;

        private readonly List<ShoostPostProcessRuntimeLayer> runtimeLayers = new List<ShoostPostProcessRuntimeLayer>();
        private readonly ProfilingSampler _profilingSampler;
        private readonly string _passName;
        private RTHandle cameraColorTarget;
        private RTHandle tempTextureA;
        private RTHandle tempTextureB;
        private RTHandle irisTextureA;
        private RTHandle irisTextureB;
        private RTHandle rgbBlurTextureA;
        private RTHandle rgbBlurTextureB;
        private RTHandle glowTextureA;
        private RTHandle glowTextureB;
        private RTHandle apertureBokehTextureA;
        private RTHandle apertureBokehTextureB;
        private bool warnedBackBuffer;
        private bool warnedLegacyAovMask;
        private readonly Dictionary<int, ChangeFrameRateState> changeFrameRateStates = new Dictionary<int, ChangeFrameRateState>();

        public ShoostPostProcessPass(string passName)
        {
            _passName = passName;
            _profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            RTHandle cameraColorTarget,
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            this.cameraColorTarget = cameraColorTarget;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(
            List<ShoostPostProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            this.cameraColorTarget = null;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            ReleaseRuntimeResources();
            runtimeLayers.Clear();
        }

        public void ClearRuntimeLayers()
        {
            runtimeLayers.Clear();
        }

        public void ReleaseRuntimeResources()
        {
            tempTextureA?.Release();
            tempTextureB?.Release();
            tempTextureA = null;
            tempTextureB = null;
            irisTextureA?.Release();
            irisTextureB?.Release();
            irisTextureA = null;
            irisTextureB = null;
            rgbBlurTextureA?.Release();
            rgbBlurTextureB?.Release();
            rgbBlurTextureA = null;
            rgbBlurTextureB = null;
            glowTextureA?.Release();
            glowTextureB?.Release();
            glowTextureA = null;
            glowTextureB = null;
            apertureBokehTextureA?.Release();
            apertureBokehTextureB?.Release();
            apertureBokehTextureA = null;
            apertureBokehTextureB = null;
            foreach (ChangeFrameRateState state in changeFrameRateStates.Values)
            {
                state.Release();
            }

            changeFrameRateStates.Clear();
            ResetGlobalTextures();
            cameraColorTarget = null;
        }

        private static void ResetGlobalTextures()
        {
            Texture fallback = Texture2D.blackTexture;
            Shader.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, fallback);
            Shader.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, fallback);
            Shader.SetGlobalTexture(ShoostPostProcessShaderConstants.BloomTexId, fallback);
            Shader.SetGlobalTexture(ShoostPostProcessShaderConstants.FrozenFrameTexId, fallback);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (runtimeLayers.Count == 0)
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref descriptor);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ShoostPostProcessShaderConstants.TempTextureBName);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (runtimeLayers.Count == 0 || cameraColorTarget == null || tempTextureA == null || tempTextureB == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, _profilingSampler))
            {
                RTHandle source = cameraColorTarget;
                bool writeToA = true;

                for (int i = 0; i < runtimeLayers.Count; i++)
                {
                    ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;
                    WarnIfImageProcessLayerUsesLegacyAovMask(runtimeLayer);

                    ExecuteEffectLayer(
                        cmd,
                        renderingData.cameraData.cameraTargetDescriptor,
                        renderingData.cameraData.camera,
                        source,
                        destination,
                        runtimeLayer);

                    source = destination;
                    writeToA = !writeToA;
                }

                Blitter.BlitCameraTexture(cmd, source, cameraColorTarget, 0, true);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (runtimeLayers.Count == 0)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
            {
                if (!warnedBackBuffer)
                {
                    Debug.LogWarning($"{_passName} skipped because the active color target is the backbuffer. The Shoost post process stack requires an intermediate color texture.");
                    warnedBackBuffer = true;
                }
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid())
            {
                return;
            }

            var imageChain = new ImageProcessChain();
            imageChain.Begin(renderGraph, source);
            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                ShoostPostProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                WarnIfImageProcessLayerRequestsSemanticInput(runtimeLayer);
                WarnIfImageProcessLayerUsesLegacyAovMask(runtimeLayer);
                ImageProcessPassContext passContext = imageChain.NextPass(renderGraph, i);
                TextureHandle effectResult = RecordEffectLayer(
                    passContext.RenderGraph,
                    passContext.Read,
                    passContext.Write,
                    runtimeLayer,
                    passContext.LayerIndex,
                    frameData);
                imageChain.Commit(effectResult);
            }

            resourceData.cameraColor = imageChain.Current;
        }

        private void CopyLayers(List<ShoostPostProcessRuntimeLayer> layers)
        {
            runtimeLayers.Clear();
            if (layers == null)
            {
                return;
            }

            runtimeLayers.AddRange(layers);
        }

        private void ConfigurePass(RenderPassEvent passEvent)
        {
            renderPassEvent = passEvent;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        private void WarnIfImageProcessLayerRequestsSemanticInput(ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ImageProcessResourceRequest[] requests = runtimeLayer?.descriptor.ResourceRequests;
            if (requests == null || requests.Length == 0)
            {
                return;
            }

            for (int i = 0; i < requests.Length; i++)
            {
                if (!requests[i].IsSemanticInput)
                {
                    continue;
                }

                ShoostPostProcessEffect effect = runtimeLayer.settings.effect;
                if (warnedSemanticInputEffects.Add(effect))
                {
                    Debug.LogWarning($"{_passName} ignored semantic resource request '{requests[i].Kind}' from ImageProcess effect '{effect}'. Move this effect to ScreenProcess.");
                }
            }
        }

        private void WarnIfImageProcessLayerUsesLegacyAovMask(ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer?.settings;
            if (layer == null || warnedLegacyAovMask || (!layer.useAovMask && !layer.debugAovMask))
            {
                return;
            }

            warnedLegacyAovMask = true;
            Debug.LogWarning($"{_passName} ignored legacy AOV mask settings on ImageProcess layer '{layer.effect}'. Move object/material/depth gated post effects to ScreenProcess.");
        }

    }
}
