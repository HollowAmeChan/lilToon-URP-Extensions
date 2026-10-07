#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.Debugging;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>
    /// SB 的调试直出：六张数值图、owner 与语义视图。
    /// **owner 视图是"对齐有没有出问题"的唯一入口**：绿 = owner 与 OB 层 0 一致，红 = 不一致（没人写也算）。
    /// </summary>
    internal sealed class HoSurfaceBufferDebugPass : HoDebugRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SurfaceBuffer Debug");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private HoSurfaceBufferSettings settings;
        private Material debugMaterial;
        private RTHandle cameraColorTarget;

        private sealed class PassData
        {
            public Vector2Int debugSize;
            public Material material;
            public HoSurfaceBufferDebugMode debugMode;
            public TextureHandle colorTexture;
            public TextureHandle emissionTexture;
            public TextureHandle normalTexture;
            public TextureHandle materialTexture;
            public TextureHandle reflectionTexture;
            public TextureHandle classificationTexture;
            public TextureHandle ownerTexture;
            /// <summary>语义 lane（单采样）：`SemanticOwner` / `SemanticLanes` 两个视图要读。</summary>
            public TextureHandle objectId0;
            public bool semanticAvailable;
            public TextureHandle semanticOwnerTexture;
            public TextureHandle[] semanticLaneTextures;
        }

        public void Setup(HoSurfaceBufferSettings settings, Material debugMaterial, RTHandle cameraColorTarget)
        {
            this.settings = settings;
            this.debugMaterial = debugMaterial;
            this.cameraColorTarget = cameraColorTarget;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void ReleaseCompatibilityResources()
        {
            ReleaseDebugResources();
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
                debugMaterial.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), debugMaterial, (int)settings.debugMode, HoSurfaceBufferShaderConstants.DebugModeId, HoSurfaceBufferDebugViewInfo.Views, new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height));
            }

            PublishCompatibility(renderingData.cameraData.camera);
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
            if (!resources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = CreateDebugTarget(renderGraph, frameData);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SurfaceBuffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugSize = DebugSize(renderGraph, destination);
                passData.material = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.colorTexture = resources.colorTexture;
                passData.emissionTexture = resources.emissionTexture;
                passData.normalTexture = resources.normalTexture;
                passData.materialTexture = resources.materialTexture;
                passData.reflectionTexture = resources.reflectionTexture;
                passData.classificationTexture = resources.classificationTexture;
                passData.ownerTexture = resources.ownerTexture;
                // 语义 lane 的视图（mode 8/9）读的就是这几张：声明依赖，别靠"生产者刚好不会被裁"。
                passData.objectId0 = frameData.GetOrCreate<HoObjectBufferRenderGraphResources>().id0Texture;
                passData.semanticAvailable = resources.HasSemanticLanes;
                if (passData.objectId0.IsValid()) builder.UseTexture(passData.objectId0, AccessFlags.Read);
                passData.semanticOwnerTexture = resources.semanticOwnerTexture;
                passData.semanticLaneTextures = resources.semanticLaneTextures;

                builder.UseTexture(passData.colorTexture, AccessFlags.Read);
                builder.UseTexture(passData.emissionTexture, AccessFlags.Read);
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
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat("_HoSurfaceBufferActive", 1);
                    context.cmd.SetGlobalFloat("_HoSurfaceSemanticActive", data.semanticAvailable ? 1 : 0);
                    context.cmd.SetGlobalFloat("_HoObjectBufferActive", data.objectId0.IsValid() ? 1 : 0);
                    if (data.objectId0.IsValid()) context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.objectId0);
                    if (data.semanticAvailable)
                    {
                        context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.SemanticOwnerTextureId, data.semanticOwnerTexture);
                        for (int i = 0; i < data.semanticLaneTextures.Length; i++)
                            context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i), data.semanticLaneTextures[i]);
                    }
                    data.material.SetFloat(HoSurfaceBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    data.material.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ColorTextureId, data.colorTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.EmissionTextureId, data.emissionTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.NormalTextureId, data.normalTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.MaterialTextureId, data.materialTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ReflectionTextureId, data.reflectionTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ClassificationTextureId, data.classificationTexture);
                    context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.OwnerTextureId, data.ownerTexture);
                    HoDebugGrid.Draw(context.cmd, data.material, (int)data.debugMode, HoSurfaceBufferShaderConstants.DebugModeId, HoSurfaceBufferDebugViewInfo.Views, data.debugSize);
                });
            }
        }
    }
}
