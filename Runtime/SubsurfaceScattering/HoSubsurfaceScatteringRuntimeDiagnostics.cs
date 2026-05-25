using UnityEngine;

namespace lilToon.URP.Extensions.SubsurfaceScattering
{
    public readonly struct HoSubsurfaceScatteringRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly string Stage;
        public readonly bool CameraColorAvailable;
        public readonly bool MetadataBufferAvailable;
        public readonly bool GeometryBufferAvailable;
        public readonly bool Ready;
        public readonly string Reason;

        internal HoSubsurfaceScatteringRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string cameraName,
            string stage,
            bool cameraColorAvailable,
            bool metadataBufferAvailable,
            bool geometryBufferAvailable,
            bool ready,
            string reason)
        {
            IsValid = isValid;
            FrameCount = frameCount;
            CameraName = cameraName ?? string.Empty;
            Stage = stage ?? string.Empty;
            CameraColorAvailable = cameraColorAvailable;
            MetadataBufferAvailable = metadataBufferAvailable;
            GeometryBufferAvailable = geometryBufferAvailable;
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class HoSubsurfaceScatteringRuntimeDiagnostics
    {
        private static readonly HoSubsurfaceScatteringRuntimeDiagnosticSnapshot EmptySnapshot =
            new HoSubsurfaceScatteringRuntimeDiagnosticSnapshot(
                false,
                0,
                string.Empty,
                string.Empty,
                false,
                false,
                false,
                false,
                string.Empty);

        private static HoSubsurfaceScatteringRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static HoSubsurfaceScatteringRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static void PublishSkipped(Camera camera, string stage, string reason)
        {
            currentSnapshot = new HoSubsurfaceScatteringRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                false,
                false,
                false,
                false,
                reason);
        }

        internal static void PublishBufferStatus(
            Camera camera,
            string stage,
            bool cameraColorAvailable,
            bool metadataBufferAvailable,
            bool geometryBufferAvailable)
        {
            bool ready = cameraColorAvailable && metadataBufferAvailable && geometryBufferAvailable;
            currentSnapshot = new HoSubsurfaceScatteringRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                cameraColorAvailable,
                metadataBufferAvailable,
                geometryBufferAvailable,
                ready,
                ready ? "输入有效。" : BuildMissingInputReason(cameraColorAvailable, metadataBufferAvailable, geometryBufferAvailable));
        }

        private static string BuildMissingInputReason(
            bool cameraColorAvailable,
            bool metadataBufferAvailable,
            bool geometryBufferAvailable)
        {
            if (!cameraColorAvailable)
            {
                return "camera color 不可用。";
            }

            if (!metadataBufferAvailable && !geometryBufferAvailable)
            {
                return "MetadataBuffer 与 GeometryBuffer 不可用。";
            }

            if (!metadataBufferAvailable)
            {
                return "MetadataBuffer 不可用。";
            }

            return "GeometryBuffer 不可用。";
        }
    }
}
