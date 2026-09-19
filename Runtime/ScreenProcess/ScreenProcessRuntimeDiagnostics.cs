using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ScreenProcessRuntimeResourceRequirements
    {
        public readonly int ActiveLayerCount;
        public readonly bool RequiresMaskId;
        public readonly bool RequiresNormalDepth;
        public readonly bool RequiresSurfaceData;
        public readonly bool RequiresCustom0;
        public readonly bool RequiresObjectCustom0;
        public readonly bool RequiresObjectCustom1;
        public readonly bool RequiresSkyTexture;

        public ScreenProcessRuntimeResourceRequirements(
            int activeLayerCount,
            bool requiresMaskId,
            bool requiresNormalDepth,
            bool requiresSurfaceData,
            bool requiresCustom0,
            bool requiresObjectCustom0,
            bool requiresObjectCustom1,
            bool requiresSkyTexture)
        {
            ActiveLayerCount = activeLayerCount;
            RequiresMaskId = requiresMaskId;
            RequiresNormalDepth = requiresNormalDepth;
            RequiresSurfaceData = requiresSurfaceData;
            RequiresCustom0 = requiresCustom0;
            RequiresObjectCustom0 = requiresObjectCustom0;
            RequiresObjectCustom1 = requiresObjectCustom1;
            RequiresSkyTexture = requiresSkyTexture;
        }
    }

    public readonly struct ScreenProcessRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly string Stage;
        public readonly int ActiveLayerCount;
        public readonly int WrittenLayerCount;
        public readonly bool BackBufferActive;
        public readonly bool CameraColorAvailable;
        public readonly bool RequiresMetadataBuffer;
        public readonly bool MetadataBufferAvailable;
        public readonly bool RequiresGeometryBuffer;
        public readonly bool GeometryBufferAvailable;
        public readonly bool RequiresMaskId;
        public readonly bool MaskIdAvailable;
        public readonly bool RequiresSurfaceData;
        public readonly bool SurfaceDataAvailable;
        public readonly bool RequiresCustom0;
        public readonly bool Custom0Available;
        public readonly bool RequiresObjectCustom0;
        public readonly bool ObjectCustom0Available;
        public readonly bool RequiresObjectCustom1;
        public readonly bool ObjectCustom1Available;
        public readonly bool RequiresNormalDepth;
        public readonly bool NormalDepthAvailable;
        public readonly bool RequiresSkyTexture;
        public readonly bool SkyTextureAvailable;
        public readonly bool Ready;
        public readonly string Reason;

        internal ScreenProcessRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string cameraName,
            string stage,
            int activeLayerCount,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable,
            ScreenProcessRuntimeResourceRequirements requirements,
            bool maskIdAvailable,
            bool surfaceDataAvailable,
            bool custom0Available,
            bool objectCustom0Available,
            bool objectCustom1Available,
            bool normalDepthAvailable,
            bool skyTextureAvailable,
            bool ready,
            string reason)
        {
            IsValid = isValid;
            FrameCount = frameCount;
            CameraName = cameraName ?? string.Empty;
            Stage = stage ?? string.Empty;
            ActiveLayerCount = activeLayerCount;
            WrittenLayerCount = writtenLayerCount;
            BackBufferActive = backBufferActive;
            CameraColorAvailable = cameraColorAvailable;
            RequiresMaskId = requirements.RequiresMaskId;
            RequiresSurfaceData = requirements.RequiresSurfaceData;
            RequiresCustom0 = requirements.RequiresCustom0;
            RequiresObjectCustom0 = requirements.RequiresObjectCustom0;
            RequiresObjectCustom1 = requirements.RequiresObjectCustom1;
            RequiresNormalDepth = requirements.RequiresNormalDepth;
            RequiresSkyTexture = requirements.RequiresSkyTexture;
            MaskIdAvailable = maskIdAvailable;
            SurfaceDataAvailable = surfaceDataAvailable;
            Custom0Available = custom0Available;
            ObjectCustom0Available = objectCustom0Available;
            ObjectCustom1Available = objectCustom1Available;
            NormalDepthAvailable = normalDepthAvailable;
            SkyTextureAvailable = skyTextureAvailable;
            RequiresMetadataBuffer = RequiresMaskId
                || RequiresSurfaceData
                || RequiresCustom0
                || RequiresObjectCustom0
                || RequiresObjectCustom1;
            MetadataBufferAvailable = (!RequiresMaskId || MaskIdAvailable)
                && (!RequiresSurfaceData || SurfaceDataAvailable)
                && (!RequiresCustom0 || Custom0Available)
                && (!RequiresObjectCustom0 || ObjectCustom0Available)
                && (!RequiresObjectCustom1 || ObjectCustom1Available);
            RequiresGeometryBuffer = RequiresNormalDepth || RequiresSkyTexture;
            GeometryBufferAvailable = (!RequiresNormalDepth || NormalDepthAvailable)
                && (!RequiresSkyTexture || SkyTextureAvailable);
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class ScreenProcessRuntimeDiagnostics
    {
        private static readonly ScreenProcessRuntimeResourceRequirements EmptyRequirements =
            new ScreenProcessRuntimeResourceRequirements(0, false, false, false, false, false, false, false);

        private static readonly ScreenProcessRuntimeDiagnosticSnapshot EmptySnapshot =
            new ScreenProcessRuntimeDiagnosticSnapshot(
                false,
                0,
                string.Empty,
                string.Empty,
                0,
                0,
                false,
                false,
                EmptyRequirements,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                string.Empty);

        private static ScreenProcessRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static ScreenProcessRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static ScreenProcessRuntimeResourceRequirements AnalyzeRequirements(List<ScreenProcessRuntimeLayer> layers)
        {
            int activeLayerCount = 0;
            bool requiresMaskId = false;
            bool requiresNormalDepth = false;
            // The deleted rule sources were the only ScreenProcess consumers of these MetadataBuffer
            // channels, so they stay reported (availability) but are never required until AC lands.
            bool requiresSurfaceData = false;
            bool requiresCustom0 = false;
            bool requiresObjectCustom0 = false;
            bool requiresObjectCustom1 = false;
            bool requiresSkyTexture = false;

            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ScreenProcessRuntimeLayer runtimeLayer = layers[i];
                    ScreenProcessLayer layer = runtimeLayer != null ? runtimeLayer.settings : null;
                    if (layer == null || !layer.IsActive || runtimeLayer.material == null)
                    {
                        continue;
                    }

                    activeLayerCount++;
                    bool isEdgeLight = layer.effect == ScreenProcessEffect.EdgeLight;
                    bool isDropShadow = layer.effect == ScreenProcessEffect.DropShadow;
                    bool isOutline = layer.effect == ScreenProcessEffect.Outline;
                    bool isDepthOfField = layer.effect == ScreenProcessEffect.DepthOfField;
                    bool isPostLighting = layer.effect == ScreenProcessEffect.PostLighting;
                    bool isSkyTyndall = layer.effect == ScreenProcessEffect.SkyTyndall;
                    bool needsMask = isEdgeLight || isDropShadow || isPostLighting || layer.useMask || layer.debugMask;
                    if (needsMask)
                    {
                        requiresMaskId = true;
                    }

                    if (isEdgeLight || isOutline || isDepthOfField || isPostLighting || isSkyTyndall)
                    {
                        requiresNormalDepth = true;
                    }

                    if (isSkyTyndall)
                    {
                        requiresSkyTexture = true;
                    }
                }
            }

            return new ScreenProcessRuntimeResourceRequirements(
                activeLayerCount,
                requiresMaskId,
                requiresNormalDepth,
                requiresSurfaceData,
                requiresCustom0,
                requiresObjectCustom0,
                requiresObjectCustom1,
                requiresSkyTexture);
        }

        internal static void PublishSkipped(Camera camera, string stage, string reason)
        {
            currentSnapshot = new ScreenProcessRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                0,
                0,
                false,
                false,
                EmptyRequirements,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                reason);
        }

        internal static void PublishRenderGraphInputs(
            Camera camera,
            string stage,
            ScreenProcessRuntimeResourceRequirements requirements,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool maskIdAvailable,
            bool surfaceDataAvailable,
            bool custom0Available,
            bool objectCustom0Available,
            bool objectCustom1Available,
            bool normalDepthAvailable,
            bool skyTextureAvailable)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && (!requirements.RequiresMaskId || maskIdAvailable)
                && (!requirements.RequiresSurfaceData || surfaceDataAvailable)
                && (!requirements.RequiresCustom0 || custom0Available)
                && (!requirements.RequiresObjectCustom0 || objectCustom0Available)
                && (!requirements.RequiresObjectCustom1 || objectCustom1Available)
                && (!requirements.RequiresNormalDepth || normalDepthAvailable)
                && (!requirements.RequiresSkyTexture || skyTextureAvailable);

            currentSnapshot = new ScreenProcessRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                requirements.ActiveLayerCount,
                writtenLayerCount,
                backBufferActive,
                cameraColorAvailable,
                requirements,
                maskIdAvailable,
                surfaceDataAvailable,
                custom0Available,
                objectCustom0Available,
                objectCustom1Available,
                normalDepthAvailable,
                skyTextureAvailable,
                ready,
                ready ? "输入有效。" : BuildMissingInputReason(
                    requirements,
                    backBufferActive,
                    cameraColorAvailable,
                    maskIdAvailable,
                    surfaceDataAvailable,
                    custom0Available,
                    objectCustom0Available,
                    objectCustom1Available,
                    normalDepthAvailable,
                    skyTextureAvailable));
        }

        private static string BuildMissingInputReason(
            ScreenProcessRuntimeResourceRequirements requirements,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool maskIdAvailable,
            bool surfaceDataAvailable,
            bool custom0Available,
            bool objectCustom0Available,
            bool objectCustom1Available,
            bool normalDepthAvailable,
            bool skyTextureAvailable)
        {
            if (backBufferActive)
            {
                return "当前 active target 是 back buffer。";
            }

            if (!cameraColorAvailable)
            {
                return "camera color 不可用。";
            }

            bool metadataAvailable = (!requirements.RequiresMaskId || maskIdAvailable)
                && (!requirements.RequiresSurfaceData || surfaceDataAvailable)
                && (!requirements.RequiresCustom0 || custom0Available)
                && (!requirements.RequiresObjectCustom0 || objectCustom0Available)
                && (!requirements.RequiresObjectCustom1 || objectCustom1Available);
            bool geometryAvailable = (!requirements.RequiresNormalDepth || normalDepthAvailable)
                && (!requirements.RequiresSkyTexture || skyTextureAvailable);
            if (!metadataAvailable && !geometryAvailable)
            {
                return "MetadataBuffer 与 GeometryBuffer 不可用或不完整。";
            }

            if (!metadataAvailable)
            {
                return "MetadataBuffer 输入不完整。";
            }

            if (requirements.RequiresSkyTexture && !skyTextureAvailable)
            {
                return "GeometryBuffer sky texture is unavailable.";
            }

            return "GeometryBuffer normalDepth 不可用。";
        }
    }
}
