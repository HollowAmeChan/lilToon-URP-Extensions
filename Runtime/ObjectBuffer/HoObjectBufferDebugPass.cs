#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ObjectBuffer
{
    /// <summary>
    /// 调试视图：把 CB 的通道画到相机颜色上（与 MetadataBuffer 的调试 pass 同形）。
    /// 没产出时 shader 会输出暗红，而不是静默黑屏——"没跑"和"全是背景"必须能分开。
    /// </summary>
    internal sealed class HoObjectBufferDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-ObjectBuffer Debug");

        private HoObjectBufferSettings settings;
        private HoObjectBufferRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private Material debugMaterial;

        private sealed class PassData
        {
            public Material debugMaterial;
            public HoObjectBufferDebugMode debugMode;
            public TextureHandle id0Texture;
            public TextureHandle id1Texture;
            public TextureHandle coverageTexture;
            public TextureHandle selectionTexture;
        }

        public void Setup(
            HoObjectBufferSettings settings,
            HoObjectBufferRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
            if (cameraColorTarget != null)
            {
                ConfigureTarget(cameraColorTarget);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || debugMaterial == null || renderTargets == null || cameraColorTarget == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                debugMaterial.SetFloat(HoObjectBufferShaderConstants.DebugModeId, (float)settings.debugMode);
                cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, renderTargets.Id0Texture != null ? 1.0f : 0.0f);
                if (renderTargets.Id0Texture != null)
                {
                    cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, renderTargets.Id0Texture.nameID);
                    cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, renderTargets.Id1Texture.nameID);
                    cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
                    if (renderTargets.SelectionTexture != null)
                    {
                        cmd.SetGlobalTexture(HoObjectBufferShaderConstants.SelectionTextureId, renderTargets.SelectionTexture.nameID);
                    }
                }

                CoreUtils.DrawFullScreen(cmd, debugMaterial, shaderPassId: 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoObjectBufferRenderGraphResources resources = frameData.GetOrCreate<HoObjectBufferRenderGraphResources>();
            TextureHandle destination = resourceData.activeColorTexture;

            if (!destination.IsValid() || !resources.HasRequiredTextures)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-Object-Buffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.id0Texture = resources.id0Texture;
                passData.id1Texture = resources.id1Texture;
                passData.coverageTexture = resources.coverageTexture;
                passData.selectionTexture = resources.selectionTexture;

                builder.UseTexture(resources.id0Texture, AccessFlags.Read);
                builder.UseTexture(resources.id1Texture, AccessFlags.Read);
                builder.UseTexture(resources.coverageTexture, AccessFlags.Read);
                if (resources.selectionTexture.IsValid())
                {
                    builder.UseTexture(resources.selectionTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoObjectBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    context.cmd.SetGlobalFloat(HoObjectBufferShaderConstants.ValidId, 1.0f);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.id0Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, data.id1Texture);
                    context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, data.coverageTexture);
                    if (data.selectionTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.SelectionTextureId, data.selectionTexture);
                    }

                    context.cmd.DrawProcedural(Matrix4x4.identity, data.debugMaterial, 0, MeshTopology.Triangles, 3, 1);
                });
            }
        }

        public void ReleaseCompatibilityResources()
        {
            cameraColorTarget = null;
        }
    }
}
