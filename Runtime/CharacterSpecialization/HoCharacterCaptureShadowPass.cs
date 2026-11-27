using lilToon.URP.Extensions.CharacterShadow;
using lilToon.URP.Extensions.Shadows;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal static class HoCharacterCaptureShadowPass
    {
        private static readonly int InputId = Shader.PropertyToID("_HoCaptureShadowResolveInput");
        private static readonly int NativeAtlasId = Shader.PropertyToID("_HoCaptureShadowResolveAtlas");
        private static readonly int CharacterAtlasId = Shader.PropertyToID("_HoCSAtlas");
        private static readonly int ConfigurationId = Shader.PropertyToID("_HoCaptureShadowResolveConfiguration");
        private static readonly int ResolveSizeId = Shader.PropertyToID("_HoCaptureShadowResolveSize");

        private static readonly int FaceSurfaceId = Shader.PropertyToID("_HoShadowsCaptureFaceSurface");
        private static readonly int EyeSurfaceId = Shader.PropertyToID("_HoShadowsCaptureEyeSurface");
        private static readonly int FaceVisibilityId = Shader.PropertyToID("_HoShadowsCaptureFaceVisibility");
        private static readonly int EyeVisibilityId = Shader.PropertyToID("_HoShadowsCaptureEyeVisibility");
        private static readonly int FaceActiveId = Shader.PropertyToID("_HoShadowsCaptureFaceActive");
        private static readonly int EyeActiveId = Shader.PropertyToID("_HoShadowsCaptureEyeActive");
        private static readonly int DrawSizeId = Shader.PropertyToID("_HoShadowsCaptureSize");
        private static readonly int AdditionalVisibilityId = Shader.PropertyToID("_HoShadowsCaptureAdditionalVisibility");
        private static readonly int AdditionalActiveId = Shader.PropertyToID("_HoShadowsCaptureAdditionalActive");
        private static readonly int AdditionalSlotsId = Shader.PropertyToID("_HoShadowsCaptureAdditionalSlots");
        private static readonly int LayerCountId = Shader.PropertyToID("_HoShadowsCaptureLayerCount");
        private const GraphicsFormat OutputFormat = GraphicsFormat.R16G16B16A16_SFloat;

        internal struct DrawInputs
        {
            internal Vector4 rasterSize;
            internal bool main;
            internal HoAdditionalShadowFrameData additional;
            internal int layerCount;
        }

        private sealed class ResolveData
        {
            public Material material;
            public TextureHandle input, nativeAtlas, characterAtlas;
            public Vector4 configuration, size;
        }

        internal static void Record(RenderGraph graph, ContextContainer frame, Material material, Material additionalMaterial,
            HoCharacterCaptureInputFrameData inputs)
        {
            if (inputs == null || !inputs.FaceSurface.IsValid())
                return;

            var camera = frame.Get<UniversalCameraData>().camera;
            if (camera == null || camera.GetInstanceID() != inputs.CameraInstanceId)
                return;

            var resources = frame.Get<UniversalResourceData>();
            var lights = frame.Get<UniversalLightData>();
            RecordAdditional(graph, frame, additionalMaterial, inputs, resources.additionalShadowsTexture, lights);
            if (material == null || !SystemInfo.IsFormatSupported(OutputFormat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return;

            var shadows = frame.Get<UniversalShadowData>();
            int mainIndex = lights.mainLightIndex;
            Light main = (uint)mainIndex < (uint)lights.visibleLights.Length ? lights.visibleLights[mainIndex].light : null;
            bool nativeValid = main != null && main.type == LightType.Directional && main.shadows != LightShadows.None
                && main.shadowStrength > 0 && shadows.supportsMainLightShadows && resources.mainShadowsTexture.IsValid()
                && frame.Get<UniversalRenderingData>().cullResults.GetShadowCasterBounds(mainIndex, out _);
            TextureHandle native = nativeValid ? resources.mainShadowsTexture : TextureHandle.nullHandle;
            TextureHandle character = frame.Contains<HoCharacterShadowResources>()
                ? frame.Get<HoCharacterShadowResources>().atlas : TextureHandle.nullHandle;
            if (!native.IsValid() && !character.IsValid())
                return;

            var configuration = new Vector4(nativeValid ? shadows.mainLightShadowCascadesCount : 0,
                character.IsValid() ? 1 : 0, main != null && main.shadows == LightShadows.Soft && shadows.supportsSoftShadows ? 1 : 0, 0);
            inputs.FaceVisibility = RecordResolve(graph, material, inputs.FaceSurface, native, character, configuration,
                FaceVisibilityId, "Ho Capture Face Shadow Resolve");
            if (inputs.EyeSurface.IsValid())
                inputs.EyeVisibility = RecordResolve(graph, material, inputs.EyeSurface, native, character, configuration,
                    EyeVisibilityId, "Ho Capture Eye Shadow Resolve");
        }

        private static TextureHandle RecordResolve(RenderGraph graph, Material material, TextureHandle input, TextureHandle native,
            TextureHandle character, Vector4 configuration, int visibilityId, string name)
        {
            var descriptor = input.GetDescriptor(graph);
            descriptor.name = name;
            descriptor.colorFormat = OutputFormat;
            descriptor.clearBuffer = false;
            TextureHandle output = graph.CreateTexture(descriptor);
            using var builder = graph.AddRasterRenderPass<ResolveData>(name, out var pass);
            pass.material = material;
            pass.input = input;
            pass.nativeAtlas = native;
            pass.characterAtlas = character;
            pass.configuration = configuration;
            pass.size = RasterSize(descriptor.width, descriptor.height);
            builder.UseTexture(input, AccessFlags.Read);
            if (native.IsValid()) builder.UseTexture(native, AccessFlags.Read);
            if (character.IsValid()) builder.UseTexture(character, AccessFlags.Read);
            builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
            builder.SetGlobalTextureAfterPass(output, visibilityId);
            builder.SetRenderFunc(static (ResolveData data, RasterGraphContext context) =>
            {
                var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
                properties.SetTexture(InputId, (Texture)data.input);
                if (data.nativeAtlas.IsValid()) properties.SetTexture(NativeAtlasId, (Texture)data.nativeAtlas);
                if (data.characterAtlas.IsValid()) properties.SetTexture(CharacterAtlasId, (Texture)data.characterAtlas);
                properties.SetVector(ConfigurationId, data.configuration);
                properties.SetVector(ResolveSizeId, data.size);
                CoreUtils.DrawFullScreen(context.cmd, data.material, properties);
            });

            return output;
        }

        private sealed class PublishData { }

        private static void RecordAdditional(RenderGraph graph, ContextContainer frame, Material material,
            HoCharacterCaptureInputFrameData inputs, TextureHandle atlas, UniversalLightData lights)
        {
            if (material == null || !atlas.IsValid() || !frame.Contains<HoAdditionalShadowFrameData>()
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return;

            var additional = frame.Get<HoAdditionalShadowFrameData>();
            if (additional.Count == 0 || additional.CameraInstanceId != inputs.CameraInstanceId)
                return;
            TextureHandle output = HoAdditionalScreenShadowPass.RecordVisibility(graph, material, lights, additional,
                atlas, inputs.FaceSurface, inputs.EyeSurface);
            if (!output.IsValid())
                return;

            inputs.AdditionalVisibility = output;
            inputs.AdditionalLights = additional;
            inputs.AdditionalLayerCount = inputs.EyeSurface.IsValid() ? 2 : 1;
            using var builder = graph.AddRasterRenderPass<PublishData>("Ho Capture Bind Additional Shadows", out _);
            builder.UseTexture(output, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(output, AdditionalVisibilityId);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PublishData _, RasterGraphContext __) => { });
        }

        internal static void PublishSurface(IRasterRenderGraphBuilder builder, TextureHandle surface, bool eye)
        {
            builder.SetGlobalTextureAfterPass(surface, eye ? EyeSurfaceId : FaceSurfaceId);
        }

        internal static DrawInputs UseDrawInputs(IRasterRenderGraphBuilder builder, HoCharacterCaptureInputFrameData inputs, bool eye)
        {
            if (inputs == null)
                return default;
            TextureHandle surface = eye ? inputs.EyeSurface : inputs.FaceSurface;
            TextureHandle visibility = eye ? inputs.EyeVisibility : inputs.FaceVisibility;
            bool main = visibility.IsValid();
            bool additional = inputs.AdditionalVisibility.IsValid();
            if (!surface.IsValid() || (!main && !additional))
                return default;

            builder.UseTexture(surface, AccessFlags.Read);
            if (main) builder.UseTexture(visibility, AccessFlags.Read);
            if (additional) builder.UseTexture(inputs.AdditionalVisibility, AccessFlags.Read);
            return new DrawInputs
            {
                rasterSize = RasterSize(inputs.Width, inputs.Height),
                main = main,
                additional = additional ? inputs.AdditionalLights : null,
                layerCount = additional ? inputs.AdditionalLayerCount : 0
            };
        }

        internal static void BeginDraw(RasterGraphContext context, DrawInputs inputs, bool eye)
        {
            if (inputs.rasterSize.x == 0)
                return;
            var commands = context.cmd;
            commands.SetGlobalVector(DrawSizeId, inputs.rasterSize);
            if (inputs.main)
                commands.SetGlobalInt(eye ? EyeActiveId : FaceActiveId, 1);
            if (inputs.additional != null)
            {
                commands.SetGlobalInt(LayerCountId, inputs.layerCount);
                HoOpaqueScreenShadowBindingPass.BindAdditionalSlots(context, inputs.additional, AdditionalSlotsId);
                commands.SetGlobalInt(AdditionalActiveId, 1);
            }
        }

        internal static void EndDraw(RasterCommandBuffer commands, DrawInputs inputs, bool eye)
        {
            if (inputs.main)
                commands.SetGlobalInt(eye ? EyeActiveId : FaceActiveId, 0);
            if (inputs.additional != null)
                commands.SetGlobalInt(AdditionalActiveId, 0);
        }

        private static Vector4 RasterSize(int width, int height) => new Vector4(width, height, 1f / width, 1f / height);
    }
}
