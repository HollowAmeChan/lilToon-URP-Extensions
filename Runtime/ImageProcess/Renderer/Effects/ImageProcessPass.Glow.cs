using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#pragma warning disable CS0618, CS0672

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed partial class ImageProcessPass
    {
        private void ApplyGlowLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ImageProcessRuntimeLayer runtimeLayer)
        {
            ImageProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float radius = Mathf.Clamp(layer.parameters0.z, 0.0f, 12.0f);
            int mode = Mathf.Clamp(Mathf.RoundToInt(layer.parameters0.w), 0, 2);
            int downScale = radius > 0.75f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(radius * 1.25f), 2, 10);

            RenderTextureDescriptor glowDescriptor = sourceDescriptor;
            glowDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            glowDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            glowDescriptor.depthBufferBits = 0;
            glowDescriptor.depthStencilFormat = GraphicsFormat.None;
            glowDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref glowDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref glowTextureA, glowDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilImageProcessGlowA");
            RenderingUtils.ReAllocateIfNeeded(ref glowTextureB, glowDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilImageProcessGlowB");

            RTHandle current = glowTextureA;
            RTHandle next = glowTextureB;

            material.SetFloat(ImageProcessShaderConstants.RadiusId, Mathf.Max(0.75f, radius));
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 0; i < iterations; i++)
            {
                material.SetFloat(ImageProcessShaderConstants.RadiusId, Mathf.Lerp(0.75f, 2.5f + radius, (i + 1.0f) / iterations));
                Blitter.BlitCameraTexture(cmd, current, next, material, 1);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            if (mode != 0)
            {
                float angle = mode == 2 ? layer.parameters2.y : 0.0f;
                material.SetFloat(ImageProcessShaderConstants.AngleId, angle);
                material.SetFloat(ImageProcessShaderConstants.RadiusId, Mathf.Max(1.0f, radius * 1.75f));
                Blitter.BlitCameraTexture(cmd, current, next, material, 2);
                current = next;
            }

            cmd.SetGlobalTexture(ImageProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ImageProcessShaderConstants.BloomTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 3);
        }

        private TextureHandle RecordGlowLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ImageProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ImageProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;

            float radius = Mathf.Clamp(layer.parameters0.z, 0.0f, 12.0f);
            int mode = Mathf.Clamp(Mathf.RoundToInt(layer.parameters0.w), 0, 2);
            int downScale = radius > 0.75f ? 2 : 1;
            int iterations = Mathf.Clamp(2 + Mathf.RoundToInt(radius * 1.25f), 2, 10);

            TextureDesc glowDesc = sourceDesc;
            glowDesc.name = $"_lilImageProcessGlow_{layerIndex}";
            glowDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            glowDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            glowDesc.clearBuffer = false;
            glowDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref glowDesc);

            TextureHandle glowA = renderGraph.CreateTexture(glowDesc);
            TextureHandle glowB = renderGraph.CreateTexture(glowDesc);
            TextureHandle current = AddGlowPass(renderGraph, source, glowA, material, 0, Mathf.Max(0.75f, radius), runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = glowB;

            for (int i = 0; i < iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                float passRadius = Mathf.Lerp(0.75f, 2.5f + radius, (i + 1.0f) / iterations);
                current = AddGlowPass(renderGraph, passSource, passDestination, material, 1, passRadius, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            if (mode != 0)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                float angle = mode == 2 ? layer.parameters2.y : 0.0f;
                current = AddGlowPass(renderGraph, passSource, passDestination, material, 2, Mathf.Max(1.0f, radius * 1.75f), runtimeLayer.settings, _profilingSampler, _passName, default, angle);
            }

            return AddGlowPass(renderGraph, source, destination, material, 3, radius, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle AddGlowPass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float radius,
            ImageProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle bloomTexture = default,
            float angle = 0.0f)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} Glow", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.originalTexture = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = radius;
                passData.angle = angle;
                passData.bloomTexture = bloomTexture;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                if (bloomTexture.IsValid())
                {
                    builder.UseTexture(bloomTexture, AccessFlags.Read);
                    builder.SetGlobalTextureAfterPass(bloomTexture, ImageProcessShaderConstants.BloomTexId);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    data.material.SetFloat(ImageProcessShaderConstants.RadiusId, data.radius);
                    data.material.SetFloat(ImageProcessShaderConstants.AngleId, data.angle);
                    if (data.bloomTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ImageProcessShaderConstants.OriginalTexId, data.originalTexture);
                        context.cmd.SetGlobalTexture(ImageProcessShaderConstants.BloomTexId, data.bloomTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }
    }
}
