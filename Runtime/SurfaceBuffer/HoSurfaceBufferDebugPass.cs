#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// SB 的调试直出：把五张数值图（与 owner 对齐）画到相机颜色上。
    /// **owner 视图是"对齐有没有出问题"的唯一入口**：绿 = owner 与 OB 层 0 一致，红 = 不一致（没人写也算）。
    /// </summary>
    internal sealed class HoSurfaceBufferDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SurfaceBuffer Debug");

        private HoSurfaceBufferSettings settings;
        private Material debugMaterial;
        private RTHandle cameraColorTarget;

        private sealed class PassData
        {
            public Material material;
            public HoSurfaceBufferDebugMode debugMode;
            public TextureHandle colorTexture;
            public TextureHandle normalTexture;
            public TextureHandle materialTexture;
            public TextureHandle reflectionTexture;
            public TextureHandle classificationTexture;
            public TextureHandle ownerTexture;
            /// <summary>语义 lane（单采样）：`SemanticOwner` / `SemanticLanes` 两个视图要读。</summary>
            public TextureHandle semanticOwnerTexture;
            public TextureHandle[] semanticLaneTextures;
        }

        public void Setup(HoSurfaceBufferSettings settings, Material debugMaterial, RTHandle cameraColorTarget)
        {
            this.settings = settings;
            this.debugMaterial = debugMaterial;
            this.cameraColorTarget = cameraColorTarget;
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
            if (cameraColorTarget != null)
            {
                ConfigureTarget(cameraColorTarget);
            }
        }

        public void ReleaseCompatibilityResources()
        {
            cameraColorTarget = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || debugMaterial == null || cameraColorTarget == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                debugMaterial.SetFloat(HoSurfaceBufferShaderConstants.DebugModeId, (float)settings.debugMode);
                CoreUtils.DrawFullScreen(cmd, debugMaterial, shaderPassId: 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || debugMaterial == null || settings.debugMode == HoSurfaceBufferDebugMode.Off)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoSurfaceBufferRenderGraphResources resources = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            TextureHandle destination = resourceData.activeColorTexture;
            if (!destination.IsValid() || !resources.HasRequiredTextures)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SurfaceBuffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.material = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.colorTexture = resources.colorTexture;
                passData.normalTexture = resources.normalTexture;
                passData.materialTexture = resources.materialTexture;
                passData.reflectionTexture = resources.reflectionTexture;
                passData.classificationTexture = resources.classificationTexture;
                passData.ownerTexture = resources.ownerTexture;
                // 语义 lane 的视图（mode 8/9）读的就是这几张：声明依赖，别靠"生产者刚好不会被裁"。
                passData.semanticOwnerTexture = resources.semanticOwnerTexture;
                passData.semanticLaneTextures = resources.semanticLaneTextures;

                builder.UseTexture(passData.colorTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalTexture, AccessFlags.Read);
                builder.UseTexture(passData.materialTexture, AccessFlags.Read);
                builder.UseTexture(passData.reflectionTexture, AccessFlags.Read);
                builder.UseTexture(passData.classificationTexture, AccessFlags.Read);
                builder.UseTexture(passData.ownerTexture, AccessFlags.Read);
                if (resources.HasSemanticLanes)
                {
                    builder.UseTexture(passData.semanticOwnerTexture, AccessFlags.Read);
                    for (int i = 0; i < passData.semanticLaneTextures.Length; i++)
                    {
                        builder.UseTexture(passData.semanticLaneTextures[i], AccessFlags.Read);
                    }
                }
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(HoSurfaceBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ColorTextureId, data.colorTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.NormalTextureId, data.normalTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.MaterialTextureId, data.materialTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ReflectionTextureId, data.reflectionTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ClassificationTextureId, data.classificationTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.OwnerTextureId, data.ownerTexture);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}
