using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ImageProcessPass : ScriptableRenderPass
    {
        private const float LogoOverlayInitMarker = -12347.0f;

        private readonly List<ImageProcessRuntimeLayer> runtimeLayers = new List<ImageProcessRuntimeLayer>();
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
        private readonly Dictionary<int, ChangeFrameRateState> changeFrameRateStates = new Dictionary<int, ChangeFrameRateState>();

        public ImageProcessPass(string passName)
        {
            _passName = passName;
            _profilingSampler = new ProfilingSampler(passName);
        }

        public void Setup(
            RTHandle cameraColorTarget,
            List<ImageProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            this.cameraColorTarget = cameraColorTarget;
            CopyLayers(layers);
            ConfigurePass(passEvent);
            requiresIntermediateTexture = true;
        }

        public void SetupRenderGraph(
            List<ImageProcessRuntimeLayer> layers,
            RenderPassEvent passEvent)
        {
            this.cameraColorTarget = null;
            ReleaseCompatibilityResources();
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
            ReleaseCompatibilityResources(true);
            foreach (ChangeFrameRateState state in changeFrameRateStates.Values)
            {
                state.Release();
            }

            changeFrameRateStates.Clear();
            Shader.SetGlobalTexture(ImageProcessShaderConstants.FrozenFrameTexId, Texture2D.blackTexture);
            cameraColorTarget = null;
        }

        private static void ReleaseRTHandle(ref RTHandle handle)
        {
            handle?.Release();
            handle = null;
        }

        private void ReleaseCompatibilityResources(bool forceResetGlobalTextures = false)
        {
            bool hadCompatibilityResources = tempTextureA != null
                || tempTextureB != null
                || irisTextureA != null
                || irisTextureB != null
                || rgbBlurTextureA != null
                || rgbBlurTextureB != null
                || glowTextureA != null
                || glowTextureB != null
                || apertureBokehTextureA != null
                || apertureBokehTextureB != null;

            ReleaseRTHandle(ref tempTextureA);
            ReleaseRTHandle(ref tempTextureB);
            ReleaseRTHandle(ref irisTextureA);
            ReleaseRTHandle(ref irisTextureB);
            ReleaseRTHandle(ref rgbBlurTextureA);
            ReleaseRTHandle(ref rgbBlurTextureB);
            ReleaseRTHandle(ref glowTextureA);
            ReleaseRTHandle(ref glowTextureB);
            ReleaseRTHandle(ref apertureBokehTextureA);
            ReleaseRTHandle(ref apertureBokehTextureB);
            if (forceResetGlobalTextures || hadCompatibilityResources)
            {
                ResetCompatibilityGlobalTextures();
            }
        }

        private static void ResetCompatibilityGlobalTextures()
        {
            Texture fallback = Texture2D.blackTexture;
            Shader.SetGlobalTexture(ImageProcessShaderConstants.OriginalTexId, fallback);
            Shader.SetGlobalTexture(ImageProcessShaderConstants.BlurredTexId, fallback);
            Shader.SetGlobalTexture(ImageProcessShaderConstants.BloomTexId, fallback);
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
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureA, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ImageProcessShaderConstants.TempTextureAName);
            RenderingUtils.ReAllocateIfNeeded(ref tempTextureB, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: ImageProcessShaderConstants.TempTextureBName);
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
                    ImageProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
                    RTHandle destination = writeToA ? tempTextureA : tempTextureB;

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
                    Debug.LogWarning($"{_passName} skipped because the active color target is the backbuffer. The ImageProcess post process stack requires an intermediate color texture.");
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
                ImageProcessRuntimeLayer runtimeLayer = runtimeLayers[i];
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

        private void CopyLayers(List<ImageProcessRuntimeLayer> layers)
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

    }
}
