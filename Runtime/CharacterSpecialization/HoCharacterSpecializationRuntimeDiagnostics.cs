using UnityEngine;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>
    /// 这支 feature 的输入自检快照。**语义全部来自 ObjectBuffer**（规划 §5.13：角色特化只吃
    /// 「组 + 标签 + 覆盖率」），所以这里只查 OB 身份池、语义位平面和 GeometryBuffer 三件事。
    /// </summary>
    public readonly struct HoCharacterSpecializationRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string CameraName;
        public readonly string Stage;
        public readonly bool BackBufferActive;
        public readonly bool CameraColorAvailable;
        /// <summary>OB 身份池（Id0 / Id1 / 覆盖率）在。</summary>
        public readonly bool ObjectBufferIdentityAvailable;
        /// <summary>语义位平面这帧能产出（OB 身份 + 打包材质都在）。</summary>
        public readonly bool ObjectSemanticAvailable;
        public readonly bool GeometryNormalDepthAvailable;
        public readonly bool GeometryDepthAvailable;
        public readonly bool GeometryDepthRequired;
        public readonly bool ObjectBufferAvailable;
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
            bool objectBufferIdentityAvailable,
            bool objectSemanticAvailable,
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
            ObjectBufferIdentityAvailable = objectBufferIdentityAvailable;
            ObjectSemanticAvailable = objectSemanticAvailable;
            GeometryNormalDepthAvailable = geometryNormalDepthAvailable;
            GeometryDepthAvailable = geometryDepthAvailable;
            GeometryDepthRequired = geometryDepthRequired;
            ObjectBufferAvailable = objectBufferIdentityAvailable && objectSemanticAvailable;
            GeometryBufferAvailable = geometryNormalDepthAvailable && (!geometryDepthRequired || geometryDepthAvailable);
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class HoCharacterSpecializationRuntimeDiagnostics
    {
        private static readonly HoCharacterSpecializationRuntimeDiagnosticSnapshot EmptySnapshot =
            new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                false, 0, string.Empty, string.Empty, false, false, false, false, false, false, false, false, string.Empty);

        private static HoCharacterSpecializationRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static HoCharacterSpecializationRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static void PublishSkipped(Camera camera, string stage, string reason)
        {
            currentSnapshot = new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                false, false, false, false, false, false, false,
                false,
                reason);
        }

        internal static void PublishRenderGraphInputs(
            Camera camera,
            string stage,
            bool backBufferActive,
            bool cameraColorAvailable,
            bool objectBufferIdentityAvailable,
            bool objectSemanticAvailable,
            bool geometryNormalDepthAvailable,
            bool geometryDepthAvailable,
            bool geometryDepthRequired)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && objectBufferIdentityAvailable
                && objectSemanticAvailable
                && geometryNormalDepthAvailable
                && (!geometryDepthRequired || geometryDepthAvailable);

            currentSnapshot = new HoCharacterSpecializationRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                camera != null ? camera.name : "<no camera>",
                stage,
                backBufferActive,
                cameraColorAvailable,
                objectBufferIdentityAvailable,
                objectSemanticAvailable,
                geometryNormalDepthAvailable,
                geometryDepthAvailable,
                geometryDepthRequired,
                ready,
                ready ? "Inputs are valid." : BuildMissingInputReason(
                    backBufferActive,
                    cameraColorAvailable,
                    objectBufferIdentityAvailable,
                    objectSemanticAvailable,
                    geometryNormalDepthAvailable,
                    geometryDepthAvailable,
                    geometryDepthRequired));
        }

        private static string BuildMissingInputReason(
            bool backBufferActive,
            bool cameraColorAvailable,
            bool objectBufferIdentityAvailable,
            bool objectSemanticAvailable,
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

            bool objectBufferAvailable = objectBufferIdentityAvailable && objectSemanticAvailable;
            bool geometryAvailable = geometryNormalDepthAvailable && (!geometryDepthRequired || geometryDepthAvailable);
            if (!objectBufferAvailable && !geometryAvailable)
            {
                return "ObjectBuffer and GeometryBuffer are unavailable.";
            }

            if (!objectBufferIdentityAvailable)
            {
                return "ObjectBuffer identity pool is unavailable (is the Ho-ObjectBuffer feature in this renderer?).";
            }

            if (!objectSemanticAvailable)
            {
                return "ObjectBuffer semantic plane could not be packed (missing shader or material).";
            }

            if (!geometryNormalDepthAvailable)
            {
                return "GeometryBuffer normalDepth is unavailable.";
            }

            return "GeometryBuffer depth is unavailable.";
        }
    }
}
