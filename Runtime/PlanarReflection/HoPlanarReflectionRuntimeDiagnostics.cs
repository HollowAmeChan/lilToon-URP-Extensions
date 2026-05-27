using UnityEngine;

namespace lilToon.URP.Extensions.PlanarReflection
{
    public readonly struct HoPlanarReflectionRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly int SurfaceCount;
        public readonly int ActiveSurfaceCount;
        public readonly bool Ready;
        public readonly string Reason;

        internal HoPlanarReflectionRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string cameraName,
            int surfaceCount,
            int activeSurfaceCount,
            bool ready,
            string reason)
        {
            IsValid = isValid;
            FrameCount = frameCount;
            CameraName = cameraName ?? string.Empty;
            SurfaceCount = surfaceCount;
            ActiveSurfaceCount = activeSurfaceCount;
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class HoPlanarReflectionRuntimeDiagnostics
    {
        private static HoPlanarReflectionRuntimeDiagnosticSnapshot currentSnapshot =
            new HoPlanarReflectionRuntimeDiagnosticSnapshot(false, 0, string.Empty, 0, 0, false, string.Empty);

        public static HoPlanarReflectionRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static void Publish(Camera camera, HoPlanarReflectionRenderStats stats)
        {
            currentSnapshot = new HoPlanarReflectionRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stats.SurfaceCount,
                stats.ActiveSurfaceCount,
                stats.ActiveSurfaceCount > 0,
                stats.Reason);
        }
    }
}
