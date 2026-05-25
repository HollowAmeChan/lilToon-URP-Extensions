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
        private static int GetChangeFrameRateTargetFrameRate(ImageProcessLayer layer)
        {
            float value = layer.parameters0.x > 0.0f ? layer.parameters0.x : 12.0f;
            return Mathf.Clamp(Mathf.RoundToInt(value), 1, 60);
        }

        private ChangeFrameRateState GetChangeFrameRateState(int cameraId, RenderTextureDescriptor descriptor)
        {
            if (!changeFrameRateStates.TryGetValue(cameraId, out ChangeFrameRateState state))
            {
                state = new ChangeFrameRateState();
                changeFrameRateStates.Add(cameraId, state);
            }

            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            EnsureHdrDescriptor(ref descriptor);

            bool descriptorChanged = state.frozenTexture == null
                || state.width != descriptor.width
                || state.height != descriptor.height
                || state.volumeDepth != descriptor.volumeDepth
                || state.msaaSamples != descriptor.msaaSamples
                || state.dimension != descriptor.dimension
                || state.graphicsFormat != descriptor.graphicsFormat;

            if (descriptorChanged)
            {
                state.Release();
                state.width = descriptor.width;
                state.height = descriptor.height;
                state.volumeDepth = descriptor.volumeDepth;
                state.msaaSamples = descriptor.msaaSamples;
                state.dimension = descriptor.dimension;
                state.graphicsFormat = descriptor.graphicsFormat;
            }

            RenderingUtils.ReAllocateIfNeeded(ref state.frozenTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: $"_lilImageProcessChangeFrameRate_{cameraId}");
            return state;
        }

        private static bool ShouldRefreshChangeFrameRateState(ChangeFrameRateState state, ImageProcessLayer layer, out int targetFrameRate, out double now)
        {
            targetFrameRate = GetChangeFrameRateTargetFrameRate(layer);
            now = Time.realtimeSinceStartupAsDouble;
            if (state.targetFrameRate != targetFrameRate)
            {
                state.targetFrameRate = targetFrameRate;
                state.nextUpdateTime = 0.0;
                state.isValid = false;
            }

            return !state.isValid || now >= state.nextUpdateTime;
        }

        private static void MarkChangeFrameRateStateRefreshed(ChangeFrameRateState state, int targetFrameRate, double now)
        {
            state.isValid = true;
            state.nextUpdateTime = now + (1.0 / Mathf.Max(1, targetFrameRate));
        }

        private void ApplyChangeFrameRateLayer(CommandBuffer cmd, RenderTextureDescriptor sourceDescriptor, Camera camera, RTHandle source, RTHandle destination, ImageProcessRuntimeLayer runtimeLayer)
        {
            ImageProcessLayer layer = runtimeLayer.settings;
            Material material = runtimeLayer.material;
            int cameraId = camera != null ? camera.GetInstanceID() : 0;
            ChangeFrameRateState state = GetChangeFrameRateState(cameraId, sourceDescriptor);

            ApplyLayerProperties(layer, material);
            if (ShouldRefreshChangeFrameRateState(state, layer, out int targetFrameRate, out double now))
            {
                Blitter.BlitCameraTexture(cmd, source, state.frozenTexture, material, 0);
                MarkChangeFrameRateStateRefreshed(state, targetFrameRate, now);
            }

            cmd.SetGlobalTexture(ImageProcessShaderConstants.FrozenFrameTexId, state.frozenTexture);
            Blitter.BlitCameraTexture(cmd, source, destination, material, 1);
        }

        private TextureHandle RecordChangeFrameRateLayer(
            RenderGraph renderGraph,
            TextureHandle source,
            TextureHandle destination,
            ImageProcessRuntimeLayer runtimeLayer,
            int layerIndex,
            UniversalCameraData cameraData)
        {
            int cameraId = cameraData.camera != null ? cameraData.camera.GetInstanceID() : 0;
            ChangeFrameRateState state = GetChangeFrameRateState(cameraId, cameraData.cameraTargetDescriptor);
            TextureHandle frozenFrameTexture = renderGraph.ImportTexture(state.frozenTexture);

            if (ShouldRefreshChangeFrameRateState(state, runtimeLayer.settings, out int targetFrameRate, out double now))
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Change Frame Rate Capture", out PassData passData, _profilingSampler))
                {
                    passData.source = source;
                    passData.layer = runtimeLayer.settings;
                    passData.material = runtimeLayer.material;
                    passData.passIndex = 0;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(frozenFrameTexture, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ApplyLayerProperties(data.layer, data.material);
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                    });
                }

                MarkChangeFrameRateStateRefreshed(state, targetFrameRate, now);
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{_passName} Change Frame Rate", out PassData passData, _profilingSampler))
            {
                passData.source = source;
                passData.frozenFrameTexture = frozenFrameTexture;
                passData.layer = runtimeLayer.settings;
                passData.material = runtimeLayer.material;
                passData.passIndex = 1;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(frozenFrameTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    ApplyLayerProperties(data.layer, data.material);
                    context.cmd.SetGlobalTexture(ImageProcessShaderConstants.FrozenFrameTexId, data.frozenFrameTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                });
            }

            return destination;
        }

    }
}
