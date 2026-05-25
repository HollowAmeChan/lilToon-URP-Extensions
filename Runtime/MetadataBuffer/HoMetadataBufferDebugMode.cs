using UnityEngine;

namespace lilToon.URP.Extensions.MetadataBuffer
{
    public enum HoMetadataBufferDebugMode
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
        [InspectorName("Surface Color")]
        SurfaceColor
    }
}
