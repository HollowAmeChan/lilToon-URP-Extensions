using UnityEngine;
namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>Last recorded camera's producer capability; pixel mismatch coverage is in the association status texture.</summary>
    public static class HoSurfaceSemanticPrecisionDiagnostics
    {
        public static string CameraName { get; private set; } = "";
        public static int Frame { get; private set; } = -1;
        public static int Samples { get; private set; }
        public static string Status { get; private set; } = "Not recorded";
        public static bool Correlated { get; private set; }
        internal static void Publish(Camera camera, int samples, string status, bool correlated = false)
        {
            CameraName = camera != null ? camera.name : ""; Frame = Time.frameCount;
            Samples = samples; Status = status; Correlated = correlated;
        }
    }
}
