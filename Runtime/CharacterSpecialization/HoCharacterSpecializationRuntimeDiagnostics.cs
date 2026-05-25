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
        public readonly bool GeometryNormalDepthAvailable;
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
            bool geometryNormalDepthAvailable,
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
            GeometryNormalDepthAvailable = geometryNormalDepthAvailable;
            MetadataBufferAvailable = metadataMaskIdAvailable && metadataObjectCustom0Available && metadataObjectCustom1Available;
            GeometryBufferAvailable = geometryNormalDepthAvailable;
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
            bool geometryNormalDepthAvailable)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && metadataMaskIdAvailable
                && metadataObjectCustom0Available
                && metadataObjectCustom1Available
                && geometryNormalDepthAvailable;

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
                geometryNormalDepthAvailable,
                ready,
                ready ? "输入有效。" : BuildMissingInputReason(
                    backBufferActive,
                    cameraColorAvailable,
                    metadataMaskIdAvailable,
                    metadataObjectCustom0Available,
                    metadataObjectCustom1Available,
                    geometryNormalDepthAvailable));
        }

        private static string BuildMissingInputReason(
            bool backBufferActive,
            bool cameraColorAvailable,
            bool metadataMaskIdAvailable,
            bool metadataObjectCustom0Available,
            bool metadataObjectCustom1Available,
            bool geometryNormalDepthAvailable)
        {
            if (backBufferActive)
            {
                return "当前 active target 是 back buffer。";
            }

            if (!cameraColorAvailable)
            {
                return "camera color 不可用。";
            }

            bool metadataAvailable = metadataMaskIdAvailable && metadataObjectCustom0Available && metadataObjectCustom1Available;
            if (!metadataAvailable && !geometryNormalDepthAvailable)
            {
                return "MetadataBuffer 与 GeometryBuffer 不可用。";
            }

            if (!metadataAvailable)
            {
                return "MetadataBuffer 的 maskId/object custom 输入不完整。";
            }

            return "GeometryBuffer normalDepth 不可用。";
        }
    }
}
