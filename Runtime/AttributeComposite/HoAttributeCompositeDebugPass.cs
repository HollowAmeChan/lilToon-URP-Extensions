#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.AttributeComposite
{
    /// <summary>
    /// AC 的调试直出：把 Selection 池画到相机颜色上（与 OB 的调试 pass 同形）。
    /// AC 架构 §12：没有 debug 视图与登记就不算落地 —— AC 至少要能看到合成语义槽。
    /// **这趟只做"看"，不产出任何东西**，所以它可以被随时关掉。
    /// </summary>
    internal sealed class HoAttributeCompositeDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-AttributeComposite Debug");

        private HoAttributeCompositeSettings settings;
        private Material debugMaterial;
        private RTHandle cameraColorTarget;

        private sealed class PassData
        {
            public Material material;
            public HoAttributeCompositeDebugMode debugMode;
            public TextureHandle[] selectionTextures;
        }

        public void Setup(HoAttributeCompositeSettings settings, Material debugMaterial, RTHandle cameraColorTarget)
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
                debugMaterial.SetFloat(HoAttributeCompositeShaderConstants.DebugModeId, (float)settings.debugMode);
                CoreUtils.DrawFullScreen(cmd, debugMaterial, shaderPassId: 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null || debugMaterial == null || settings.debugMode == HoAttributeCompositeDebugMode.Off)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoAttributeCompositeRenderGraphResources resources = frameData.GetOrCreate<HoAttributeCompositeRenderGraphResources>();
            TextureHandle destination = resourceData.activeColorTexture;
            if (!destination.IsValid() || !resources.HasSelectionPool)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-AttributeComposite Debug", out PassData passData, ProfilingSampler))
            {
                passData.material = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.selectionTextures = resources.selectionTextures;

                for (int i = 0; i < resources.selectionTextures.Length; i++)
                {
                    if (resources.selectionTextures[i].IsValid())
                    {
                        builder.UseTexture(resources.selectionTextures[i], AccessFlags.Read);
                    }
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(HoAttributeCompositeShaderConstants.DebugModeId, (float)data.debugMode);
                    for (int i = 0; i < data.selectionTextures.Length; i++)
                    {
                        if (data.selectionTextures[i].IsValid())
                        {
                            context.cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.SelectionTextureIds[i], data.selectionTextures[i]);
                        }
                    }

                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}
