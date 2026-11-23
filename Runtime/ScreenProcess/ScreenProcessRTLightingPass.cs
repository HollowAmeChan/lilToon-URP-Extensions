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
        private static readonly int BlendId = Shader.PropertyToID("_HoSPRTBlend");
        private static readonly int ComponentsId = Shader.PropertyToID("_HoSPRTComponents");
        private static readonly int TintId = Shader.PropertyToID("_HoSPRTTint");
        private static readonly int DepthRangeId = Shader.PropertyToID("_HoSPRTDepthRange");
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
            internal ScreenProcessRTLightingConsumerSettings.Layer layer;
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

        internal void SetConsumerSettings(ScreenProcessRTLightingConsumerSettings consumerSettings)
        {
            this.consumerSettings = consumerSettings;
        }

        internal void SetMaterialAndConsumerSettings(
            Material material,
            ScreenProcessRTLightingConsumerSettings consumerSettings)
        {
            this.material = material;
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
            try
            {
                HoRTPrimarySurfaceInputs.BindCompatibility(cmd, renderingData.cameraData);
                BindCompatibilityProducts(cmd, products);
                RTHandle source = cameraColorTarget;
                foreach (var layer in consumerSettings.layers)
                {
                    if (layer.indirect ? !products.indirectEnabled : !products.directEnabled) continue;
                    cmd.SetGlobalVector(BlendId, layer.blend);
                    cmd.SetGlobalVector(ComponentsId, layer.components);
                    cmd.SetGlobalVector(TintId, layer.tint);
                    cmd.SetGlobalVector(DepthRangeId, layer.depthRange);
                    RTHandle destination = source == cameraColorTarget ? scratch : cameraColorTarget;
                    Blitter.BlitCameraTexture(cmd, source, destination, material, 0);
                    source = destination;
                }
                if (source != cameraColorTarget) Blitter.BlitCameraTexture(cmd, source, cameraColorTarget);
                context.ExecuteCommandBuffer(cmd);
            }
            finally { CommandBufferPool.Release(cmd); }
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

            foreach (var layer in consumerSettings.layers)
            {
                if (layer.indirect ? !products.indirectEnabled : !products.directEnabled) continue;
                RecordLayer(renderGraph, frameData, resourceData, products, layer);
            }
        }

        private void RecordLayer(RenderGraph renderGraph, ContextContainer frameData,
            UniversalResourceData resourceData, ScreenProcessRTLightingResources products,
            ScreenProcessRTLightingConsumerSettings.Layer layer)
        {
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
                    lilToon.URP.Extensions.Debugging.HoDebugPerformanceRegistry.GetSampler("Ho-ScreenProcess RT lighting")))
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
                data.layer = layer;

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
                    context.cmd.SetGlobalVector(BlendId, passData.layer.blend);
                    context.cmd.SetGlobalVector(ComponentsId, passData.layer.components);
                    context.cmd.SetGlobalVector(TintId, passData.layer.tint);
                    context.cmd.SetGlobalVector(DepthRangeId, passData.layer.depthRange);
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
            ScreenProcessRTLightingCompatibilityFrame products)
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

        }
    }
}
