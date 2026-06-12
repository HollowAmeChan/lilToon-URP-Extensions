using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    public readonly struct HoCharacterSpecializationRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly string Stage;
        public readonly bool BackBufferActive;
        public readonly bool CameraColorAvailable;
        public readonly bool MetadataMaskIdAvailable;
        public readonly bool MetadataObjectCustom0Available;
        public readonly bool MetadataObjectCustom1Available;
        public readonly bool MetadataSurfaceColorAvailable;
        public readonly bool MetadataSurfaceColorRequired;
        public readonly bool GeometryNormalDepthAvailable;
        public readonly bool GeometryDepthAvailable;
        public readonly bool GeometryDepthRequired;
        public readonly bool MetadataBufferAvailable;
        public readonly bool GeometryBufferAvailable;
        public readonly bool Ready;
        public readonly string Reason;

        internal HoCharacterSpecializationRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string cameraName,
            string stage,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool metadataMaskIdAvailable,
            bool metadataObjectCustom0Available,
            bool metadataObjectCustom1Available,
            bool metadataSurfaceColorAvailable,
            bool metadataSurfaceColorRequired,
            bool geometryNormalDepthAvailable,
            bool geometryDepthAvailable,
            bool geometryDepthRequired,
            bool ready,
            string reason)
        {
            IsValid = isValid;
            FrameCount = frameCount;
            CameraName = cameraName ?? string.Empty;
            Stage = stage ?? string.Empty;
            BackBufferActive = backBufferActive;
            CameraColorAvailable = cameraColorAvailable;
            MetadataMaskIdAvailable = metadataMaskIdAvailable;
            MetadataObjectCustom0Available = metadataObjectCustom0Available;
            MetadataObjectCustom1Available = metadataObjectCustom1Available;
            MetadataSurfaceColorAvailable = metadataSurfaceColorAvailable;
            MetadataSurfaceColorRequired = metadataSurfaceColorRequired;
            GeometryNormalDepthAvailable = geometryNormalDepthAvailable;
            GeometryDepthAvailable = geometryDepthAvailable;
            GeometryDepthRequired = geometryDepthRequired;
            MetadataBufferAvailable = metadataMaskIdAvailable && metadataObjectCustom0Available && metadataObjectCustom1Available;
            GeometryBufferAvailable = geometryNormalDepthAvailable && (!geometryDepthRequired || geometryDepthAvailable);
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class HoCharacterSpecializationRuntimeDiagnostics
    {
        private static readonly HoCharacterSpecializationRuntimeDiagnosticSnapshot EmptySnapshot =
            new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                false,
                0,
                string.Empty,
                string.Empty,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                string.Empty);

        private static HoCharacterSpecializationRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static HoCharacterSpecializationRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static void PublishSkipped(Camera camera, string stage, string reason)
        {
            currentSnapshot = new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                false,
                false,
                false,
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
            bool backBufferActive,
            bool cameraColorAvailable,
            bool metadataMaskIdAvailable,
            bool metadataObjectCustom0Available,
            bool metadataObjectCustom1Available,
            bool metadataSurfaceColorAvailable,
            bool geometryNormalDepthAvailable,
            bool geometryDepthAvailable,
            bool geometryDepthRequired,
            bool metadataSurfaceColorRequired)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && metadataMaskIdAvailable
                && metadataObjectCustom0Available
                && metadataObjectCustom1Available
                && (!metadataSurfaceColorRequired || metadataSurfaceColorAvailable)
                && geometryNormalDepthAvailable
                && (!geometryDepthRequired || geometryDepthAvailable);

            currentSnapshot = new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                backBufferActive,
                cameraColorAvailable,
                metadataMaskIdAvailable,
                metadataObjectCustom0Available,
                metadataObjectCustom1Available,
                metadataSurfaceColorAvailable,
                metadataSurfaceColorRequired,
                geometryNormalDepthAvailable,
                geometryDepthAvailable,
                geometryDepthRequired,
                ready,
                ready ? "Inputs are valid." : BuildMissingInputReason(
                    backBufferActive,
                    cameraColorAvailable,
                    metadataMaskIdAvailable,
                    metadataObjectCustom0Available,
                    metadataObjectCustom1Available,
                    metadataSurfaceColorAvailable,
                    metadataSurfaceColorRequired,
                    geometryNormalDepthAvailable,
                    geometryDepthAvailable,
                    geometryDepthRequired));
        }

        private static string BuildMissingInputReason(
            bool backBufferActive,
            bool cameraColorAvailable,
            bool metadataMaskIdAvailable,
            bool metadataObjectCustom0Available,
            bool metadataObjectCustom1Available,
            bool metadataSurfaceColorAvailable,
            bool metadataSurfaceColorRequired,
            bool geometryNormalDepthAvailable,
            bool geometryDepthAvailable,
            bool geometryDepthRequired)
        {
            if (backBufferActive)
            {
                return "Current active target is back buffer.";
            }

            if (!cameraColorAvailable)
            {
                return "Camera color is unavailable.";
            }

            bool metadataAvailable = metadataMaskIdAvailable && metadataObjectCustom0Available && metadataObjectCustom1Available;
            bool geometryAvailable = geometryNormalDepthAvailable && (!geometryDepthRequired || geometryDepthAvailable);
            if (!metadataAvailable && !geometryAvailable)
            {
                return "MetadataBuffer and GeometryBuffer are unavailable.";
            }

            if (!metadataAvailable)
            {
                return "MetadataBuffer maskId/object custom inputs are incomplete.";
            }

            if (metadataSurfaceColorRequired && !metadataSurfaceColorAvailable)
            {
                return "MetadataBuffer SurfaceColor is unavailable.";
            }

            if (!geometryNormalDepthAvailable)
            {
                return "GeometryBuffer normalDepth is unavailable.";
            }

            return "GeometryBuffer depth is unavailable.";
        }
    }
}
