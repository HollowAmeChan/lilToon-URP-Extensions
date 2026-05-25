using UnityEngine;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    internal static class HoMetadataBufferShaderConstants
    {
        public const string ClearShaderName = "Hidden/lilToon/URP/MetadataBuffer/Clear";
        public const string FallbackShaderName = "Hidden/lilToon/URP/MetadataBuffer/Fallback";
        public const string DebugShaderName = "Hidden/lilToon/URP/MetadataBuffer/DebugView";

        public static readonly int DebugModeId = Shader.PropertyToID("_HoMetadataBufferDebugMode");
        public static readonly int DebugDepthParamsId = Shader.PropertyToID("_HoMetadataBufferDebugDepthParams");
    }
}
