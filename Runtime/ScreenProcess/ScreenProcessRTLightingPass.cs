#pragma warning disable CS0618, CS0672

using System;
using lilToon.URP.Extensions.RT;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>
    /// Owns the camera-color consumption of RTDI/RTGI products. It is
    /// intentionally separate from both producers and from the late visual
    /// ScreenProcess layer stack.
    /// </summary>
    internal sealed class ScreenProcessRTLightingPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int DirectDiffuseId = Shader.PropertyToID("_HoSPRTDirectDiffuse");
        private static readonly int DirectSpecularId = Shader.PropertyToID("_HoSPRTDirectSpecular");
        private static readonly int IndirectDiffuseId = Shader.PropertyToID("_HoSPRTIndirectDiffuse");
        private static readonly int DirectWeightId = Shader.PropertyToID("_HoSPRTDirectWeight");
        private static readonly int DirectModeId = Shader.PropertyToID("_HoSPRTDirectMode");
        private static readonly int IndirectIntensityId = Shader.PropertyToID("_HoSPRTIndirectIntensity");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepthTexture");

        private Material material;
        private RTHandle cameraColorTarget;
        private RTHandle scratch;
        private ScreenProcessRTLightingConsumerSettings consumerSettings;

        private sealed class PassData
        {
            internal Material material;
            internal TextureHandle source;
            internal TextureHandle directDiffuse;
            internal TextureHandle directSpecular;
            internal TextureHandle indirectDiffuse;
            internal TextureHandle cameraDepth;
            internal HoRTPrimarySurfaceInputs primary;
            internal float directWeight;
            internal float directMode;
            internal float indirectIntensity;
        }

        internal ScreenProcessRTLightingPass()
        {
            renderPassEvent = ScreenProcessRenderPassEvents.RTLightingComposite;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        internal void Setup(
            Material material,
            RTHandle cameraColorTarget,
            ScreenProcessRTLightingConsumerSettings consumerSettings)
        {
            this.material = material;
            this.cameraColorTarget = cameraColorTarget;
            this.consumerSettings = consumerSettings;
            renderPassEvent = ScreenProcessRenderPassEvents.RTLightingComposite;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (material == null || cameraColorTarget == null)
            {
                return;
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(
                ref scratch,
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: "_lilScreenProcessRTLightingScratch");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null ||
                cameraColorTarget == null ||
                scratch == null ||
                !consumerSettings.HasAny ||
                !ScreenProcessRTLightingCompatibility.TryGet(
                    renderingData.cameraData.camera,
                    out ScreenProcessRTLightingCompatibilityFrame products))
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Ho-ScreenProcess RT lighting");
            HoRTPrimarySurfaceInputs.BindCompatibility(cmd, renderingData.cameraData);
            BindCompatibilityProducts(cmd, products, consumerSettings);
            Blitter.BlitCameraTexture(cmd, cameraColorTarget, scratch, material, 0);
            Blitter.BlitCameraTexture(cmd, scratch, cameraColorTarget);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer ||
                !resourceData.activeColorTexture.IsValid() ||
                !resourceData.cameraDepthTexture.IsValid() ||
                material == null)
            {
                return;
            }

            ScreenProcessRTLightingResources products =
                frameData.GetOrCreate<ScreenProcessRTLightingResources>();
            if (!consumerSettings.HasAny || !products.HasAnyProduct)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "_lilScreenProcessRTLightingColor";
            descriptor.depthBufferBits = 0;
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);

            using (IRasterRenderGraphBuilder builder =
                renderGraph.AddRasterRenderPass<PassData>(
                    "Ho-ScreenProcess RT lighting",
                    out PassData data,
                    new ProfilingSampler("Ho-ScreenProcess RT lighting")))
            {
                data.material = material;
                data.source = source;
                data.directDiffuse = products.directEnabled
                    ? products.directDiffuse
                    : renderGraph.defaultResources.blackTexture;
                data.directSpecular = products.directEnabled
                    ? products.directSpecular
                    : renderGraph.defaultResources.blackTexture;
                data.indirectDiffuse = products.indirectEnabled
                    ? products.indirectDiffuse
                    : renderGraph.defaultResources.blackTexture;
                data.cameraDepth = resourceData.cameraDepthTexture;
                data.primary = HoRTPrimarySurfaceInputs.Read(renderGraph, frameData, builder);
                data.directWeight = consumerSettings.directEnabled ? consumerSettings.directWeight : 0.0f;
                data.directMode = consumerSettings.directEnabled && consumerSettings.directAdditive ? 1.0f : 0.0f;
                data.indirectIntensity = consumerSettings.indirectEnabled ? consumerSettings.indirectIntensity : 0.0f;

                builder.UseTexture(data.source, AccessFlags.Read);
                builder.UseTexture(data.cameraDepth, AccessFlags.Read);
                builder.UseTexture(data.directDiffuse, AccessFlags.Read);
                builder.UseTexture(data.directSpecular, AccessFlags.Read);
                builder.UseTexture(data.indirectDiffuse, AccessFlags.Read);

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    passData.primary.Bind(context.cmd);
                    context.cmd.SetGlobalTexture(DirectDiffuseId, passData.directDiffuse);
                    context.cmd.SetGlobalTexture(DirectSpecularId, passData.directSpecular);
                    context.cmd.SetGlobalTexture(IndirectDiffuseId, passData.indirectDiffuse);
                    context.cmd.SetGlobalTexture(CameraDepthId, passData.cameraDepth);
                    context.cmd.SetGlobalFloat(DirectWeightId, passData.directWeight);
                    context.cmd.SetGlobalFloat(DirectModeId, passData.directMode);
                    context.cmd.SetGlobalFloat(IndirectIntensityId, passData.indirectIntensity);
                    Blitter.BlitTexture(
                        context.cmd,
                        passData.source,
                        new Vector4(1, 1, 0, 0),
                        passData.material,
                        0);
                });
            }

            resourceData.cameraColor = destination;
        }

        public void Dispose()
        {
            scratch?.Release();
            scratch = null;
            cameraColorTarget = null;
            material = null;
        }

        private static void BindCompatibilityProducts(
            CommandBuffer cmd,
            ScreenProcessRTLightingCompatibilityFrame products,
            ScreenProcessRTLightingConsumerSettings consumerSettings)
        {
            cmd.SetGlobalTexture(DirectDiffuseId, Texture2D.blackTexture);
            cmd.SetGlobalTexture(DirectSpecularId, Texture2D.blackTexture);
            cmd.SetGlobalTexture(IndirectDiffuseId, Texture2D.blackTexture);
            if (products.directEnabled)
            {
                cmd.SetGlobalTexture(DirectDiffuseId, products.directDiffuse.nameID);
                cmd.SetGlobalTexture(DirectSpecularId, products.directSpecular.nameID);
            }

            if (products.indirectEnabled)
            {
                cmd.SetGlobalTexture(IndirectDiffuseId, products.indirectDiffuse.nameID);
            }

            cmd.SetGlobalFloat(
                DirectWeightId,
                products.directEnabled && consumerSettings.directEnabled ? consumerSettings.directWeight : 0.0f);
            cmd.SetGlobalFloat(
                DirectModeId,
                products.directEnabled && consumerSettings.directEnabled && consumerSettings.directAdditive ? 1.0f : 0.0f);
            cmd.SetGlobalFloat(
                IndirectIntensityId,
                products.indirectEnabled && consumerSettings.indirectEnabled
                    ? consumerSettings.indirectIntensity
                    : 0.0f);
        }
    }
}
