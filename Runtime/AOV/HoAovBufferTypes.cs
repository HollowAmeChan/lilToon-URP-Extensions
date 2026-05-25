using System;
using UnityEngine;

namespace lilToon.URP.Extensions.AOV
{
    [Flags]
    public enum HoAovChannelMask
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

    public enum HoAovDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Mask")]
        Mask,
        [InspectorName("ID")]
        Id,
        [InspectorName("Flags")]
        Flags,
        [InspectorName("Linear Depth")]
        LinearDepth,
        [InspectorName("World Normal")]
        WorldNormal,
        [InspectorName("Velocity")]
        Velocity,
        [InspectorName("Thickness")]
        Thickness,
        [InspectorName("Curvature")]
        Curvature,
        [InspectorName("Material")]
        Material,
        [InspectorName("Transmittance Hint")]
        TransmittanceHint,
        [InspectorName("Material Custom 0")]
        Custom0,
        [InspectorName("Material Custom 1")]
        Custom1,
        [InspectorName("Material Custom 2")]
        Custom2,
        [InspectorName("Material Custom 3")]
        Custom3,
        [InspectorName("Body")]
        ObjectCustom0,
        [InspectorName("Face")]
        ObjectCustom1,
        [InspectorName("Front Hair")]
        ObjectCustom2,
        [InspectorName("Eye")]
        ObjectCustom3,
        [InspectorName("Eye Reveal Area")]
        ObjectCustom4,
        [InspectorName("Accessory")]
        ObjectCustom5,
        [InspectorName("Reserved 6")]
        ObjectCustom6,
        [InspectorName("Reserved 7")]
        ObjectCustom7,
        [InspectorName("RSUV Packed")]
        RsuvPacked,
        [InspectorName("RSUV Character Group ID")]
        RsuvCharacterId,
        [InspectorName("RSUV Part ID")]
        RsuvPartId,
        [InspectorName("RSUV Flags")]
        RsuvFlags,
        [InspectorName("RSUV ID Only")]
        RsuvIdOnly,
        [InspectorName("SSS Source Color")]
        Sss
    }

    public enum HoAovRenderScale
    {
        [InspectorName("Full")]
        Full = 1,
        [InspectorName("Half")]
        Half = 2,
        [InspectorName("Quarter")]
        Quarter = 4
    }

    public static class HoAovCustomChannels
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

    public static class HoAovObjectChannels
    {
        public const int DefaultCount = 8;
        public const int MaxSupportedCount = 8;
        public const int ChannelsPerTexture = 4;
    }
}
