// Compatibility-mode hooks are kept for projects that still run URP's non-RenderGraph path.
#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.ObjectBuffer;
using lilToon.URP.Extensions.SurfaceBuffer;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    internal sealed class HoSubsurfaceScatteringDebugPass : HoDebugRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-SubsurfaceScattering Debug");
        private RTHandle cameraColorTarget;
        private HoSubsurfaceScatteringSettings settings;
        private HoSubsurfaceScatteringRenderTargets renderTargets;
        private Material material;

        private sealed class PassData
        {
            public Vector2Int debugSize;
            public TextureHandle cameraColor;
            public TextureHandle sssTexture;
            public TextureHandle transmissionTexture;
            public TextureHandle normalDepthTexture;
            public Material material;
            public TextureHandle id0, coverage, classification, surfaceMaterial, owner, surfaceColor;
            public bool surfaceAvailable;
            public Vector4 sssParams;
            public Vector4 gateParams;
            public Vector4 color;
            public Vector4 transmissionParams;
            public Vector4 transmissionColor;
            public Vector4 transmissionShapeParams;
            public Vector4 debugParams;
        }

        public void Setup(
            HoSubsurfaceScatteringSettings settings,
            RTHandle cameraColorTarget,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.cameraColorTarget = cameraColorTarget;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetDebugRenderPassEvent();
        }

        public void SetupRenderGraph(
            HoSubsurfaceScatteringSettings settings,
            HoSubsurfaceScatteringRenderTargets renderTargets,
            Material material)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.material = material;
            renderPassEvent = settings.GetDebugRenderPassEvent();
        }

        public void ReleaseCompatibilityResources()
        {
            ReleaseDebugResources();
            cameraColorTarget = null;

        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            renderTargets.ReAllocateCompositeSource(renderingData.cameraData.cameraTargetDescriptor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null || renderTargets.SourceTexture == null || renderTargets.TransmissionTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(material, settings);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, renderTargets.SourceTexture.nameID);
                cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, renderTargets.TransmissionTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, renderTargets.CompositeSourceTexture, 0, true);
                cmd.SetGlobalTexture("_BlitTexture", renderTargets.CompositeSourceTexture.nameID);
                CoreUtils.SetRenderTarget(cmd, DebugTarget);
                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), material, (int)settings.debugMode, HoSubsurfaceScatteringShaderConstants.DebugParamsId, HoSubsurfaceScatteringDebugViewInfo.Views, new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height), CreateDebugParams(settings));
            }

            PublishCompatibility(renderingData.cameraData.camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoAttributeCompositeRenderGraphResources acResources = frameData.GetOrCreate<HoAttributeCompositeRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            HoSubsurfaceScatteringRenderGraphResources sssResources = frameData.GetOrCreate<HoSubsurfaceScatteringRenderGraphResources>();
            TextureHandle cameraColor = resourceData.activeColorTexture;
            TextureHandle sssTexture = sssResources.sourceTexture;
            TextureHandle transmissionTexture = sssResources.transmissionTexture;
            if (!cameraColor.IsValid()
                || !sssTexture.IsValid()
                || !transmissionTexture.IsValid()
                || !acResources.HasIdentityPool
                || !geometryResources.HasRequiredTextures)
            {
                return;
            }

            TextureHandle destination = CreateDebugTarget(renderGraph, frameData);
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-SubsurfaceScattering Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugSize = DebugSize(renderGraph, destination);
                passData.cameraColor = cameraColor;
                var surface = frameData.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
                passData.id0 = acResources.identityId0Texture;
                passData.coverage = acResources.identityCoverageTexture;
                passData.classification = surface.classificationTexture;
                passData.surfaceMaterial = surface.materialTexture;
                passData.owner = surface.ownerTexture;
                passData.surfaceColor = surface.colorTexture;
                passData.surfaceAvailable = surface.HasRequiredTextures;
                foreach (var texture in new[] { passData.id0, passData.coverage, passData.classification, passData.surfaceMaterial, passData.owner, passData.surfaceColor })
                    HoAttributeCompositeBindings.Read(builder, texture);
                passData.sssTexture = sssTexture;
                passData.transmissionTexture = transmissionTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.material = material;
                passData.sssParams = CreateSssParams(settings);
                passData.gateParams = CreateGateParams(settings);
                passData.color = settings.color;
                passData.transmissionParams = CreateTransmissionParams(settings);
                passData.transmissionColor = settings.transmissionColor;
                passData.transmissionShapeParams = CreateTransmissionShapeParams(settings);
                passData.debugParams = CreateDebugParams(settings);

                builder.UseTexture(cameraColor, AccessFlags.Read);
                builder.UseTexture(sssTexture, AccessFlags.Read);
                builder.UseTexture(transmissionTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.material.SetFloat(HoSurfaceBufferShaderConstants.ActiveId, data.surfaceAvailable ? 1 : 0);
                    if (data.id0.IsValid()) context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.Id0TextureId, data.id0);
                    if (data.coverage.IsValid()) context.cmd.SetGlobalTexture(HoObjectBufferShaderConstants.CoverageTextureId, data.coverage);
                    if (data.classification.IsValid()) context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ClassificationTextureId, data.classification);
                    if (data.surfaceMaterial.IsValid()) context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.MaterialTextureId, data.surfaceMaterial);
                    if (data.owner.IsValid()) context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.OwnerTextureId, data.owner);
                    if (data.surfaceColor.IsValid()) context.cmd.SetGlobalTexture(HoSurfaceBufferShaderConstants.ColorTextureId, data.surfaceColor);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, data.sssParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, data.gateParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, data.color);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, data.transmissionParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, data.transmissionColor);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, data.transmissionShapeParams);
                    data.material.SetVector(HoSubsurfaceScatteringShaderConstants.DebugParamsId, data.debugParams);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.SourceTextureId, data.sssTexture);
                    context.cmd.SetGlobalTexture(HoSubsurfaceScatteringShaderConstants.TransmissionTextureId, data.transmissionTexture);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    // 表面数值不再绑 MB 的两张图：调试 shader 已改读 AC 门面（SB 的全局名由 SB 的 pass 绑）。
                    context.cmd.SetGlobalTexture("_BlitTexture", data.cameraColor);
                    HoDebugGrid.Draw(context.cmd, data.material, (int)data.debugParams.x, HoSubsurfaceScatteringShaderConstants.DebugParamsId, HoSubsurfaceScatteringDebugViewInfo.Views, data.debugSize, data.debugParams);
                });
            }

        }

        private static void SetMaterialProperties(Material material, HoSubsurfaceScatteringSettings settings)
        {
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ParamsId, CreateSssParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.GateParamsId, CreateGateParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.ColorId, settings.color);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionParamsId, CreateTransmissionParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionColorId, settings.transmissionColor);
            material.SetVector(HoSubsurfaceScatteringShaderConstants.TransmissionShapeParamsId, CreateTransmissionShapeParams(settings));
            material.SetVector(HoSubsurfaceScatteringShaderConstants.DebugParamsId, CreateDebugParams(settings));
            HoSubsurfaceScatteringProfileShaderData.Set(material, settings);
        }

        private static Vector4 CreateSssParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0f, settings.strength), PackRadius(settings.radius, 24.0f), Mathf.Clamp((int)settings.quality, 1.0f, 32.0f), 0.0f);
        }

        private static Vector4 CreateGateParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(Mathf.Max(0.0001f, settings.depthTolerance), Mathf.Max(0.01f, settings.normalTolerance), Mathf.Clamp01(settings.sourcePreserve), 0.0f);
        }

        private static Vector4 CreateTransmissionParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Clamp01(settings.transmissionStrength),
                PackRadius(settings.transmissionRadius, 24.0f),
                Mathf.Clamp(settings.transmissionSamples, 2, 32),
                Mathf.Clamp01(settings.transmissionMainLightDirection));
        }

        private static Vector4 CreateTransmissionShapeParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4(
                Mathf.Max(0.0f, settings.transmissionDepthWeight),
                Mathf.Max(0.0f, settings.transmissionEdgeBoost),
                Mathf.Clamp01(settings.transmissionRimWeight),
                Mathf.Clamp01(settings.transmissionSmoothing));
        }

        private static Vector4 CreateDebugParams(HoSubsurfaceScatteringSettings settings)
        {
            return new Vector4((float)settings.debugMode, 0.0f, 0.0f, 0.0f);
        }

        private static float PackRadius(float radius, float maxRadius)
        {
            float normalized = Mathf.Clamp01(Mathf.Max(0.0f, radius) / Mathf.Max(0.0001f, maxRadius));
            return normalized * normalized * maxRadius;
        }
    }
}
