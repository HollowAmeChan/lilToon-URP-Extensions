#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal sealed class HoShadowCastDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ShadowCast Debug");

        private HoShadowCastRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private HoShadowCastFrameConfig config;
        private Material debugMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle atlasTexture;
            public Material debugMaterial;
            public int debugMode;
        }

        public void Setup(HoShadowCastFrameConfig config, HoShadowCastRenderTargets renderTargets, RTHandle cameraColorTarget, Material debugMaterial)
        {
            this.config = config;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void SetupRenderGraph(HoShadowCastFrameConfig config, Material debugMaterial)
        {
            this.config = config;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void Dispose()
        {
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoShadowCastDebugSource");
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (debugMaterial == null || renderTargets == null || config == null || cameraColorTarget == null || tempTexture == null)
            {
                return;
            }

            RTHandle debugAtlas = config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas
                ? renderTargets.SecondDirectionalAtlasTexture
                : renderTargets.AtlasTexture;
            if (debugAtlas == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalInt(HoShadowCastShaderConstants.DebugModeId, (int)config.debugMode);
                if (config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas)
                {
                    cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, debugAtlas.nameID);
                }
                else
                {
                    cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, debugAtlas.nameID);
                }

                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, debugMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (config == null)
            {
                return;
            }

            TextureHandle atlas = config.debugMode == HoShadowCastDebugMode.SecondDirectionalAtlas
                ? shadowCastResources.secondDirectionalAtlasTexture
                : shadowCastResources.atlasTexture;
            if (!source.IsValid() || !atlas.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_HoShadowCastDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-ShadowCast Debug", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.atlasTexture = atlas;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = (int)config.debugMode;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(atlas, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalInt(HoShadowCastShaderConstants.DebugModeId, data.debugMode);
                    if (data.debugMode == (int)HoShadowCastDebugMode.SecondDirectionalAtlas)
                    {
                        context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, data.atlasTexture);
                    }
                    else
                    {
                        context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, data.atlasTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.debugMaterial, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
