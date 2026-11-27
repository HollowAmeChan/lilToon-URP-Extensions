using lilToon.URP.Extensions.CharacterShadow;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal readonly struct HoLayeredShadowDrawInputs
    {
        internal readonly TextureHandle surface0, surface1, visibility0, visibility1, additional;
        internal readonly bool weightedOit;
        internal bool IsValid => surface0.IsValid() && surface1.IsValid() && visibility0.IsValid() && visibility1.IsValid();

        internal HoLayeredShadowDrawInputs(TextureHandle surface0, TextureHandle surface1,
            TextureHandle visibility0, TextureHandle visibility1, TextureHandle additional, bool weightedOit)
        {
            this.surface0 = surface0;
            this.surface1 = surface1;
            this.visibility0 = visibility0;
            this.visibility1 = visibility1;
            this.additional = additional;
            this.weightedOit = weightedOit;
        }

        internal void DeclareReads(IRasterRenderGraphBuilder builder)
        {
            if (!IsValid) return;
            builder.UseTexture(surface0, AccessFlags.Read);
            builder.UseTexture(surface1, AccessFlags.Read);
            builder.UseTexture(visibility0, AccessFlags.Read);
            builder.UseTexture(visibility1, AccessFlags.Read);
            if (additional.IsValid()) builder.UseTexture(additional, AccessFlags.Read);
        }

        internal void Clear(RenderGraph graph)
        {
            if (IsValid) HoTransparentScreenShadowPass.RecordClear(graph, weightedOit);
        }
    }

    internal sealed class HoTransparentScreenShadowPass : ScriptableRenderPass
    {
        private static readonly int CandidateId = Shader.PropertyToID("_HoTransparentShadowCandidate");
        private static readonly int NativeAtlasId = Shader.PropertyToID("_HoTransparentMainShadowAtlas");
        private static readonly int CharacterAtlasId = Shader.PropertyToID("_HoCSAtlas");
        private static readonly int ConfigurationId = Shader.PropertyToID("_HoTransparentShadowConfiguration");
        private static readonly int SizeId = Shader.PropertyToID("_HoTransparentShadowRasterSize");
        private static readonly BindingNames EyeBindings = new("_HoShadowsLayer");
        private static readonly BindingNames OitBindings = new("_HoShadowsOit");
        private const GraphicsFormat OutputFormat = GraphicsFormat.R16G16B16A16_SFloat;

        private readonly Material material;
        private readonly Material additionalMaterial;
        private readonly bool clear;

        private sealed class ResolveData
        {
            internal Material material;
            internal TextureHandle candidate, nativeAtlas, characterAtlas;
            internal Vector4 configuration, size;
        }

        private sealed class BindingData
        {
            internal bool active;
            internal BindingNames names;
            internal HoAdditionalShadowFrameData additional;
        }

        private readonly struct BindingNames
        {
            internal readonly int surface0, surface1, visibility0, visibility1, active;
            internal readonly int additionalVisibility, additionalActive, additionalSlots;

            internal BindingNames(string prefix)
            {
                surface0 = Shader.PropertyToID(prefix + "Surface0");
                surface1 = Shader.PropertyToID(prefix + "Surface1");
                visibility0 = Shader.PropertyToID(prefix + "Visibility0");
                visibility1 = Shader.PropertyToID(prefix + "Visibility1");
                active = Shader.PropertyToID(prefix + "Active");
                additionalVisibility = Shader.PropertyToID(prefix + "AdditionalVisibility");
                additionalActive = Shader.PropertyToID(prefix + "AdditionalActive");
                additionalSlots = Shader.PropertyToID(prefix + "AdditionalSlots");
            }
        }

        internal HoTransparentScreenShadowPass(Material material, Material additionalMaterial = null, bool clear = false)
        {
            this.material = material;
            this.additionalMaterial = additionalMaterial;
            this.clear = clear;
            renderPassEvent = clear ? RenderPassEvent.AfterRenderingTransparents : RenderPassEvent.BeforeRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (clear || !frameData.Contains<HoTransparentShadowSurfaceData>())
            {
                RecordClear(graph);
                return;
            }

            var surface = frameData.Get<HoTransparentShadowSurfaceData>();
            var camera = frameData.Get<UniversalCameraData>().camera;
            if (surface.CameraInstanceId != camera.GetInstanceID())
            {
                RecordClear(graph);
                return;
            }

            var inputs = RecordLayers(graph, frameData, surface.First, surface.Second);
            if (!inputs.IsValid)
            {
                RecordClear(graph);
                return;
            }
            var transparentDraw = frameData.GetOrCreate<HoTransparentShadowDrawData>();
            transparentDraw.RegisterTexture(inputs.surface0);
            transparentDraw.RegisterTexture(inputs.surface1);
            transparentDraw.RegisterTexture(inputs.visibility0);
            transparentDraw.RegisterTexture(inputs.visibility1);
            transparentDraw.RegisterTexture(inputs.additional);
        }

        internal HoLayeredShadowDrawInputs RecordLayers(RenderGraph graph, ContextContainer frameData,
            TextureHandle surface0, TextureHandle surface1, bool weightedOit = false)
        {
            if (!surface0.IsValid() || !surface1.IsValid()
                || !SystemInfo.IsFormatSupported(OutputFormat, GraphicsFormatUsage.Render | GraphicsFormatUsage.Sample))
                return default;

            var camera = frameData.Get<UniversalCameraData>().camera;

            var resources = frameData.Get<UniversalResourceData>();
            var shadows = frameData.Get<UniversalShadowData>();
            var lights = frameData.Get<UniversalLightData>();
            int mainIndex = lights.mainLightIndex;
            Light main = mainIndex >= 0 ? lights.visibleLights[mainIndex].light : null;
            bool nativeValid = main != null && main.type == LightType.Directional && main.shadows != LightShadows.None
                && main.shadowStrength > 0 && shadows.supportsMainLightShadows && resources.mainShadowsTexture.IsValid()
                && frameData.Get<UniversalRenderingData>().cullResults.GetShadowCasterBounds(mainIndex, out _);
            TextureHandle native = nativeValid ? resources.mainShadowsTexture : TextureHandle.nullHandle;
            TextureHandle character = frameData.Contains<HoCharacterShadowResources>()
                ? frameData.Get<HoCharacterShadowResources>().atlas : TextureHandle.nullHandle;
            var additional = additionalMaterial != null && frameData.Contains<HoAdditionalShadowFrameData>()
                ? frameData.Get<HoAdditionalShadowFrameData>() : null;
            bool additionalValid = additional != null && additional.CameraInstanceId == camera.GetInstanceID()
                && additional.Count != 0 && resources.additionalShadowsTexture.IsValid();
            if (!native.IsValid() && !character.IsValid() && !additionalValid)
                return default;

            var configuration = new Vector4(native.IsValid() ? shadows.mainLightShadowCascadesCount : 0,
                character.IsValid() ? 1 : 0, main != null && main.shadows == LightShadows.Soft && shadows.supportsSoftShadows ? 1 : 0, 0);
            TextureHandle first = RecordResolve(graph, surface0, native, character, configuration, "Ho Screen Shadow Layer 1");
            TextureHandle second = RecordResolve(graph, surface1, native, character, configuration, "Ho Screen Shadow Layer 2");
            TextureHandle additionalVisibility = additionalValid ? HoAdditionalScreenShadowPass.RecordVisibility(graph,
                additionalMaterial, lights, additional, resources.additionalShadowsTexture, surface0, surface1) : TextureHandle.nullHandle;
            var inputs = new HoLayeredShadowDrawInputs(surface0, surface1, first, second, additionalVisibility, weightedOit);
            RecordBindings(graph, inputs, additional, weightedOit);
            return inputs;
        }

        private TextureHandle RecordResolve(RenderGraph graph, TextureHandle candidate, TextureHandle native,
            TextureHandle character, Vector4 configuration, string name)
        {
            var desc = candidate.GetDescriptor(graph);
            desc.name = name;
            desc.colorFormat = OutputFormat;
            desc.clearBuffer = false;
            TextureHandle output = graph.CreateTexture(desc);
            using var builder = graph.AddRasterRenderPass<ResolveData>(name, out var pass);
            pass.material = material;
            pass.candidate = candidate;
            pass.nativeAtlas = native;
            pass.characterAtlas = character;
            pass.configuration = configuration;
            pass.size = new Vector4(desc.width, desc.height, 1f / desc.width, 1f / desc.height);
            builder.UseTexture(candidate, AccessFlags.Read);
            if (native.IsValid()) builder.UseTexture(native, AccessFlags.Read);
            if (character.IsValid()) builder.UseTexture(character, AccessFlags.Read);
            builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (ResolveData data, RasterGraphContext context) =>
            {
                var props = context.renderGraphPool.GetTempMaterialPropertyBlock();
                props.SetTexture(CandidateId, (Texture)data.candidate);
                if (data.nativeAtlas.IsValid()) props.SetTexture(NativeAtlasId, (Texture)data.nativeAtlas);
                if (data.characterAtlas.IsValid()) props.SetTexture(CharacterAtlasId, (Texture)data.characterAtlas);
                props.SetVector(ConfigurationId, data.configuration);
                props.SetVector(SizeId, data.size);
                CoreUtils.DrawFullScreen(context.cmd, data.material, props);
            });
            return output;
        }

        internal static void RecordClear(RenderGraph graph, bool weightedOit = false) => RecordBindings(graph, default, null, weightedOit);

        private static void RecordBindings(RenderGraph graph, HoLayeredShadowDrawInputs inputs,
            HoAdditionalShadowFrameData additional, bool weightedOit)
        {
            string name = weightedOit ? "Ho Bind OIT Screen Shadows" : "Ho Bind Eye Screen Shadows";
            using var builder = graph.AddRasterRenderPass<BindingData>(name, out var pass);
            var bindings = weightedOit ? OitBindings : EyeBindings;
            pass.names = bindings;
            pass.active = inputs.IsValid;
            pass.additional = inputs.additional.IsValid() ? additional : null;
            if (pass.active)
            {
                Publish(builder, inputs.surface0, bindings.surface0);
                Publish(builder, inputs.surface1, bindings.surface1);
                Publish(builder, inputs.visibility0, bindings.visibility0);
                Publish(builder, inputs.visibility1, bindings.visibility1);
            }
            if (pass.additional != null)
                Publish(builder, inputs.additional, bindings.additionalVisibility);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (BindingData data, RasterGraphContext context) =>
            {
                context.cmd.SetGlobalInt(data.names.active, data.active ? 1 : 0);
                context.cmd.SetGlobalInt(data.names.additionalActive, data.additional != null ? 1 : 0);
                if (data.additional != null)
                    HoOpaqueScreenShadowBindingPass.BindAdditionalSlots(context, data.additional, data.names.additionalSlots);
            });
        }

        private static void Publish(IRasterRenderGraphBuilder builder, TextureHandle texture, int name)
        {
            builder.UseTexture(texture, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(texture, name);
        }
    }
}
