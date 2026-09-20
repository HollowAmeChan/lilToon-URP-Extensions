using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ScreenProcessRuntimeResourceRequirements
    {
        public readonly int ActiveLayerCount;
        public readonly bool RequiresCoverage;
        public readonly bool RequiresNormalDepth;
        public readonly bool RequiresSkyTexture;

        public ScreenProcessRuntimeResourceRequirements(
            int activeLayerCount,
            bool requiresCoverage,
            bool requiresNormalDepth,
            bool requiresSkyTexture)
        {
            ActiveLayerCount = activeLayerCount;
            RequiresCoverage = requiresCoverage;
            RequiresNormalDepth = requiresNormalDepth;
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
        public readonly bool RequiresGeometryBuffer;
        public readonly bool GeometryBufferAvailable;
        public readonly bool RequiresCoverage;
        public readonly bool CoverageAvailable;
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
            bool coverageAvailable,
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
            RequiresCoverage = requirements.RequiresCoverage;
            RequiresNormalDepth = requirements.RequiresNormalDepth;
            RequiresSkyTexture = requirements.RequiresSkyTexture;
            CoverageAvailable = coverageAvailable;
            NormalDepthAvailable = normalDepthAvailable;
            SkyTextureAvailable = skyTextureAvailable;
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
            new ScreenProcessRuntimeResourceRequirements(0, false, false, false);

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
                string.Empty);

        private static ScreenProcessRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static ScreenProcessRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static ScreenProcessRuntimeResourceRequirements AnalyzeRequirements(List<ScreenProcessRuntimeLayer> layers)
        {
            int activeLayerCount = 0;
            bool requiresCoverage = false;
            bool requiresNormalDepth = false;
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
                        requiresCoverage = true;
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
                requiresCoverage,
                requiresNormalDepth,
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
                reason);
        }

        internal static void PublishRenderGraphInputs(
            Camera camera,
            string stage,
            ScreenProcessRuntimeResourceRequirements requirements,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool coverageAvailable,
            bool normalDepthAvailable,
            bool skyTextureAvailable)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && (!requirements.RequiresCoverage || coverageAvailable)
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
                coverageAvailable,
                normalDepthAvailable,
                skyTextureAvailable,
                ready,
                ready ? "输入有效。" : BuildMissingInputReason(
                    requirements,
                    backBufferActive,
                    cameraColorAvailable,
                    coverageAvailable,
                    normalDepthAvailable,
                    skyTextureAvailable));
        }

        private static string BuildMissingInputReason(
            ScreenProcessRuntimeResourceRequirements requirements,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool coverageAvailable,
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

            bool coverageReady = !requirements.RequiresCoverage || coverageAvailable;
            bool geometryAvailable = (!requirements.RequiresNormalDepth || normalDepthAvailable)
                && (!requirements.RequiresSkyTexture || skyTextureAvailable);
            if (!coverageReady && !geometryAvailable)
            {
                return "角色覆盖率（AC/OB）与 GeometryBuffer 不可用或不完整。";
            }

            if (!coverageReady)
            {
                return "角色覆盖率不可用（AC 没产出 / OB 没进 renderer）。";
            }

            if (requirements.RequiresSkyTexture && !skyTextureAvailable)
            {
                return "GeometryBuffer sky texture is unavailable.";
            }

            return "GeometryBuffer normalDepth 不可用。";
        }
    }
}
