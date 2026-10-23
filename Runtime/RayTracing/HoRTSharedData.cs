using System.Runtime.InteropServices;
using UnityEngine;
namespace lilToon.URP.Extensions.RayTracing
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HoRTAnalyticLight
    {public Vector4 positionRange,radianceType,directionOuter,innerShadow;}
    // Shared primary guide ABI. DI and GI each own their previous-frame storage.
    internal static class HoRTPrimaryHistory
    {internal const int Stride=20;}
}
