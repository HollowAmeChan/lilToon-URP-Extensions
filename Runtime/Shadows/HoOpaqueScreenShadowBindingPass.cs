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
        private static readonly int RayTracedId = Shader.PropertyToID("_HoShadowsRayTracedVisibility");
        private static readonly int RayTracedActiveId = Shader.PropertyToID("_HoShadowsRayTracedActive");
        private static readonly int RayTracedMainSlotId = Shader.PropertyToID("_HoShadowsRayTracedMainSlot");
        private static readonly int RayTracedSlotsId = Shader.PropertyToID("_HoShadowsRayTracedSlots");

        // Four indices per vector. Matches the native desktop URP visible additional light limit.
        private const int SlotVectorCount = 64;
        private readonly bool clear;

        private sealed class PassData
        {
            internal bool mainActive, characterActive;
            internal HoAdditionalShadowFrameData additional;
            internal HoRayTracedScreenShadowFrameData rayTraced;
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
            var rayTraced = !clear && frameData.Contains<HoRayTracedScreenShadowFrameData>()
                ? frameData.Get<HoRayTracedScreenShadowFrameData>() : null;
            int cameraId = frameData.Get<UniversalCameraData>().camera.GetInstanceID();
            bool mainValid = main != null && main.CameraInstanceId == cameraId && main.IsValid && main.SurfaceDepth.IsValid();
            bool characterValid = mainValid && main.CharacterVisibility.IsValid();
            bool additionalValid = additional != null && additional.CameraInstanceId == cameraId
                && additional.Visibility.IsValid() && additional.SurfaceDepth.IsValid();
            bool rayTracedValid = rayTraced != null && rayTraced.CameraInstanceId == cameraId
                && rayTraced.Visibility.IsValid() && rayTraced.SurfaceDepth.IsValid();

            string passName = clear ? "Ho Clear Opaque Screen Shadows" : "Ho Bind Opaque Screen Shadows";
            using var builder = graph.AddRasterRenderPass<PassData>(passName, out var pass);
            pass.mainActive = mainValid;
            pass.characterActive = characterValid;
            pass.additional = additionalValid ? additional : null;
            pass.rayTraced = rayTracedValid ? rayTraced : null;
            if (mainValid)
                Publish(builder, main.Visibility, MainId);
            if (characterValid)
                Publish(builder, main.CharacterVisibility, CharacterId);
            if (additionalValid)
                Publish(builder, additional.Visibility, AdditionalId);
            if (rayTracedValid)
                Publish(builder, rayTraced.Visibility, RayTracedId);
            TextureHandle depth = mainValid ? main.SurfaceDepth
                : additionalValid ? additional.SurfaceDepth : rayTracedValid ? rayTraced.SurfaceDepth : TextureHandle.nullHandle;
            if (depth.IsValid())
                Publish(builder, depth, DepthId);

            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                var commands = context.cmd;
                commands.SetGlobalInt(MainActiveId, data.mainActive ? 1 : 0);
                commands.SetGlobalInt(CharacterActiveId, data.characterActive ? 1 : 0);
                commands.SetGlobalInt(AdditionalActiveId, data.additional != null ? 1 : 0);
                commands.SetGlobalInt(RayTracedActiveId, data.rayTraced != null ? 1 : 0);

                if (data.additional != null)
                {
                    var slots = context.renderGraphPool.GetTempArray<Vector4>(SlotVectorCount);
                    Array.Clear(slots, 0, SlotVectorCount);
                    for (int slot = 0; slot < data.additional.Count; slot++)
                    {
                        int index = data.additional.GetShaderLightIndex(slot);
                        if ((uint)index < SlotVectorCount * 4u)
                            slots[index >> 2][index & 3] = slot + 1;
                    }
                    commands.SetGlobalVectorArray(AdditionalSlotsId, slots);
                }
                if (data.rayTraced != null)
                {
                    var raySlots = context.renderGraphPool.GetTempArray<Vector4>(SlotVectorCount);
                    Array.Clear(raySlots, 0, SlotVectorCount);
                    int mainSlot = 0;
                    for (int slot = 0; slot < data.rayTraced.Count; slot++)
                    {
                        int index = data.rayTraced.GetLightIndex(slot);
                        if (index == HoRayTracedScreenShadowPass.MainLightIndex)
                            mainSlot = slot + 1;
                        else if ((uint)index < SlotVectorCount * 4u)
                            raySlots[index >> 2][index & 3] = slot + 1;
                    }
                    commands.SetGlobalInt(RayTracedMainSlotId, mainSlot);
                    commands.SetGlobalVectorArray(RayTracedSlotsId, raySlots);
                }
            });
        }

        private static void Publish(IRasterRenderGraphBuilder builder, TextureHandle texture, int property)
        {
            builder.UseTexture(texture, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(texture, property);
        }
    }
}
