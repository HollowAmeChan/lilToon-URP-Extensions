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
        private bool RequiresAovComposite()
        {
            if (aovCompositeMaterial == null)
            {
                return false;
            }

            for (int i = 0; i < runtimeLayers.Count; i++)
            {
                if (RequiresAovComposite(runtimeLayers[i]?.settings))
                {
                    return true;
                }
            }

            return false;
        }

        private bool RequiresAovComposite(ShoostPostProcessLayer layer)
        {
            return aovCompositeMaterial != null &&
                   layer != null &&
                   ShoostPostProcessAovSupport.SupportsComposite(layer.effect) &&
                   (layer.useAovMask || layer.debugAovMask);
        }

        private TextureHandle RecordAovCompositeIfNeeded(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle layerResult,
            ShoostPostProcessLayer layer,
            int layerIndex,
            HoAovRenderGraphResources aovResources)
        {
            if (!RequiresAovComposite(layer) || !source.IsValid() || !layerResult.IsValid())
            {
                return layerResult;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"_lilShoostPostProcessLayer{layerIndex}_AOV";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref destinationDesc);
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} AOV Mask {layerIndex}", out PassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.layerResultTexture = layerResult;
                passData.layer = layer;
                passData.material = aovCompositeMaterial;
                passData.aovMaskIdTexture = aovResources.maskIdTexture;
                passData.aovSurfaceDataTexture = aovResources.surfaceDataTexture;
                passData.aovCustom0Texture = aovResources.custom0Texture;
                passData.aovObjectCustom0Texture = aovResources.objectCustom0Texture;
                passData.aovObjectCustom1Texture = aovResources.objectCustom1Texture;
                passData.useAovMaskTexture = aovResources.maskIdTexture.IsValid();
                passData.useAovSurfaceData = aovResources.surfaceDataTexture.IsValid();
                passData.useAovCustom0 = aovResources.custom0Texture.IsValid();
                passData.useAovObjectCustom0 = aovResources.objectCustom0Texture.IsValid();
                passData.useAovObjectCustom1 = aovResources.objectCustom1Texture.IsValid();

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(layerResult, AccessFlags.Read);
                if (passData.useAovMaskTexture)
                {
                    builder.UseTexture(aovResources.maskIdTexture, AccessFlags.Read);
                }

                if (passData.useAovSurfaceData)
                {
                    builder.UseTexture(aovResources.surfaceDataTexture, AccessFlags.Read);
                }

                if (passData.useAovCustom0)
                {
                    builder.UseTexture(aovResources.custom0Texture, AccessFlags.Read);
                }

                if (passData.useAovObjectCustom0)
                {
                    builder.UseTexture(aovResources.objectCustom0Texture, AccessFlags.Read);
                }

                if (passData.useAovObjectCustom1)
                {
                    builder.UseTexture(aovResources.objectCustom1Texture, AccessFlags.Read);
                }

                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyShoostAovCompositeProperties(data.layer, data.material);
                    context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.LayerResultTextureId, data.layerResultTexture);
                    context.cmd.SetGlobalFloat(HoAovShaderConstants.ActiveId, data.useAovMaskTexture ? 1.0f : 0.0f);
                    if (data.useAovMaskTexture)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.MaskIdTextureId, data.aovMaskIdTexture);
                    }

                    if (data.useAovSurfaceData)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.SurfaceDataTextureId, data.aovSurfaceDataTexture);
                    }

                    if (data.useAovCustom0)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.Custom0TextureId, data.aovCustom0Texture);
                    }

                    if (data.useAovObjectCustom0)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom0TextureId, data.aovObjectCustom0Texture);
                    }

                    if (data.useAovObjectCustom1)
                    {
                        context.cmd.SetGlobalTexture(HoAovShaderConstants.ObjectCustom1TextureId, data.aovObjectCustom1Texture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            return destination;
        }

    }
}
