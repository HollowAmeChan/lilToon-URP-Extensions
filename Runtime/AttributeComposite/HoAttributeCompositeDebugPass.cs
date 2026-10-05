#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;

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
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

        private HoAttributeCompositeSettings settings;
        private Material debugMaterial;
        private RTHandle cameraColorTarget;

        private sealed class PassData
        {
            public Material material;
            public HoAttributeCompositeDebugMode debugMode;
            public TextureHandle[] selectionTextures;
            public HoAttributeCompositeRenderGraphResources resources;
            public HoACQueryDescriptor query;
            public Vector4 lane;
            public bool laneValid;
            public bool semanticDebug;
        }

        public void Setup(HoAttributeCompositeSettings settings, Material debugMaterial, RTHandle cameraColorTarget)
        {
            this.settings = settings;
            this.debugMaterial = debugMaterial;
            this.cameraColorTarget = cameraColorTarget;
            if (settings != null && HoAttributeCompositeSettings.IsSemanticDebug(settings.debugMode))
                HoObjectBufferRegistry.EnsureBuilt();
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
                cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                bool laneValid = HoAttributeCompositeSettings.TryResolveDebugLane(settings.debugSemanticName, out Vector4 lane);
                cmd.SetGlobalVector(HoAttributeCompositeShaderConstants.DebugLaneId, lane);
                cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.DebugLaneValidId, laneValid ? 1 : 0);
                cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.DebugSurfaceAvailableId,
                    HoSurfaceBufferSemanticPass.LastProduced ? 1 : 0);
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
            if (!destination.IsValid() || !resources.published)
            {
                return;
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-AttributeComposite Debug", out PassData passData, ProfilingSampler))
            {
                passData.material = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.selectionTextures = resources.selectionTextures;
                passData.resources = resources;
                passData.semanticDebug = HoAttributeCompositeSettings.IsSemanticDebug(settings.debugMode);
                passData.laneValid = HoAttributeCompositeSettings.TryResolveDebugLane(settings.debugSemanticName, out passData.lane);
                HoACQueryKind kind = settings.debugMode == HoAttributeCompositeDebugMode.GeometryCoverage ? HoACQueryKind.Geometry :
                    settings.debugMode == HoAttributeCompositeDebugMode.OutlineCoverage ? HoACQueryKind.Outline : HoACQueryKind.Screen;
                passData.query = HoACQueryDescriptor.Resolve(kind, null, 0, HoACMaskDomain.Screen);
                HoAttributeCompositeBindings.ReadQuery(builder, resources, passData.query);
                if (passData.semanticDebug)
                {
                    HoAttributeCompositeBindings.Read(builder, resources.identityId0Texture);
                    HoAttributeCompositeBindings.Read(builder, resources.identityId1Texture);
                    HoAttributeCompositeBindings.Read(builder, resources.identityCoverageTexture);
                    if (resources.HasSurfaceSemantics)
                    {
                        HoAttributeCompositeBindings.Read(builder, resources.semanticOwnerTexture);
                        for (int i = 0; i < resources.semanticLaneTextures.Length; i++)
                            HoAttributeCompositeBindings.Read(builder, resources.semanticLaneTextures[i]);
                    }
                }

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
                    context.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
                    HoAttributeCompositeBindings.BindQuery(context.cmd, data.resources, data.query);
                    context.cmd.SetGlobalVector(HoAttributeCompositeShaderConstants.DebugLaneId, data.lane);
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.DebugLaneValidId, data.laneValid ? 1 : 0);
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.DebugSurfaceAvailableId, data.resources.HasSurfaceSemantics ? 1 : 0);
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.ActiveId, data.resources.HasSelectionPool ? 1 : 0);
                    if (data.semanticDebug)
                    {
                        if (data.resources.HasIdentityPool)
                        {
                            context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.resources.identityId0Texture);
                            context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id1TextureId, data.resources.identityId1Texture);
                            context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, data.resources.identityCoverageTexture);
                        }
                        if (data.resources.HasSurfaceSemantics)
                        {
                            context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.SemanticOwnerTextureId, data.resources.semanticOwnerTexture);
                            for (int i = 0; i < data.resources.semanticLaneTextures.Length; i++)
                                context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.GetSemanticLaneTextureId(i), data.resources.semanticLaneTextures[i]);
                        }
                    }
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
