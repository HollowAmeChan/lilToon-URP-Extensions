#pragma warning disable CS0618

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal static class HoShadowCastPublisher
    {
        // Punctual arrays are uploaded at the fixed contract lengths for every tier. Unity caches the
        // length of a global array property for the session, so uploading a tier sized array (and
        // growing it later) triggers "exceeds previous array size ... Restart Unity to recreate the
        // arrays" and silently caps the data; the tier only bounds the collection and the shader loops.
        private static readonly Vector4[] WorldToShadowRow0 = new Vector4[HoShadowCastShaderContract.ArraySlices];
        private static readonly Vector4[] WorldToShadowRow1 = new Vector4[HoShadowCastShaderContract.ArraySlices];
        private static readonly Vector4[] WorldToShadowRow2 = new Vector4[HoShadowCastShaderContract.ArraySlices];
        private static readonly Vector4[] WorldToShadowRow3 = new Vector4[HoShadowCastShaderContract.ArraySlices];
        private static readonly Vector4[] LightData0 = new Vector4[HoShadowCastShaderContract.ArrayLights];
        private static readonly Vector4[] LightData1 = new Vector4[HoShadowCastShaderContract.ArrayLights];
        private static readonly Vector4[] LightData2 = new Vector4[HoShadowCastShaderContract.ArrayLights];
        private static readonly Vector4[] LightAttenuation = new Vector4[HoShadowCastShaderContract.ArrayLights];
        private static readonly Vector4[] LightColor = new Vector4[HoShadowCastShaderContract.ArrayLights];
        private static readonly Vector4[] SliceData = new Vector4[HoShadowCastShaderContract.ArraySlices];

        // Second directional arrays are not tiered.
        private static readonly Vector4[] SecondDirectionalWorldToShadowRow0 = new Vector4[HoShadowCastShaderContract.SecondDirectionalSlices];
        private static readonly Vector4[] SecondDirectionalWorldToShadowRow1 = new Vector4[HoShadowCastShaderContract.SecondDirectionalSlices];
        private static readonly Vector4[] SecondDirectionalWorldToShadowRow2 = new Vector4[HoShadowCastShaderContract.SecondDirectionalSlices];
        private static readonly Vector4[] SecondDirectionalWorldToShadowRow3 = new Vector4[HoShadowCastShaderContract.SecondDirectionalSlices];
        private static readonly Vector4[] SecondDirectionalLightData = new Vector4[HoShadowCastShaderContract.SecondDirectionalLights];
        private static readonly Vector4[] SecondDirectionalSliceData = new Vector4[HoShadowCastShaderContract.SecondDirectionalSlices];

        /// <summary>
        /// Selects the material side capacity tier. It bounds the collected light/slice count and the
        /// shader sampling loops; the uploaded array layout stays constant, so this is safe to change
        /// at runtime (no domain reload or editor restart needed).
        /// </summary>
        public static void ApplyCapacityKeywords(HoShadowCastLightCapacity capacity)
        {
            Shader.SetKeyword(HoShadowCastShaderConstants.CapacityMediumKeyword, capacity == HoShadowCastLightCapacity.Medium);
            Shader.SetKeyword(HoShadowCastShaderConstants.CapacityHighKeyword, capacity == HoShadowCastLightCapacity.High);
        }

        /// <summary>
        /// Same as <see cref="ApplyCapacityKeywords(HoShadowCastLightCapacity)"/>, but recorded in the
        /// pass command buffer so the keyword is applied in camera order together with the atlas data.
        /// </summary>
        public static void ApplyCapacityKeywords(CommandBuffer cmd, HoShadowCastLightCapacity capacity)
        {
            cmd.SetKeyword(HoShadowCastShaderConstants.CapacityMediumKeyword, capacity == HoShadowCastLightCapacity.Medium);
            cmd.SetKeyword(HoShadowCastShaderConstants.CapacityHighKeyword, capacity == HoShadowCastLightCapacity.High);
        }

        /// <summary>RenderGraph variant of <see cref="ApplyCapacityKeywords(CommandBuffer, HoShadowCastLightCapacity)"/>.</summary>
        public static void ApplyCapacityKeywords(RasterCommandBuffer cmd, HoShadowCastLightCapacity capacity)
        {
            cmd.SetKeyword(HoShadowCastShaderConstants.CapacityMediumKeyword, capacity == HoShadowCastLightCapacity.Medium);
            cmd.SetKeyword(HoShadowCastShaderConstants.CapacityHighKeyword, capacity == HoShadowCastLightCapacity.High);
        }

        public static void ResetAllImmediate()
        {
            SetGlobalEmpty();
            SetSecondDirectionalGlobalEmpty();
        }

        public static void ApplyGlobalData(CommandBuffer cmd, HoShadowCastFrame frame, RenderTargetIdentifier atlas)
        {
            CopyFrameArrays(frame);
            cmd.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, frame.lightCount > 0 ? 1.0f : 0.0f);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, frame.lightCount);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, frame.sliceCount);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.AtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / frame.atlasSize, 1.0f / frame.atlasSize));
            cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, atlas);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow0Id, WorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow1Id, WorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow2Id, WorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow3Id, WorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightAttenuationId, LightAttenuation);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightColorId, LightColor);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SliceDataId, SliceData);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParamsId, frame.pcssParams);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, frame.pcssParams2);
        }

        public static void ApplyGlobalData(RasterCommandBuffer cmd, HoShadowCastFrame frame)
        {
            CopyFrameArrays(frame);
            cmd.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, frame.lightCount > 0 ? 1.0f : 0.0f);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, frame.lightCount);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, frame.sliceCount);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.AtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / frame.atlasSize, 1.0f / frame.atlasSize));
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow0Id, WorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow1Id, WorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow2Id, WorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow3Id, WorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightAttenuationId, LightAttenuation);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightColorId, LightColor);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SliceDataId, SliceData);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParamsId, frame.pcssParams);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, frame.pcssParams2);
        }

        public static void SetGlobalEmpty()
        {
            Shader.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, 0);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, 0);
            Shader.SetGlobalVector(HoShadowCastShaderConstants.PcssParamsId, Vector4.zero);
            Shader.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, Vector4.zero);
        }

        public static void SetGlobalEmpty(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, 0.0f);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, 0);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, 0);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParamsId, Vector4.zero);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, Vector4.zero);
        }

        public static void SetSecondDirectionalGlobalEmpty()
        {
            Shader.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalParamsId, Vector4.zero);
            Shader.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalPcssParamsId, Vector4.zero);
        }

        public static void SetSecondDirectionalGlobalEmpty(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalParamsId, Vector4.zero);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalPcssParamsId, Vector4.zero);
        }

        public static void ApplySecondDirectionalGlobalData(CommandBuffer cmd, HoShadowCastSecondDirectionalFrame frame, RenderTargetIdentifier atlas)
        {
            CopySecondDirectionalFrameArrays(frame);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalParamsId, new Vector4(frame.lightCount > 0 ? 1.0f : 0.0f, frame.lightCount, frame.cascadeCountPerLight, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalCameraPositionId, new Vector4(frame.cameraPosition.x, frame.cameraPosition.y, frame.cameraPosition.z, 1.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalAtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / Mathf.Max(1, frame.atlasSize), 1.0f / Mathf.Max(1, frame.atlasSize)));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalPcssParamsId, frame.pcssParams);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, frame.pcssParams2);
            cmd.SetGlobalTexture(HoShadowCastShaderConstants.SecondDirectionalAtlasTextureId, atlas);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow0Id, SecondDirectionalWorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow1Id, SecondDirectionalWorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow2Id, SecondDirectionalWorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow3Id, SecondDirectionalWorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalLightDataId, SecondDirectionalLightData);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalSliceDataId, SecondDirectionalSliceData);
        }

        public static void ApplySecondDirectionalGlobalData(RasterCommandBuffer cmd, HoShadowCastSecondDirectionalFrame frame)
        {
            CopySecondDirectionalFrameArrays(frame);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalParamsId, new Vector4(frame.lightCount > 0 ? 1.0f : 0.0f, frame.lightCount, frame.cascadeCountPerLight, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalCameraPositionId, new Vector4(frame.cameraPosition.x, frame.cameraPosition.y, frame.cameraPosition.z, 1.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalAtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / Mathf.Max(1, frame.atlasSize), 1.0f / Mathf.Max(1, frame.atlasSize)));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.SecondDirectionalPcssParamsId, frame.pcssParams);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.PcssParams2Id, frame.pcssParams2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow0Id, SecondDirectionalWorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow1Id, SecondDirectionalWorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow2Id, SecondDirectionalWorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalWorldToShadowRow3Id, SecondDirectionalWorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalLightDataId, SecondDirectionalLightData);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SecondDirectionalSliceDataId, SecondDirectionalSliceData);
        }

        private static void CopyFrameArrays(HoShadowCastFrame frame)
        {
            for (int i = 0; i < HoShadowCastShaderContract.ArraySlices; i++)
            {
                Matrix4x4 worldToShadow = frame.worldToShadow[i];
                WorldToShadowRow0[i] = worldToShadow.GetRow(0);
                WorldToShadowRow1[i] = worldToShadow.GetRow(1);
                WorldToShadowRow2[i] = worldToShadow.GetRow(2);
                WorldToShadowRow3[i] = worldToShadow.GetRow(3);
                SliceData[i] = frame.sliceData[i];
            }

            for (int i = 0; i < HoShadowCastShaderContract.ArrayLights; i++)
            {
                LightData0[i] = frame.lightData0[i];
                LightData1[i] = frame.lightData1[i];
                LightData2[i] = frame.lightData2[i];
                LightAttenuation[i] = frame.lightAttenuation[i];
                LightColor[i] = frame.lightColor[i];
            }
        }

        private static void CopySecondDirectionalFrameArrays(HoShadowCastSecondDirectionalFrame frame)
        {
            for (int i = 0; i < HoShadowCastShaderContract.SecondDirectionalSlices; i++)
            {
                SecondDirectionalWorldToShadowRow0[i] = frame.worldToShadow[i].GetRow(0);
                SecondDirectionalWorldToShadowRow1[i] = frame.worldToShadow[i].GetRow(1);
                SecondDirectionalWorldToShadowRow2[i] = frame.worldToShadow[i].GetRow(2);
                SecondDirectionalWorldToShadowRow3[i] = frame.worldToShadow[i].GetRow(3);
                SecondDirectionalSliceData[i] = frame.sliceData[i];
            }

            for (int i = 0; i < HoShadowCastShaderContract.SecondDirectionalLights; i++)
            {
                SecondDirectionalLightData[i] = frame.lightData[i];
            }
        }
    }
}
