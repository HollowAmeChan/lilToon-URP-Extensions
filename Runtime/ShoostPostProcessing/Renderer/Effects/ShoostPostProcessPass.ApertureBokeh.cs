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
        private void ApplyApertureBokehLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            ApplyLayerProperties(layer, material);

            float apertureSize = Mathf.Clamp01(layer.parameters0.x);
            float radius = Mathf.Lerp(2.0f, 24.0f, apertureSize);
            int downScale = apertureSize > 0.35f ? 2 : 1;

            RenderTextureDescriptor bokehDescriptor = sourceDescriptor;
            bokehDescriptor.width = Mathf.Max(1, sourceDescriptor.width / downScale);
            bokehDescriptor.height = Mathf.Max(1, sourceDescriptor.height / downScale);
            bokehDescriptor.depthBufferBits = 0;
            bokehDescriptor.depthStencilFormat = GraphicsFormat.None;
            bokehDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref bokehDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref apertureBokehTextureA, bokehDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostApertureBokehA");
            RenderingUtils.ReAllocateIfNeeded(ref apertureBokehTextureB, bokehDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostApertureBokehB");

            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, radius);
            Blitter.BlitCameraTexture(cmd, source, apertureBokehTextureA, material, 0);
            Blitter.BlitCameraTexture(cmd, apertureBokehTextureA, apertureBokehTextureB, material, 1);
            Blitter.BlitCameraTexture(cmd, apertureBokehTextureB, apertureBokehTextureA, material, 2);

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BloomTexId, apertureBokehTextureA);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 3);
        }

        private TextureHandle RecordApertureBokehLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ShoostPostProcessRuntimeLayer runtimeLayer,
            int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            float apertureSize = Mathf.Clamp01(layer.parameters0.x);
            float radius = Mathf.Lerp(2.0f, 24.0f, apertureSize);
            int downScale = apertureSize > 0.35f ? 2 : 1;

            TextureDesc bokehDesc = sourceDesc;
            bokehDesc.name = $"_lilShoostApertureBokeh_{layerIndex}";
            bokehDesc.width = Mathf.Max(1, sourceDesc.width / downScale);
            bokehDesc.height = Mathf.Max(1, sourceDesc.height / downScale);
            bokehDesc.clearBuffer = false;
            bokehDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref bokehDesc);

            TextureHandle bokehA = renderGraph.CreateTexture(bokehDesc);
            bokehDesc.name = $"_lilShoostApertureBokehTmp_{layerIndex}";
            TextureHandle bokehB = renderGraph.CreateTexture(bokehDesc);

            TextureHandle current = AddGlowPass(renderGraph, source, bokehA, material, 0, radius, layer, _profilingSampler, _passName);
            current = AddGlowPass(renderGraph, current, bokehB, material, 1, radius, layer, _profilingSampler, _passName);
            current = AddGlowPass(renderGraph, current, bokehA, material, 2, radius, layer, _profilingSampler, _passName);

            return AddGlowPass(renderGraph, source, destination, material, 3, radius, layer, _profilingSampler, _passName, current);
        }

    }
}
