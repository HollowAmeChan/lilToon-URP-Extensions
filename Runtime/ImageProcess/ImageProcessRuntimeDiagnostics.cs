using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    public readonly struct ImageProcessRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly string Stage;
        public readonly int ActiveLayerCount;
        public readonly int WrittenLayerCount;
        public readonly bool BackBufferActive;
        public readonly bool CameraColorAvailable;
        public readonly bool Ready;
        public readonly string Reason;

        internal ImageProcessRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string cameraName,
            string stage,
            int activeLayerCount,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable,
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
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class ImageProcessRuntimeDiagnostics
    {
        private static readonly ImageProcessRuntimeDiagnosticSnapshot EmptySnapshot =
            new ImageProcessRuntimeDiagnosticSnapshot(
                false,
                0,
                string.Empty,
                string.Empty,
                0,
                0,
                false,
                false,
                false,
                string.Empty);

        private static ImageProcessRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static ImageProcessRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static void PublishSkipped(Camera camera, string stage, string reason)
        {
            currentSnapshot = new ImageProcessRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                0,
                0,
                false,
                false,
                false,
                reason);
        }

        internal static void PublishInputs(
            Camera camera,
            string stage,
            int activeLayerCount,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable)
        {
            bool ready = activeLayerCount > 0
                && writtenLayerCount > 0
                && !backBufferActive
                && cameraColorAvailable;

            currentSnapshot = new ImageProcessRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                activeLayerCount,
                writtenLayerCount,
                backBufferActive,
                cameraColorAvailable,
                ready,
                ready ? "输入有效。" : BuildReason(activeLayerCount, writtenLayerCount, backBufferActive, cameraColorAvailable));
        }

        private static string BuildReason(
            int activeLayerCount,
            int writtenLayerCount,
            bool backBufferActive,
            bool cameraColorAvailable)
        {
            if (activeLayerCount == 0)
            {
                return "没有可运行的 ImageProcess layer。";
            }

            if (backBufferActive)
            {
                return "当前 active target 是 back buffer。";
            }

            if (!cameraColorAvailable)
            {
                return "camera color 不可用。";
            }

            if (writtenLayerCount == 0)
            {
                return "没有写入的 ImageProcess layer。";
            }

            return "输入不可用。";
        }
    }
}
