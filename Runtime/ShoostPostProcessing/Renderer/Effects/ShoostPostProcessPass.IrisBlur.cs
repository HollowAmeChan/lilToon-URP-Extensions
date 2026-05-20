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
        private static IrisBlurParameters GetIrisBlurParameters(ShoostPostProcessLayer layer)
        {
            Vector4 parameters0 = layer.parameters0;
            Vector4 parameters1 = layer.parameters1;
            Vector4 parameters2 = layer.parameters2;
            Vector4 parameters3 = layer.parameters3;

            return new IrisBlurParameters
            {
                resolutionType = Mathf.Clamp(Mathf.RoundToInt(parameters0.x), 0, 1),
                customResolution = new Vector2Int(Mathf.RoundToInt(parameters0.y), Mathf.RoundToInt(parameters0.z)),
                radius = parameters1.x > 0.0f ? parameters1.x : 1.0f,
                downScale = Mathf.Clamp(Mathf.RoundToInt(parameters1.y > 0.0f ? parameters1.y : 2.0f), 1, 4),
                iterations = Mathf.Clamp(Mathf.RoundToInt(parameters1.z > 0.0f ? parameters1.z : 3.0f), 1, 8),
                center = new Vector2(parameters2.x, parameters2.y),
                centerSize = parameters2.z > 0.0f ? parameters2.z : 0.8f,
                smoothness = parameters2.w > 0.0f ? parameters2.w : 0.1f,
                enableRgbSplit = parameters3.x > 0.5f,
                blurRadiusR = Mathf.Max(0.0f, parameters3.y),
                blurRadiusG = Mathf.Max(0.0f, parameters3.z),
                blurRadiusB = Mathf.Max(0.0f, parameters3.w),
                distance = Mathf.Max(0.0f, parameters0.w),
                angleRadians = parameters1.w * Mathf.Deg2Rad
            };
        }

        private static void ApplyIrisBlurProperties(Material material, IrisBlurParameters parameters, float screenRatio)
        {
            material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, parameters.radius * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, screenRatio);
            material.SetVector(ShoostPostProcessShaderConstants.CenterId, new Vector4(parameters.center.x, parameters.center.y, 0.0f, 0.0f));
            material.SetFloat(ShoostPostProcessShaderConstants.CenterSizeId, 1.0f - parameters.centerSize);
            material.SetFloat(ShoostPostProcessShaderConstants.SmoothnessId, parameters.smoothness);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetRId, parameters.blurRadiusR * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetGId, parameters.blurRadiusG * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetBId, parameters.blurRadiusB * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.DistanceId, parameters.distance * 0.01f);
            material.SetFloat(ShoostPostProcessShaderConstants.AngleId, parameters.angleRadians);

            if (parameters.enableRgbSplit)
            {
                material.EnableKeyword("ENABLE_RGBSPLIT");
            }
            else
            {
                material.DisableKeyword("ENABLE_RGBSPLIT");
            }
        }

        private void ApplyIrisBlurLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, RTHandle source, RTHandle destination, ShoostPostProcessRuntimeLayer runtimeLayer)
        {
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            IrisBlurParameters parameters = GetIrisBlurParameters(layer);

            int width = parameters.resolutionType == 1 && parameters.customResolution.x > 0 ? parameters.customResolution.x : sourceDescriptor.width;
            int height = parameters.resolutionType == 1 && parameters.customResolution.y > 0 ? parameters.customResolution.y : sourceDescriptor.height;
            width = Mathf.Max(1, width / parameters.downScale);
            height = Mathf.Max(1, height / parameters.downScale);

            RenderTextureDescriptor blurDescriptor = sourceDescriptor;
            blurDescriptor.width = width;
            blurDescriptor.height = height;
            blurDescriptor.depthBufferBits = 0;
            blurDescriptor.depthStencilFormat = GraphicsFormat.None;
            blurDescriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref blurDescriptor);

            RenderingUtils.ReAllocateIfNeeded(ref irisTextureA, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostIrisBlurA");
            RenderingUtils.ReAllocateIfNeeded(ref irisTextureB, blurDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_lilShoostIrisBlurB");

            float screenRatio = Mathf.Max(1.0f, sourceDescriptor.width) / Mathf.Max(1.0f, sourceDescriptor.height);
            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.OriginalTexId, source);
            ApplyLayerProperties(layer, material);
            ApplyIrisBlurProperties(material, parameters, screenRatio);

            RTHandle current = irisTextureA;
            RTHandle next = irisTextureB;
            Blitter.BlitCameraTexture(cmd, source, current, material, 0);

            for (int i = 1; i < parameters.iterations; i++)
            {
                Blitter.BlitCameraTexture(cmd, current, next, material, 1);
                RTHandle swap = current;
                current = next;
                next = swap;
            }

            cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, current);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 2);
        }

        private TextureHandle RecordIrisBlurLayer(RenderGraph renderGraph, TextureHandle source, ShoostPostProcessRuntimeLayer runtimeLayer, int layerIndex)
        {
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            ShoostPostProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            IrisBlurParameters parameters = GetIrisBlurParameters(layer);

            int width = parameters.resolutionType == 1 && parameters.customResolution.x > 0 ? parameters.customResolution.x : sourceDesc.width;
            int height = parameters.resolutionType == 1 && parameters.customResolution.y > 0 ? parameters.customResolution.y : sourceDesc.height;
            width = Mathf.Max(1, width / parameters.downScale);
            height = Mathf.Max(1, height / parameters.downScale);

            TextureDesc blurDesc = sourceDesc;
            blurDesc.name = $"_lilShoostIrisBlur_{layerIndex}";
            blurDesc.width = width;
            blurDesc.height = height;
            blurDesc.clearBuffer = false;
            blurDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref blurDesc);

            TextureHandle blurA = renderGraph.CreateTexture(blurDesc);
            TextureHandle blurB = renderGraph.CreateTexture(blurDesc);
            float screenRatio = Mathf.Max(1.0f, sourceDesc.width) / Mathf.Max(1.0f, sourceDesc.height);

            TextureHandle current = AddIrisPass(renderGraph, source, blurA, material, 0, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName);
            TextureHandle next = blurB;
            for (int i = 1; i < parameters.iterations; i++)
            {
                TextureHandle passSource = current;
                TextureHandle passDestination = next;
                current = AddIrisPass(renderGraph, passSource, passDestination, material, 1, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName);
                next = passSource;
            }

            TextureDesc outputDesc = sourceDesc;
            outputDesc.name = $"_lilShoostPostProcessLayer{layerIndex}";
            outputDesc.clearBuffer = false;
            outputDesc.depthBufferBits = 0;
            EnsureHdrTextureDesc(ref outputDesc);
            TextureHandle destination = renderGraph.CreateTexture(outputDesc);
            return AddIrisPass(renderGraph, source, destination, material, 2, parameters, screenRatio, runtimeLayer.settings, _profilingSampler, _passName, current);
        }

        private TextureHandle AddIrisPass(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            IrisBlurParameters parameters,
            float screenRatio,
            ShoostPostProcessLayer layer,
            ProfilingSampler passProfilingSampler,
            string label,
            TextureHandle blurredTexture = default)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{label} Iris", out PassData passData, passProfilingSampler))
            {
                passData.source = source;
                passData.layer = layer;
                passData.material = material;
                passData.passIndex = Mathf.Max(0, passIndex);
                passData.radius = parameters.radius;
                passData.screenRatio = screenRatio;
                passData.center = parameters.center;
                passData.centerSize = parameters.centerSize;
                passData.smoothness = parameters.smoothness;
                passData.distance = parameters.distance;
                passData.angle = parameters.angleRadians;
                passData.blurOffsetR = parameters.blurRadiusR;
                passData.blurOffsetG = parameters.blurRadiusG;
                passData.blurOffsetB = parameters.blurRadiusB;
                passData.enableRgbSplit = parameters.enableRgbSplit;
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
                    data.material.SetFloat(ShoostPostProcessShaderConstants.RadiusId, data.radius * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.ScreenRatioId, data.screenRatio);
                    data.material.SetVector(ShoostPostProcessShaderConstants.CenterId, new Vector4(data.center.x, data.center.y, 0.0f, 0.0f));
                    data.material.SetFloat(ShoostPostProcessShaderConstants.CenterSizeId, 1.0f - data.centerSize);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.SmoothnessId, data.smoothness);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetRId, data.blurOffsetR * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetGId, data.blurOffsetG * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.BlurOffsetBId, data.blurOffsetB * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.DistanceId, data.distance * 0.01f);
                    data.material.SetFloat(ShoostPostProcessShaderConstants.AngleId, data.angle);

                    if (data.enableRgbSplit)
                    {
                        data.material.EnableKeyword("ENABLE_RGBSPLIT");
                    }
                    else
                    {
                        data.material.DisableKeyword("ENABLE_RGBSPLIT");
                    }

                    if (data.blurredTexture.IsValid())
                    {
                        context.cmd.SetGlobalTexture(ShoostPostProcessShaderConstants.BlurredTexId, data.blurredTexture);
                    }

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

    }
}
