using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.GeometryBuffer;

namespace lilToon.URP.Extensions.AttributeComposite
{
    // AC's visible identity product is distinct from the raw OB pool. The original renderer owner
    // comes from the actual expanded shell draw, not a dilation of IDs or a nearest-pixel guess.
    internal sealed class HoAttributeCompositeOutlineIdentityPass : IDisposable
    {
        // Fixed 3-target identity and 4-target semantic shaders; no per-camera MRT layout mutation.
        private Material identityMaterial, selectionMaterial;
        internal RTHandle[] CompatibilityTargets => targets;
        internal int CompatibilityIdentityCount => targets[0] != null ? 3 : 0;
        internal int CompatibilitySelectionCount => targets[3] != null ? 4 : 0;
        private readonly RTHandle[] targets = new RTHandle[7];
        private static readonly string[] Names = { "_HoACVisualIdentityId0Texture", "_HoACVisualIdentityId1Texture", "_HoACVisualIdentityCoverageTexture",
            "_HoACVisualSelection0Texture", "_HoACVisualSelection1Texture", "_HoACVisualSelection2Texture", "_HoACVisualSelection3Texture" };
        private static readonly int[] OutputIds = { HoObjectBufferShaderConstants.Id0TextureId, HoObjectBufferShaderConstants.Id1TextureId, HoObjectBufferShaderConstants.CoverageTextureId,
            HoAttributeCompositeShaderConstants.SelectionTextureIds[0], HoAttributeCompositeShaderConstants.SelectionTextureIds[1],
            HoAttributeCompositeShaderConstants.SelectionTextureIds[2], HoAttributeCompositeShaderConstants.SelectionTextureIds[3] };
        private static readonly int[] InputIds = { Shader.PropertyToID("_HoACOutlineBaseId0Texture"),Shader.PropertyToID("_HoACOutlineBaseId1Texture"),Shader.PropertyToID("_HoACOutlineBaseCoverageTexture") };
        private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private sealed class Data
        {
            public TextureHandle id0, id1, coverage, outline;
            public Material material;
            public TextureHandle[] selections;
            public bool surfaceAvailable;
            public MaterialPropertyBlock properties;
        }
        private Material GetMaterial(bool selections)
        {
            var existing = selections ? selectionMaterial : identityMaterial;
            if (existing != null) return existing;
            var shader = Shader.Find(selections ? "Hidden/lilToon/URP/AttributeComposite/VisualSelectionResolve" : "Hidden/lilToon/URP/AttributeComposite/VisualIdentityResolve");
            if (shader == null) return null;
            var created = CoreUtils.CreateEngineMaterial(shader);
            #if UNITY_EDITOR
                UnityEditor.ShaderUtil.CompilePass(created,0,true);
            #endif
            if (selections) selectionMaterial = created; else identityMaterial = created;
            return created;
        }
        private static bool Needed(HoAttributeCompositeDemandSnapshot demand) => demand != null &&
            (demand.Resources & (HoACDemandResources.Identity | HoACDemandResources.Selection | HoACDemandResources.SurfaceAttributes)) != 0;

        internal void Record(RenderGraph graph, UniversalCameraData camera,
            HoAttributeCompositeRenderGraphResources r, HoObjectBufferRenderGraphResources raw)
        {
            if (!raw.HasRequiredTextures || !r.outlineOwnerTexture.IsValid() || !Needed(r.demand)) return;
            var material = GetMaterial(false); if (material == null) return;
            var desc = new TextureDesc(camera.cameraTargetDescriptor.width,camera.cameraTargetDescriptor.height)
            {
                format = GraphicsFormat.R8G8B8A8_UNorm, dimension = camera.cameraTargetDescriptor.dimension,
                slices = camera.cameraTargetDescriptor.volumeDepth, clearBuffer = true, clearColor = Color.clear,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                useDynamicScale = camera.cameraTargetDescriptor.useDynamicScale,
                useDynamicScaleExplicit = camera.cameraTargetDescriptor.useDynamicScaleExplicit
            };
            bool selections = r.HasSelectionPool;
            var result = new TextureHandle[selections ? 7 : 3];
            for (int i = 0; i < result.Length; ++i) { desc.name = Names[i]; result[i] = graph.CreateTexture(desc); }
            using (var builder = graph.AddRasterRenderPass<Data>("Ho-AC Outline Identity Inheritance",out var data))
            {
                data.id0 = raw.id0Texture; data.id1 = raw.id1Texture; data.coverage = raw.coverageTexture;
                data.outline = r.outlineOwnerTexture; data.material = material;
                data.properties = new MaterialPropertyBlock();
                builder.UseTexture(data.id0,AccessFlags.Read); builder.UseTexture(data.id1,AccessFlags.Read);
                builder.UseTexture(data.coverage,AccessFlags.Read); builder.UseTexture(data.outline,AccessFlags.Read);
                builder.SetGlobalTextureAfterPass(data.id0,HoAttributeCompositeShaderConstants.RawIdentityIds[0]);
                builder.SetGlobalTextureAfterPass(data.id1,HoAttributeCompositeShaderConstants.RawIdentityIds[1]);
                builder.SetGlobalTextureAfterPass(data.coverage,HoAttributeCompositeShaderConstants.RawIdentityIds[2]);
                for (int i = 0; i < 3; ++i)
                {
                    builder.SetRenderAttachment(result[i],i,AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(result[i],OutputIds[i]);
                }
                builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                    RTHandle a = d.id0, b = d.id1, c = d.coverage, shell = d.outline;
                    d.properties.SetTexture(InputIds[0],a.rt); d.properties.SetTexture(InputIds[1],b.rt); d.properties.SetTexture(InputIds[2],c.rt);
                    d.properties.SetTexture(HoAttributeCompositeShaderConstants.OutlineOwnerId,shell.rt);
                    d.properties.SetFloat(HoAttributeCompositeShaderConstants.OutlineInheritanceActiveId,1);
                    d.properties.SetVector(ScaleBiasId,new Vector4(1,1,0,0));
                    context.cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.RawInputsAvailableId,1);
                    context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,d.properties);
                });
            }
            r.identityId0Texture = result[0]; r.identityId1Texture = result[1]; r.identityCoverageTexture = result[2];
            r.outlineInheritance = true;
            if (selections)
            {
                var selectionMaterial = GetMaterial(true);
                using (var builder = graph.AddRasterRenderPass<Data>("Ho-AC Outline Semantic Inheritance",out var data))
                {
                    data.id0 = raw.id0Texture; data.outline = r.outlineOwnerTexture; data.material = selectionMaterial;
                    data.properties = new MaterialPropertyBlock();
                    data.selections = (TextureHandle[])r.selectionTextures.Clone(); data.surfaceAvailable = r.HasSurfaceSemantics || r.HasCorrelatedSemantics;
                    builder.UseTexture(data.id0,AccessFlags.Read); builder.UseTexture(data.outline,AccessFlags.Read);
                    for (int i = 0; i < 4; ++i)
                    {
                        builder.UseTexture(data.selections[i],AccessFlags.Read);
                        builder.SetRenderAttachment(result[i+3],i,AccessFlags.Write);
                        builder.SetGlobalTextureAfterPass(result[i+3],OutputIds[i+3]);
                    }
                    builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                        RTHandle shell = d.outline;
                        d.properties.SetTexture(HoAttributeCompositeShaderConstants.OutlineOwnerId,shell.rt);
                        d.properties.SetFloat(HoAttributeCompositeShaderConstants.OutlineInheritanceActiveId,1);
                        d.properties.SetFloat(HoAttributeCompositeShaderConstants.OutlineSurfaceAvailableId,d.surfaceAvailable ? 1 : 0);
                        d.properties.SetVector(ScaleBiasId,new Vector4(1,1,0,0));
                        for (int i = 0; i < 4; ++i) { RTHandle texture = d.selections[i]; d.properties.SetTexture(HoAttributeCompositeShaderConstants.RawSelectionIds[i],texture.rt); }
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,d.properties);
                    });
                }
                for (int i = 0; i < 4; ++i) { r.rawSelectionTextures[i] = r.selectionTextures[i]; r.selectionTextures[i] = result[i+3]; }
            }
        }
        internal void Execute(CommandBuffer cmd, RenderTextureDescriptor descriptor, HoAttributeCompositeDemandSnapshot demand,
            HoObjectBufferRenderTargets raw, HoGeometryBufferRenderTargets geometry, RTHandle[] rawSelections, bool surfaceAvailable)
        {
            if (raw == null || geometry?.OutlineOwnerTexture == null || !Needed(demand))
            { ReleaseCompatibilityResources(); return; }
            descriptor.depthBufferBits = 0; descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm; descriptor.msaaSamples = 1; descriptor.bindMS = false;
            bool selections = rawSelections != null && rawSelections[0] != null;
            var material = GetMaterial(false); if (material == null) { ReleaseCompatibilityResources(); return; }
            var identifiers = new RenderTargetIdentifier[selections ? 7 : 3];
            if (!selections) for (int i = 3; i < targets.Length; ++i) { targets[i]?.Release(); targets[i] = null; }
            for (int i = 0; i < identifiers.Length; ++i)
            {
                RenderingUtils.ReAllocateIfNeeded(ref targets[i],descriptor,FilterMode.Point,TextureWrapMode.Clamp,name:Names[i]);
                identifiers[i] = targets[i].nameID;
            }
            cmd.SetGlobalTexture(InputIds[0],raw.Id0Texture); cmd.SetGlobalTexture(InputIds[1],raw.Id1Texture);
            cmd.SetGlobalTexture(InputIds[2],raw.CoverageTexture);
            cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.OutlineOwnerId,geometry.OutlineOwnerTexture);
            cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.OutlineInheritanceActiveId,1);
            cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.RawInputsAvailableId,1);
            cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.RawIdentityIds[0],raw.Id0Texture);
            cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.RawIdentityIds[1],raw.Id1Texture);
            cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.RawIdentityIds[2],raw.CoverageTexture);
            cmd.SetGlobalFloat(HoAttributeCompositeShaderConstants.OutlineSurfaceAvailableId,surfaceAvailable ? 1 : 0);
            if (selections) for (int i = 0; i < 4; ++i) cmd.SetGlobalTexture(HoAttributeCompositeShaderConstants.RawSelectionIds[i],rawSelections[i]);
            cmd.SetRenderTarget(new[]{identifiers[0],identifiers[1],identifiers[2]},BuiltinRenderTextureType.None);
            cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
            Blitter.BlitTexture(cmd,raw.Id0Texture,new Vector4(1,1,0,0),material,0);
            if (selections)
            {
                cmd.SetRenderTarget(new[]{identifiers[3],identifiers[4],identifiers[5],identifiers[6]},BuiltinRenderTextureType.None);
                cmd.ClearRenderTarget(RTClearFlags.Color,Color.clear,1,0);
                Blitter.BlitTexture(cmd,raw.Id0Texture,new Vector4(1,1,0,0),GetMaterial(true),0);
            }
            for (int i = 0; i < identifiers.Length; ++i) cmd.SetGlobalTexture(OutputIds[i],targets[i]);
        }
        internal void ReleaseCompatibilityResources()
        {
            for (int i = 0; i < targets.Length; ++i) { targets[i]?.Release(); targets[i] = null; }
        }
        public void Dispose()
        {
            ReleaseCompatibilityResources(); CoreUtils.Destroy(identityMaterial); CoreUtils.Destroy(selectionMaterial);
            identityMaterial = selectionMaterial = null;
        }
    }
}
