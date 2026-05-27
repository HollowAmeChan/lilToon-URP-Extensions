using UnityEngine;

namespace lilToon.URP.Extensions.GeometryBuffer
{
    public enum HoGeometryBufferDebugMode
    {
        [InspectorName("Off")]
        Off = 0,
        [InspectorName("Coverage")]
        Coverage,
        [InspectorName("Linear Depth")]
        LinearDepth,
        [InspectorName("World Normal")]
        WorldNormal,
        [InspectorName("Normal Validity")]
        NormalValidity,
        [InspectorName("Sky Radiance")]
        SkyRadiance,
        [InspectorName("Sky Contribution")]
        SkyContribution
    }
}
