using lilToon.URP.Extensions.AOV;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ShoostPostProcessPass
    {
        private void ApplyRGBBlurV2Layer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float maxChannelBlur = Mathf.Clamp01(Mathf.Max(layer.parameters0.x, layer.parameters0.y, layer.parameters0.z) * layer.intensity);
            int downScale = maxChannelBlur > 0.0001f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(maxChannelBlur * 4.0f), 2, 6);
            float radius = Mathf.Lerp(0.75f, 9.0f, maxChannelBlur);

            RenderTextureDescriptor blurDescriptor = sourceDescriptor;
            blurDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            blurDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            blurDescriptor.depthBufferBits = 0;
            blurDescriptor.depthStencilFormat = GraphicsFormat.None;
            blurDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref blurDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref rgbBlurTextureA, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostRGBBlurV2A");
            RenderingUtils.ReAllocateIfNeeded(ref rgbBlurTextureB, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostRGBBlurV2B");

            RTHandle current = rgbBlurTextureA;
            RTHandle next = rgbBlurTextureB;
            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius);
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 1; i < iterations; i++)
            {
                material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius + i);
                Blitter.BlitCameraTexture(cmd, current, next, material, 0);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 1);
        }

        private TextureHandle RecordRGBBlurV2Layer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;

            float maxChannelBlur = Mathf.Clamp01(Mathf.Max(layer.parameters0.x, layer.parameters0.y, layer.parameters0.z) * layer.intensity);
            int downScale = maxChannelBlur > 0.0001f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(maxChannelBlur * 4.0f), 2, 6);
            float radius = Mathf.Lerp(0.75f, 9.0f, maxChannelBlur);

            TextureDesc blurDesc = sourceDesc;
            blurDesc.name = $"_lilShoostRGBBlurV2_{layerIndex}";
            blurDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            blurDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            blurDesc.clearBuffer = false;
            blurDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref blurDesc);

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            TextureHandle current = AddRGBBlurV2Pass(renderGraph, source, blurA, material, 0, radius, runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = blurB;
            for (int i = 1; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddRGBBlurV2Pass(renderGraph, passSource, passDestination, material, 0, radius + i, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddRGBBlurV2Pass(renderGraph, source, destination, material, 1, radius, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle AddRGBBlurV2Pass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float radius,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle blurredTexture = default)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} RGB Blur V2", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.originalTexture = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = radius;
                passData.blurredTexture = blurredTexture;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                if (blurredTexture.IsValid())
                {
                    builder.UseTexture(blurredTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(blurredTexture, ShoostPostProcessShaderConstants.BlurredTexId);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius);
                    if (data.blurredTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, data.originalTexture);
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, data.blurredTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

    }
}
