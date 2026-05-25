#pragma warning disable CS0618, CS0672

using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.AOV;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal sealed class HoMetadataBufferDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-MetadataBuffer Debug");
        private HoMetadataBufferSettings settings;
        private HoAovRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private Material debugMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle maskIdTexture;
            public TextureHandle normalDepthTexture;
            public TextureHandle surfaceDataTexture;
            public TextureHandle custom0Texture;
            public TextureHandle objectCustom0Texture;
            public TextureHandle objectCustom1Texture;
            public TextureHandle sssTexture;
            public Material debugMaterial;
            public HoAovDebugMode debugMode;
            public Vector4 debugDepthParams;
        }

        public void Setup(
            HoMetadataBufferSettings settings,
            HoAovRenderTargets renderTargets,
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
            HoMetadataBufferSettings settings,
            HoAovRenderTargets renderTargets,
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
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilHoAovDebugSource");
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
                cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, renderTargets.MaskIdTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, renderTargets.NormalDepthTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, renderTargets.SurfaceDataTexture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, renderTargets.Custom0Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, renderTargets.ObjectCustom0Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, renderTargets.ObjectCustom1Texture.nameID);
                cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, renderTargets.SssTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, debugMaterial, 0);
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
            HoMetadataBufferRenderGraphResources metadataResources = frameData.GetOrCreate<HoMetadataBufferRenderGraphResources>();
            HoGeometryBufferRenderGraphResources geometryResources = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()
                || !metadataResources.maskIdTexture.IsValid()
                || !geometryResources.normalDepthTexture.IsValid()
                || !metadataResources.surfaceDataTexture.IsValid()
                || !metadataResources.custom0Texture.IsValid()
                || !metadataResources.objectCustom0Texture.IsValid()
                || !metadataResources.objectCustom1Texture.IsValid()
                || !metadataResources.sssTexture.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_lilHoAovDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-MetadataBuffer Debug", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.maskIdTexture = metadataResources.maskIdTexture;
                passData.normalDepthTexture = geometryResources.normalDepthTexture;
                passData.surfaceDataTexture = metadataResources.surfaceDataTexture;
                passData.custom0Texture = metadataResources.custom0Texture;
                passData.objectCustom0Texture = metadataResources.objectCustom0Texture;
                passData.objectCustom1Texture = metadataResources.objectCustom1Texture;
                passData.sssTexture = metadataResources.sssTexture;
                passData.debugMaterial = debugMaterial;
                passData.debugMode = settings.debugMode;
                passData.debugDepthParams = GetDebugDepthParams(settings);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(passData.maskIdTexture, AccessFlags.Read);
                builder.UseTexture(passData.normalDepthTexture, AccessFlags.Read);
                builder.UseTexture(passData.surfaceDataTexture, AccessFlags.Read);
                builder.UseTexture(passData.custom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectCustom0Texture, AccessFlags.Read);
                builder.UseTexture(passData.objectCustom1Texture, AccessFlags.Read);
                builder.UseTexture(passData.sssTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.debugMaterial.SetFloat(HoAovShaderConstants.DebugModeId, (float)data.debugMode);
                    data.debugMaterial.SetVector(HoAovShaderConstants.DebugDepthParamsId, data.debugDepthParams);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, 1.0f);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.maskIdTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.NormalDepthTextureId, data.normalDepthTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.surfaceDataTexture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, data.custom0Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, data.objectCustom0Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, data.objectCustom1Texture);
                    context.cmd.SetGlobalTexture(HoAovShaderConstants.SssTextureId, data.sssTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.debugMaterial, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        private static void SetMaterialProperties(Material material, HoMetadataBufferSettings settings)
        {
            material.SetFloat(HoAovShaderConstants.DebugModeId, (float)settings.debugMode);
            material.SetVector(HoAovShaderConstants.DebugDepthParamsId, GetDebugDepthParams(settings));
        }

        private static Vector4 GetDebugDepthParams(HoMetadataBufferSettings settings)
        {
            float near = Mathf.Max(0.0f, settings.debugDepthNear);
            float far = Mathf.Max(near + 0.0001f, settings.debugDepthFar);
            return new Vector4(near, far, 1.0f / (far - near), 0.0f);
        }
    }

}
