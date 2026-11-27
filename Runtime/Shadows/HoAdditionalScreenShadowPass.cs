using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoAdditionalScreenShadowPass : ScriptableRenderPass
    {
        private static readonly int DepthId = Shader.PropertyToID("_HoAdditionalScreenShadowDepth");
        private static readonly int AtlasId = Shader.PropertyToID("_HoAdditionalScreenShadowAtlas");
        private static readonly int LightIndexId = Shader.PropertyToID("_HoAdditionalScreenShadowLightIndex");
        private static readonly int LightPositionId = Shader.PropertyToID("_HoAdditionalScreenShadowLightPosition");
        private const GraphicsFormat OutputFormat = GraphicsFormat.R16_SFloat;

        private readonly Material material;
        internal int Budget { get; set; }

        private sealed class PassData
        {
            internal Material material;
            internal TextureHandle depth, atlas;
            internal int shaderIndex;
            internal Vector4 position;
        }

        internal HoAdditionalScreenShadowPass(Material material)
        {
            this.material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (!frameData.Contains<AdditionalLightShadowAtlasData>() || !SystemInfo.supports2DArrayTextures
                || !SystemInfo.IsFormatSupported(OutputFormat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return;

            var native = frameData.Get<AdditionalLightShadowAtlasData>();
            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle depth = resources.cameraDepthTexture;
            TextureHandle atlas = resources.additionalShadowsTexture;
            if (!native.UsesForwardLightIndices || native.Count == 0 || !depth.IsValid() || !atlas.IsValid())
                return;

            var camera = frameData.Get<UniversalCameraData>().camera;
            var lights = frameData.Get<UniversalLightData>().visibleLights;
            var product = frameData.GetOrCreate<HoAdditionalShadowFrameData>();
            int capacity = Mathf.Min(Budget, SystemInfo.maxTextureArraySlices);
            for (int rank = 0; rank < native.Count && product.Count < capacity; rank++)
            {
                var entry = native[rank];
                if (entry.VisibleLightIndex < 0 || entry.VisibleLightIndex >= lights.Length)
                    continue;
                var type = lights[entry.VisibleLightIndex].lightType;
                // URP's point comparison addresses first slice + cubemap face. Require all six before using it.
                if ((type == LightType.Spot && entry.ShadowSliceMask == 1u)
                    || (type == LightType.Point && entry.ShadowSliceMask == 0x3fu))
                    product.Add(entry);
            }
            if (product.Count == 0)
                return;

            product.Visibility = RecordVisibility(graph, material, frameData.Get<UniversalLightData>(), product, atlas, depth);
            product.SurfaceDepth = depth;
            product.CameraInstanceId = camera.GetInstanceID();
        }

        internal static TextureHandle RecordVisibility(RenderGraph graph, Material material,
            UniversalLightData lightData, HoAdditionalShadowFrameData product, TextureHandle atlas,
            TextureHandle firstSurface, TextureHandle secondSurface = default)
        {
            int layerCount = secondSurface.IsValid() ? 2 : 1;
            var desc = firstSurface.GetDescriptor(graph);
            desc.name = layerCount == 1 ? "Ho Additional Screen Shadow Visibility" : "Ho Transparent Additional Shadow Visibility";
            desc.depthBufferBits = DepthBits.None;
            desc.colorFormat = OutputFormat;
            desc.dimension = TextureDimension.Tex2DArray;
            desc.slices = product.Count * layerCount;
            desc.msaaSamples = MSAASamples.None;
            desc.bindTextureMS = false;
            desc.filterMode = FilterMode.Point;
            desc.wrapMode = TextureWrapMode.Clamp;
            desc.clearBuffer = false;
            if (desc.slices > SystemInfo.maxTextureArraySlices)
                return TextureHandle.nullHandle;

            TextureHandle output = graph.CreateTexture(desc);
            for (int slot = 0; slot < product.Count; slot++)
            {
                for (int layer = 0; layer < layerCount; layer++)
                {
                    using var builder = graph.AddRasterRenderPass<PassData>(desc.name, out var pass);
                    pass.material = material;
                    pass.depth = layer == 0 ? firstSurface : secondSurface;
                    pass.atlas = atlas;
                    pass.shaderIndex = product.GetShaderLightIndex(slot);
                    pass.position = lightData.visibleLights[product.GetVisibleLightIndex(slot)].localToWorldMatrix.GetColumn(3);
                    builder.UseTexture(pass.depth, AccessFlags.Read);
                    builder.UseTexture(atlas, AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0, AccessFlags.Write, 0, slot * layerCount + layer);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                        properties.SetTexture(DepthId, (Texture)data.depth);
                        properties.SetTexture(AtlasId, (Texture)data.atlas);
                        properties.SetInteger(LightIndexId, data.shaderIndex);
                        properties.SetVector(LightPositionId, data.position);
                        CoreUtils.DrawFullScreen(context.cmd, data.material, properties);
                    });
                }
            }
            return output;
        }
    }
}
