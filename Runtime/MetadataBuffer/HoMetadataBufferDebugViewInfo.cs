using lilToon.URP.Extensions.Debugging;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    public static class HoMetadataBufferDebugViewInfo
    {
        private const string FeatureName = "MetadataBuffer";
        private const string ShaderName = "Hidden/lilToon/URP/MetadataBuffer/DebugView";
        private const string ShaderAssetPath = "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/MetadataBuffer/Shaders/Debug/HoMetadataBufferDebug.shader";
        private const string MissingFallback = "MetadataBuffer debug view is skipped when the feature-local debug shader is missing.";

        public static readonly HoDebugViewInfo[] Views =
        {
            View("metadata.mask", "Mask", HoMetadataBufferDebugMode.Mask),
            View("metadata.id", "ID", HoMetadataBufferDebugMode.Id),
            View("metadata.flags", "Flags", HoMetadataBufferDebugMode.Flags),
            View("metadata.thickness", "Thick", HoMetadataBufferDebugMode.Thickness),
            View("metadata.curvature", "Curve", HoMetadataBufferDebugMode.Curvature),
            View("metadata.material", "Mat", HoMetadataBufferDebugMode.Material),
            View("metadata.transmittance-hint", "Trans", HoMetadataBufferDebugMode.TransmittanceHint),
            View("metadata.custom0", "MC0", HoMetadataBufferDebugMode.Custom0),
            View("metadata.custom1", "MC1", HoMetadataBufferDebugMode.Custom1),
            View("metadata.custom2", "MC2", HoMetadataBufferDebugMode.Custom2),
            View("metadata.custom3", "MC3", HoMetadataBufferDebugMode.Custom3),
            View("metadata.object-custom0", "Body", HoMetadataBufferDebugMode.ObjectCustom0),
            View("metadata.object-custom1", "Face", HoMetadataBufferDebugMode.ObjectCustom1),
            View("metadata.object-custom2", "Hair", HoMetadataBufferDebugMode.ObjectCustom2),
            View("metadata.object-custom3", "Eye", HoMetadataBufferDebugMode.ObjectCustom3),
            View("metadata.object-custom4", "EyeA", HoMetadataBufferDebugMode.ObjectCustom4),
            View("metadata.object-custom5", "Acc", HoMetadataBufferDebugMode.ObjectCustom5),
            View("metadata.object-custom6", "Obj6", HoMetadataBufferDebugMode.ObjectCustom6),
            View("metadata.object-custom7", "Obj7", HoMetadataBufferDebugMode.ObjectCustom7),
            View("metadata.rsuv-packed", "RSUV", HoMetadataBufferDebugMode.RsuvPacked),
            View("metadata.rsuv-character-id", "Char", HoMetadataBufferDebugMode.RsuvCharacterId),
            View("metadata.rsuv-part-id", "Part", HoMetadataBufferDebugMode.RsuvPartId),
            View("metadata.rsuv-flags", "RFlag", HoMetadataBufferDebugMode.RsuvFlags),
            View("metadata.rsuv-id-only", "RID", HoMetadataBufferDebugMode.RsuvIdOnly),
            View("metadata.surface-color", "Base", HoMetadataBufferDebugMode.SurfaceColor),
            View("metadata.mbuffer-depth", "MDep", HoMetadataBufferDebugMode.MBufferDepth)
        };

        private static HoDebugViewInfo View(string viewId, string shortName, HoMetadataBufferDebugMode mode)
        {
            return new HoDebugViewInfo(FeatureName, viewId, shortName, (int)mode, HoDebugViewRenderKind.MetadataBuffer, ShaderName, ShaderAssetPath, true, MissingFallback);
        }
    }
}
