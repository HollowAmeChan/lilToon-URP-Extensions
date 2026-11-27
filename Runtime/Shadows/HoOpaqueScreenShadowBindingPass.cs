using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    internal sealed class HoOpaqueScreenShadowBindingPass : ScriptableRenderPass
    {
        private static readonly int MainId = Shader.PropertyToID("_HoShadowsMainVisibility");
        private static readonly int CharacterId = Shader.PropertyToID("_HoShadowsCharacterVisibility");
        private static readonly int AdditionalId = Shader.PropertyToID("_HoShadowsAdditionalVisibility");
        private static readonly int DepthId = Shader.PropertyToID("_HoShadowsSurfaceDepth");
        private static readonly int MainActiveId = Shader.PropertyToID("_HoShadowsMainActive");
        private static readonly int CharacterActiveId = Shader.PropertyToID("_HoShadowsCharacterActive");
        private static readonly int AdditionalActiveId = Shader.PropertyToID("_HoShadowsAdditionalActive");
        private static readonly int AdditionalSlotsId = Shader.PropertyToID("_HoShadowsAdditionalSlots");

        // Four indices per vector. Matches the native desktop URP visible additional light limit.
        private const int SlotVectorCount = 64;
        private readonly bool clear;

        private sealed class PassData
        {
            internal bool mainActive, characterActive;
            internal HoAdditionalShadowFrameData additional;
        }

        internal HoOpaqueScreenShadowBindingPass(bool clear)
        {
            this.clear = clear;
            renderPassEvent = clear ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.BeforeRenderingOpaques;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var main = !clear && frameData.Contains<HoNativeMainShadowFrameData>()
                ? frameData.Get<HoNativeMainShadowFrameData>() : null;
            var additional = !clear && frameData.Contains<HoAdditionalShadowFrameData>()
                ? frameData.Get<HoAdditionalShadowFrameData>() : null;
            int cameraId = frameData.Get<UniversalCameraData>().camera.GetInstanceID();
            bool mainValid = main != null && main.CameraInstanceId == cameraId && main.IsValid && main.SurfaceDepth.IsValid();
            bool characterValid = mainValid && main.CharacterVisibility.IsValid();
            bool additionalValid = additional != null && additional.CameraInstanceId == cameraId
                && additional.Visibility.IsValid() && additional.SurfaceDepth.IsValid();

            string passName = clear ? "Ho Clear Opaque Screen Shadows" : "Ho Bind Opaque Screen Shadows";
            using var builder = graph.AddRasterRenderPass<PassData>(passName, out var pass);
            pass.mainActive = mainValid;
            pass.characterActive = characterValid;
            pass.additional = additionalValid ? additional : null;
            if (mainValid)
                Publish(builder, main.Visibility, MainId);
            if (characterValid)
                Publish(builder, main.CharacterVisibility, CharacterId);
            if (additionalValid)
                Publish(builder, additional.Visibility, AdditionalId);
            if (mainValid || additionalValid)
                Publish(builder, mainValid ? main.SurfaceDepth : additional.SurfaceDepth, DepthId);

            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                var commands = context.cmd;
                commands.SetGlobalInt(MainActiveId, data.mainActive ? 1 : 0);
                commands.SetGlobalInt(CharacterActiveId, data.characterActive ? 1 : 0);
                commands.SetGlobalInt(AdditionalActiveId, data.additional != null ? 1 : 0);
                if (data.additional == null)
                    return;

                var slots = context.renderGraphPool.GetTempArray<Vector4>(SlotVectorCount);
                Array.Clear(slots, 0, SlotVectorCount);
                for (int slot = 0; slot < data.additional.Count; slot++)
                {
                    int index = data.additional.GetShaderLightIndex(slot);
                    if ((uint)index < SlotVectorCount * 4u)
                        slots[index >> 2][index & 3] = slot + 1;
                }
                commands.SetGlobalVectorArray(AdditionalSlotsId, slots);
            });
        }

        private static void Publish(IRasterRenderGraphBuilder builder, TextureHandle texture, int property)
        {
            builder.UseTexture(texture, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(texture, property);
        }
    }
}
