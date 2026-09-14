#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterBuffer
{
    /// <summary>
    /// 调试视图：把 CB 的通道画到相机颜色上（与 MetadataBuffer 的调试 pass 同形）。
    /// 没产出时 shader 会输出暗红，而不是静默黑屏——"没跑"和"全是背景"必须能分开。
    /// </summary>
    internal sealed class HoCharacterBufferDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-CharacterBuffer Debug");

        private HoCharacterBufferSettings settings;
        private HoCharacterBufferRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private Material debugMaterial;

        private sealed class PassData
        {
            public Material debugMaterial;
            public HoCharacterBufferDebugMode debugMode;
            public TextureHandle id0Texture;
            public TextureHandle id1Texture;
            public TextureHandle coverageTexture;
            public TextureHandle surfaceTexture;
            public TextureHandle material0Texture;
            public TextureHandle selectionTexture;
        }

        public void Setup(
            HoCharacterBufferSettings settings,
            HoCharacterBufferRenderTargets renderTargets,
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
                debugMaterial.SetFloat(HoCharacterBufferShaderConstants.DebugModeId, (float)settings.debugMode);
                cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, renderTargets.Id0Texture != null ? 1.0f : 0.0f);
                if (renderTargets.Id0Texture != null)
                {
                    cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id0TextureId, renderTargets.Id0Texture.nameID);
                    cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id1TextureId, renderTargets.Id1Texture.nameID);
                    cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
                    cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SurfaceTextureId, renderTargets.SurfaceTexture.nameID);
                    if (renderTargets.Material0Texture != null)
                    {
                        cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Material0TextureId, renderTargets.Material0Texture.nameID);
                    }

                    if (renderTargets.SelectionTexture != null)
                    {
                        cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SelectionTextureId, renderTargets.SelectionTexture.nameID);
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
            HoCharacterBufferRenderGraphResources resources = frameData.GetOrCreate<HoCharacterBufferRenderGraphResources>();
            TextureHandle destination = resourceData.activeColorTexture;

            if (!destination.IsValid() || !resources.HasRequiredTextures)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-Character-Buffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.id0Texture = resources.id0Texture;
                passData.id1Texture = resources.id1Texture;
                passData.coverageTexture = resources.coverageTexture;
                passData.surfaceTexture = resources.surfaceTexture;
                passData.material0Texture = resources.material0Texture;
                passData.selectionTexture = resources.selectionTexture;

                builder.UseTexture(resources.id0Texture, AccessFlags.Read);
                builder.UseTexture(resources.id1Texture, AccessFlags.Read);
                builder.UseTexture(resources.coverageTexture, AccessFlags.Read);
                if (resources.surfaceTexture.IsValid())
                {
                    builder.UseTexture(resources.surfaceTexture, AccessFlags.Read);
                }

                if (resources.material0Texture.IsValid())
                {
                    builder.UseTexture(resources.material0Texture, AccessFlags.Read);
                }

                if (resources.selectionTexture.IsValid())
                {
                    builder.UseTexture(resources.selectionTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoCharacterBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    context.cmd.SetGlobalFloat(HoCharacterBufferShaderConstants.ValidId, 1.0f);
                    context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id0TextureId, data.id0Texture);
                    context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Id1TextureId, data.id1Texture);
                    context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.CoverageTextureId, data.coverageTexture);
                    if (data.surfaceTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SurfaceTextureId, data.surfaceTexture);
                    }

                    if (data.material0Texture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.Material0TextureId, data.material0Texture);
                    }

                    if (data.selectionTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoCharacterBufferShaderConstants.SelectionTextureId, data.selectionTexture);
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
