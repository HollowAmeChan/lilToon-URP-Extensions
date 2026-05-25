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

        public ScreenProcessRuntimeResourceRequirements(
            int activeLayerCount,
            bool requiresMaskId,
            bool requiresNormalDepth,
            bool requiresSurfaceData,
            bool requiresCustom0,
            bool requiresObjectCustom0,
            bool requiresObjectCustom1)
        {
            ActiveLayerCount = activeLayerCount;
            RequiresMaskId = requiresMaskId;
            RequiresNormalDepth = requiresNormalDepth;
            RequiresSurfaceData = requiresSurfaceData;
            RequiresCustom0 = requiresCustom0;
            RequiresObjectCustom0 = requiresObjectCustom0;
            RequiresObjectCustom1 = requiresObjectCustom1;
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
            MaskIdAvailable = maskIdAvailable;
            SurfaceDataAvailable = surfaceDataAvailable;
            Custom0Available = custom0Available;
            ObjectCustom0Available = objectCustom0Available;
            ObjectCustom1Available = objectCustom1Available;
            NormalDepthAvailable = normalDepthAvailable;
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
            RequiresGeometryBuffer = RequiresNormalDepth;
            GeometryBufferAvailable = !RequiresNormalDepth || NormalDepthAvailable;
            Ready = ready;
            Reason = reason ?? string.Empty;
        }
    }

    public static class ScreenProcessRuntimeDiagnostics
    {
        private static readonly ScreenProcessRuntimeResourceRequirements EmptyRequirements =
            new ScreenProcessRuntimeResourceRequirements(0, false, false, false, false, false, false);

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
                string.Empty);

        private static ScreenProcessRuntimeDiagnosticSnapshot currentSnapshot = EmptySnapshot;

        public static ScreenProcessRuntimeDiagnosticSnapshot CurrentSnapshot => currentSnapshot;

        internal static ScreenProcessRuntimeResourceRequirements AnalyzeRequirements(List<ScreenProcessRuntimeLayer> layers)
        {
            int activeLayerCount = 0;
            bool requiresMaskId = false;
            bool requiresNormalDepth = false;
            bool requiresSurfaceData = false;
            bool requiresCustom0 = false;
            bool requiresObjectCustom0 = false;
            bool requiresObjectCustom1 = false;

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
                    bool isPostLighting = layer.effect == ScreenProcessEffect.PostLighting;
                    bool needsRule = isEdgeLight || isDropShadow || isPostLighting || layer.useRuleMask || layer.debugRuleMask;
                    if (needsRule)
                    {
                        requiresMaskId = true;
                    }

                    if (isEdgeLight || isPostLighting)
                    {
                        requiresNormalDepth = true;
                    }

                    if (isDropShadow || layer.useRuleMask || layer.debugRuleMask)
                    {
                        AccumulateRuleSourceRequirements(
                            layer,
                            ref requiresSurfaceData,
                            ref requiresCustom0,
                            ref requiresObjectCustom0,
                            ref requiresObjectCustom1);
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
                requiresObjectCustom1);
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
            bool normalDepthAvailable)
        {
            bool ready = !backBufferActive
                && cameraColorAvailable
                && (!requirements.RequiresMaskId || maskIdAvailable)
                && (!requirements.RequiresSurfaceData || surfaceDataAvailable)
                && (!requirements.RequiresCustom0 || custom0Available)
                && (!requirements.RequiresObjectCustom0 || objectCustom0Available)
                && (!requirements.RequiresObjectCustom1 || objectCustom1Available)
                && (!requirements.RequiresNormalDepth || normalDepthAvailable);

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
                    normalDepthAvailable));
        }

        private static void AccumulateRuleSourceRequirements(
            ScreenProcessLayer layer,
            ref bool requiresSurfaceData,
            ref bool requiresCustom0,
            ref bool requiresObjectCustom0,
            ref bool requiresObjectCustom1)
        {
            List<ScreenProcessRuleMaskRule> rules = layer.ruleMasks;
            if (rules == null || rules.Count == 0)
            {
                AccumulateRuleSource(layer.ruleSource, ref requiresSurfaceData, ref requiresCustom0, ref requiresObjectCustom0, ref requiresObjectCustom1);
                return;
            }

            int ruleCount = Mathf.Min(rules.Count, ScreenProcessRuleMaskRuntime.MaxRuleCount);
            for (int i = 0; i < ruleCount; i++)
            {
                ScreenProcessRuleMaskRule rule = rules[i];
                if (rule != null && rule.enabled)
                {
                    AccumulateRuleSource(rule.source, ref requiresSurfaceData, ref requiresCustom0, ref requiresObjectCustom0, ref requiresObjectCustom1);
                }
            }
        }

        private static void AccumulateRuleSource(
            ScreenProcessRuleSource source,
            ref bool requiresSurfaceData,
            ref bool requiresCustom0,
            ref bool requiresObjectCustom0,
            ref bool requiresObjectCustom1)
        {
            switch (source)
            {
                case ScreenProcessRuleSource.Thickness:
                case ScreenProcessRuleSource.Curvature:
                case ScreenProcessRuleSource.Material:
                case ScreenProcessRuleSource.TransmittanceHint:
                    requiresSurfaceData = true;
                    break;
                case ScreenProcessRuleSource.Custom0:
                case ScreenProcessRuleSource.Custom1:
                case ScreenProcessRuleSource.Custom2:
                case ScreenProcessRuleSource.Custom3:
                    requiresCustom0 = true;
                    break;
                case ScreenProcessRuleSource.ObjectCustom0:
                case ScreenProcessRuleSource.ObjectCustom1:
                case ScreenProcessRuleSource.ObjectCustom2:
                case ScreenProcessRuleSource.ObjectCustom3:
                    requiresObjectCustom0 = true;
                    break;
                case ScreenProcessRuleSource.ObjectCustom4:
                case ScreenProcessRuleSource.ObjectCustom5:
                case ScreenProcessRuleSource.ObjectCustom6:
                case ScreenProcessRuleSource.ObjectCustom7:
                    requiresObjectCustom1 = true;
                    break;
            }
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
            bool normalDepthAvailable)
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
            bool geometryAvailable = !requirements.RequiresNormalDepth || normalDepthAvailable;
            if (!metadataAvailable && !geometryAvailable)
            {
                return "MetadataBuffer 与 GeometryBuffer 不可用或不完整。";
            }

            if (!metadataAvailable)
            {
                return "MetadataBuffer 输入不完整。";
            }

            return "GeometryBuffer normalDepth 不可用。";
        }
    }
}
