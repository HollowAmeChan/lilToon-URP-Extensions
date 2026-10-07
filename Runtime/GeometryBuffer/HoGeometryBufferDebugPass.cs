#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.Debugging;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferDebugPass : HoDebugRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GeometryBuffer Debug");
        private HoGeometryBufferSettings settings;
        private HoGeometryBufferRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private Material debugMaterial;

        private sealed class PassData
        {
            public Vector2Int debugSize;
            public TextureHandle normalDepthTexture;
            public TextureHandle outlineNormalDepthTexture;
            public TextureHandle coverageTexture;
            public TextureHandle outlineCoverageTexture;
            public TextureHandle skyTexture;
            public Material debugMaterial;
            public HoGeometryBufferDebugMode debugMode;
            public Vector4 debugDepthParams;
            public bool useSkyTexture;
            public bool useCoverageTexture;
            public bool useOutlineCoverageTexture;
        }

        public void Setup(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            RTHandle cameraColorTarget,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void SetupRenderGraph(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            ReleaseDebugResources();
            renderTargets = null;
            cameraColorTarget = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || debugMaterial == null || cameraColorTarget == null || renderTargets == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(debugMaterial, settings);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, renderTargets.OutlineNormalDepthTexture.nameID);
                if (renderTargets.CoverageTexture != null)
                {
                    cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.CoverageTextureId, renderTargets.CoverageTexture.nameID);
                }

                if (renderTargets.OutlineCoverageTexture != null)
                {
                    cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineCoverageTextureId, renderTargets.OutlineCoverageTexture.nameID);
                }

                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, renderTargets.CoverageTexture != null ? 1.0f : 0.0f);
                cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, renderTargets.OutlineCoverageTexture != null ? 1.0f : 0.0f);
                if (renderTargets.SkyTexture != null)
                {
                    cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, renderTargets.SkyTexture.nameID);
                }

                HoDebugGrid.Draw(CommandBufferHelpers.GetRasterCommandBuffer(cmd), debugMaterial, (int)settings.debugMode, HoGeometryBufferShaderConstants.DebugModeId, HoGeometryBufferDebugViewInfo.Views, new Vector2Int(DebugTarget.rt.width, DebugTarget.rt.height));
            }

            PublishCompatibility(renderingData.cameraData.camera);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            ReleaseCompatibilityResources();
            if (settings == null || debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid() || !geometryResources.normalDepthTexture.IsValid())
            {
                return;
            }

            TextureHandle destination = CreateDebugTarget(renderGraph, frameData);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-GeometryBuffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.debugSize = DebugSize(renderGraph, destination);
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.outlineNormalDepthTexture = geometryResources.outlineNormalDepthTexture;
                passData.coverageTexture = geometryResources.coverageTexture;
                passData.outlineCoverageTexture = geometryResources.outlineCoverageTexture;
                passData.skyTexture = geometryResources.skyTexture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.debugDepthParams = GetDebugDepthParams(settings);
                passData.useSkyTexture = geometryResources.skyTexture.IsValid();
                passData.useCoverageTexture = geometryResources.coverageTexture.IsValid();
                passData.useOutlineCoverageTexture = geometryResources.outlineCoverageTexture.IsValid();

                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                if (passData.outlineNormalDepthTexture.IsValid())
                {
                    builder.UseTexture(passData.outlineNormalDepthTexture, AccessFlags.Read);
                }
                if (passData.useCoverageTexture)
                {
                    builder.UseTexture(passData.coverageTexture, AccessFlags.Read);
                }
                if (passData.useOutlineCoverageTexture)
                {
                    builder.UseTexture(passData.outlineCoverageTexture, AccessFlags.Read);
                }
                if (passData.useSkyTexture)
                {
                    builder.UseTexture(passData.skyTexture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoGeometryBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    data.debugMaterial.SetVector(HoGeometryBufferShaderConstants.DebugDepthParamsId, data.debugDepthParams);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    if (data.useCoverageTexture)
                    {
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.CoverageTextureId, data.coverageTexture);
                    }

                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.CoverageTextureValidId, data.useCoverageTexture ? 1.0f : 0.0f);
                    if (data.outlineNormalDepthTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineNormalDepthTextureId, data.outlineNormalDepthTexture);
                    }
                    if (data.useOutlineCoverageTexture)
                    {
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.OutlineCoverageTextureId, data.outlineCoverageTexture);
                    }

                    context.cmd.SetGlobalFloat(HoGeometryBufferShaderConstants.OutlineCoverageTextureValidId, data.useOutlineCoverageTexture ? 1.0f : 0.0f);
                    if (data.useSkyTexture)
                    {
                        context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.SkyTextureId, data.skyTexture);
                    }

                    HoDebugGrid.Draw(context.cmd, data.debugMaterial, (int)data.debugMode, HoGeometryBufferShaderConstants.DebugModeId, HoGeometryBufferDebugViewInfo.Views, data.debugSize);
                });
            }

        }

        private static void SetMaterialProperties(Material material, HoGeometryBufferSettings settings)
        {
            material.SetFloat(HoGeometryBufferShaderConstants.DebugModeId, (float)settings.debugMode);
            material.SetVector(HoGeometryBufferShaderConstants.DebugDepthParamsId, GetDebugDepthParams(settings));
        }

        private static Vector4 GetDebugDepthParams(HoGeometryBufferSettings settings)
        {
            float near = Mathf.Max(0.0f, settings.debugDepthNear);
            float far = Mathf.Max(near + 0.0001f, settings.debugDepthFar);
            return new Vector4(near, far, 1.0f / (far - near), 0.0f);
        }
    }
}
