#pragma warning disable CS0618, CS0672

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    internal sealed class HoGeometryBufferDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("Ho-GeometryBuffer Debug");
        private HoGeometryBufferSettings settings;
        private HoGeometryBufferRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private Material debugMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle normalDepthTexture;
            public Material debugMaterial;
            public HoGeometryBufferDebugMode debugMode;
            public Vector4 debugDepthParams;
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
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void SetupRenderGraph(
            HoGeometryBufferSettings settings,
            HoGeometryBufferRenderTargets renderTargets,
            Material debugMaterial)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            this.debugMaterial = debugMaterial;
            renderPassEvent = settings != null ? settings.debugPassEvent : RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void Dispose()
        {
            ReleaseCompatibilityResources();
        }

        public void ReleaseCompatibilityResources()
        {
            renderTargets = null;
            cameraColorTarget = null;
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilHoGeometryBufferDebugSource");
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings == null || debugMaterial == null || cameraColorTarget == null || tempTexture == null || renderTargets == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                SetMaterialProperties(debugMaterial, settings);
                cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, debugMaterial, 0);
            }

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

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_lilHoGeometryBufferDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Ho-GeometryBuffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.debugDepthParams = GetDebugDepthParams(settings);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoGeometryBufferShaderConstants.DebugModeId, (float)data.debugMode);
                    data.debugMaterial.SetVector(HoGeometryBufferShaderConstants.DebugDepthParamsId, data.debugDepthParams);
                    context.cmd.SetGlobalTexture(HoGeometryBufferShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.debugMaterial, 0);
                });
            }

            resourceData.cameraColor = destination;
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
