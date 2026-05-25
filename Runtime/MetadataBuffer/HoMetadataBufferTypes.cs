using System;
using UnityEngine;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    [Flags]
    public enum HoMetadataBufferChannelMask
    {
        [InspectorName("None")]
        None = 0,
        [InspectorName("Mask")]
        Mask = 1 << 0,
        [InspectorName("ID")]
        Id = 1 << 1,
        [InspectorName("Flags")]
        Flags = 1 << 2,
        [InspectorName("Linear Depth")]
        LinearDepth = 1 << 3,
        [InspectorName("World Normal")]
        WorldNormal = 1 << 4,
        [InspectorName("Velocity")]
        Velocity = 1 << 7,
        [InspectorName("Thickness")]
        Thickness = 1 << 8,
        [InspectorName("Curvature")]
        Curvature = 1 << 9,
        [InspectorName("Material")]
        Material = 1 << 10,
        [InspectorName("Transmittance Hint")]
        TransmittanceHint = 1 << 11,
        [InspectorName("Default")]
        Default = Mask | Id | Flags | LinearDepth | WorldNormal | Thickness | Curvature | Material | TransmittanceHint
    }

    public static class HoMetadataBufferCustomChannels
    {
        public const int DefaultCount = 4;
        public const int MaxSupportedCount = 4;
        public const int ChannelsPerTexture = 4;

        public static int GetTextureCount(int channelCount)
        {
            int clampedCount = Mathf.Clamp(channelCount, 0, MaxSupportedCount);
            return Mathf.CeilToInt(clampedCount / (float)ChannelsPerTexture);
        }
    }

    public static class HoMetadataBufferObjectChannels
    {
        public const int DefaultCount = 8;
        public const int MaxSupportedCount = 8;
        public const int ChannelsPerTexture = 4;
    }
}
