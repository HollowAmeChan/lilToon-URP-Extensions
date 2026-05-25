using UnityEngine;

namespace lilToon.URP.Extensions.ShadowCast
{
    public readonly struct HoShadowCastRuntimeDiagnosticLight
    {
        public readonly string Name;
        public readonly string Stage;
        public readonly LightType Type;
        public readonly int FirstSlice;
        public readonly int SliceCount;
        public readonly int Resolution;
        public readonly int BlockOffsetX;
        public readonly int BlockOffsetY;
        public readonly int BlockWidth;
        public readonly int BlockHeight;

        public HoShadowCastRuntimeDiagnosticLight(
            string name,
            string stage,
            LightType type,
            int firstSlice,
            int sliceCount,
            int resolution,
            int blockOffsetX,
            int blockOffsetY,
            int blockWidth,
            int blockHeight)
        {
            Name = name;
            Stage = stage;
            Type = type;
            FirstSlice = firstSlice;
            SliceCount = sliceCount;
            Resolution = resolution;
            BlockOffsetX = blockOffsetX;
            BlockOffsetY = blockOffsetY;
            BlockWidth = blockWidth;
            BlockHeight = blockHeight;
        }
    }

    public readonly struct HoShadowCastRuntimeDiagnosticSkip
    {
        public readonly string Name;
        public readonly string Stage;
        public readonly LightType Type;
        public readonly string Reason;

        public HoShadowCastRuntimeDiagnosticSkip(string name, string stage, LightType type, string reason)
        {
            Name = name;
            Stage = stage;
            Type = type;
            Reason = reason;
        }
    }

    public readonly struct HoShadowCastRuntimeDiagnosticSnapshot
    {
        public readonly bool IsValid;
        public readonly int FrameCount;
        public readonly string Path;
        public readonly string CameraName;
        public readonly string Source;
        public readonly int VisibleLightCount;
        public readonly int CandidateCount;
        public readonly int SkippedCandidateCount;
        public readonly bool HasFrame;
        public readonly int LightCount;
        public readonly int SliceCount;
        public readonly int AtlasSize;
        public readonly bool HasSecondDirectionalFrame;
        public readonly int SecondDirectionalLightCount;
        public readonly int SecondDirectionalSliceCount;
        public readonly int SecondDirectionalCascadeCount;
        public readonly int SecondDirectionalAtlasSize;
        public readonly HoShadowCastRuntimeDiagnosticLight[] AcceptedLights;
        public readonly HoShadowCastRuntimeDiagnosticSkip[] SkippedLights;

        internal HoShadowCastRuntimeDiagnosticSnapshot(
            bool isValid,
            int frameCount,
            string path,
            string cameraName,
            string source,
            int visibleLightCount,
            int candidateCount,
            int skippedCandidateCount,
            bool hasFrame,
            int lightCount,
            int sliceCount,
            int atlasSize,
            bool hasSecondDirectionalFrame,
            int secondDirectionalLightCount,
            int secondDirectionalSliceCount,
            int secondDirectionalCascadeCount,
            int secondDirectionalAtlasSize,
            HoShadowCastRuntimeDiagnosticLight[] acceptedLights,
            HoShadowCastRuntimeDiagnosticSkip[] skippedLights)
        {
            IsValid = isValid;
            FrameCount = frameCount;
            Path = path;
            CameraName = cameraName;
            Source = source;
            VisibleLightCount = visibleLightCount;
            CandidateCount = candidateCount;
            SkippedCandidateCount = skippedCandidateCount;
            HasFrame = hasFrame;
            LightCount = lightCount;
            SliceCount = sliceCount;
            AtlasSize = atlasSize;
            HasSecondDirectionalFrame = hasSecondDirectionalFrame;
            SecondDirectionalLightCount = secondDirectionalLightCount;
            SecondDirectionalSliceCount = secondDirectionalSliceCount;
            SecondDirectionalCascadeCount = secondDirectionalCascadeCount;
            SecondDirectionalAtlasSize = secondDirectionalAtlasSize;
            AcceptedLights = acceptedLights ?? EmptyAcceptedLights;
            SkippedLights = skippedLights ?? EmptySkippedLights;
        }

        private static readonly HoShadowCastRuntimeDiagnosticLight[] EmptyAcceptedLights = new HoShadowCastRuntimeDiagnosticLight[0];
        private static readonly HoShadowCastRuntimeDiagnosticSkip[] EmptySkippedLights = new HoShadowCastRuntimeDiagnosticSkip[0];
    }

    public static class HoShadowCastRuntimeDiagnostics
    {
        private static readonly HoShadowCastRuntimeDiagnosticSnapshot EmptySnapshot =
            new HoShadowCastRuntimeDiagnosticSnapshot(
                false,
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                0,
                0,
                false,
                0,
                0,
                0,
                false,
                0,
                0,
                0,
                0,
                null,
                null);

        private static HoShadowCastRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static HoShadowCastRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static HoShadowCastFrameDiagnostics Begin(string path, HoShadowCastFrameConfig config, int visibleLightCount, Camera camera)
        {
            return new HoShadowCastFrameDiagnostics(path, config, visibleLightCount, camera);
        }

        internal static void Publish(HoShadowCastRuntimeDiagnosticSnapshot snapshot)
        {
            currentSnapshot = snapshot;
        }
    }

    internal sealed class HoShadowCastFrameDiagnostics
    {
        private const int MaxAcceptedLightEntries = 8;
        private const int MaxSkippedLightEntries = 12;

        private readonly HoShadowCastRuntimeDiagnosticLight[] acceptedLights = new HoShadowCastRuntimeDiagnosticLight[MaxAcceptedLightEntries];
        private readonly HoShadowCastRuntimeDiagnosticSkip[] skippedLights = new HoShadowCastRuntimeDiagnosticSkip[MaxSkippedLightEntries];
        private readonly string path;
        private readonly string cameraName;
        private readonly string source;
        private readonly int visibleLightCount;
        private int acceptedLightEntryCount;
        private int skippedLightEntryCount;

        public int CandidateCount { get; private set; }
        public int SkippedCandidateCount { get; private set; }

        public HoShadowCastFrameDiagnostics(string path, HoShadowCastFrameConfig config, int visibleLightCount, Camera camera)
        {
            this.path = path ?? string.Empty;
            this.visibleLightCount = Mathf.Max(0, visibleLightCount);
            cameraName = camera != null ? camera.name : "<no camera>";
            if (config == null)
            {
                source = "Unavailable";
            }
            else if (config.collectVisibleLights)
            {
                source = "Visible Lights";
            }
            else if (config.usingControllerOverride)
            {
                source = "Controller Override";
            }
            else
            {
                source = "Manual Lists";
            }
        }

        public void AddCandidate()
        {
            CandidateCount++;
        }

        public void AddSkipped(Light light, string stage, LightType type, string reason)
        {
            SkippedCandidateCount++;
            if (skippedLightEntryCount >= skippedLights.Length)
            {
                return;
            }

            skippedLights[skippedLightEntryCount++] = new HoShadowCastRuntimeDiagnosticSkip(
                light != null ? light.name : "<none>",
                stage,
                type,
                reason);
        }

        public void AddAccepted(
            Light light,
            string stage,
            LightType type,
            int firstSlice,
            int sliceCount,
            int resolution,
            int blockOffsetX = -1,
            int blockOffsetY = -1,
            int blockWidth = 0,
            int blockHeight = 0)
        {
            if (acceptedLightEntryCount >= acceptedLights.Length)
            {
                return;
            }

            acceptedLights[acceptedLightEntryCount++] = new HoShadowCastRuntimeDiagnosticLight(
                light != null ? light.name : "<none>",
                stage,
                type,
                firstSlice,
                sliceCount,
                resolution,
                blockOffsetX,
                blockOffsetY,
                blockWidth,
                blockHeight);
        }

        public void Publish(bool hasFrame, HoShadowCastFrame frame, bool hasSecondDirectionalFrame, HoShadowCastSecondDirectionalFrame secondDirectionalFrame)
        {
            HoShadowCastRuntimeDiagnostics.Publish(new HoShadowCastRuntimeDiagnosticSnapshot(
                true,
                Time.frameCount,
                path,
                cameraName,
                source,
                visibleLightCount,
                CandidateCount,
                SkippedCandidateCount,
                hasFrame,
                frame != null ? frame.lightCount : 0,
                frame != null ? frame.sliceCount : 0,
                frame != null ? frame.atlasSize : 0,
                hasSecondDirectionalFrame,
                secondDirectionalFrame != null ? secondDirectionalFrame.lightCount : 0,
                secondDirectionalFrame != null ? secondDirectionalFrame.sliceCount : 0,
                secondDirectionalFrame != null ? secondDirectionalFrame.cascadeCountPerLight : 0,
                secondDirectionalFrame != null ? secondDirectionalFrame.atlasSize : 0,
                CopyAcceptedLights(),
                CopySkippedLights()));
        }

        private HoShadowCastRuntimeDiagnosticLight[] CopyAcceptedLights()
        {
            HoShadowCastRuntimeDiagnosticLight[] copy = new HoShadowCastRuntimeDiagnosticLight[acceptedLightEntryCount];
            for (int i = 0; i < acceptedLightEntryCount; i++)
            {
                copy[i] = acceptedLights[i];
            }

            return copy;
        }

        private HoShadowCastRuntimeDiagnosticSkip[] CopySkippedLights()
        {
            HoShadowCastRuntimeDiagnosticSkip[] copy = new HoShadowCastRuntimeDiagnosticSkip[skippedLightEntryCount];
            for (int i = 0; i < skippedLightEntryCount; i++)
            {
                copy[i] = skippedLights[i];
            }

            return copy;
        }
    }
}
