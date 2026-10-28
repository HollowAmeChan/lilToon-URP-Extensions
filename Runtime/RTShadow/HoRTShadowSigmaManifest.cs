// Generated from pinned NRD 4.17.3 native descriptors and original resource declarations.
namespace lilToon.URP.Extensions.RTShadow
{
    internal static class HoRTShadowSigmaManifest
    {
        internal static readonly string[] Identifiers = {
            "SIGMA_ClassifyTiles.cs.hlsl|TRANSLUCENCY=0",
            "SIGMA_SmoothTiles.cs.hlsl",
            "SIGMA_Copy.cs.hlsl",
            "SIGMA_Blur.cs.hlsl|TRANSLUCENCY=0|FIRST_PASS=1",
            "SIGMA_Blur.cs.hlsl|TRANSLUCENCY=0|FIRST_PASS=0",
            "SIGMA_TemporalStabilization.cs.hlsl|TRANSLUCENCY=0",
            "SIGMA_SplitScreen.cs.hlsl|TRANSLUCENCY=0",
            "Clear.cs.hlsl|FLOAT=1",
            "Clear.cs.hlsl|FLOAT=0",
        };
        internal static readonly string[][] Resources = {
            new[] { "gIn_ViewZ", "gIn_Penumbra", "gOut_Tiles" },
            new[] { "gIn_Tiles", "gOut_Tiles" },
            new[] { "gIn_Tiles", "gIn_History", "gIn_HistoryLength", "gOut_History", "gOut_HistoryLength" },
            new[] { "gIn_ViewZ", "gIn_Normal_Roughness", "gIn_Penumbra", "gIn_Tiles", "gOut_Penumbra", "gOut_Shadow_Translucency" },
            new[] { "gIn_ViewZ", "gIn_Normal_Roughness", "gIn_Penumbra", "gIn_Tiles", "gIn_Shadow_Translucency", "gOut_Penumbra", "gOut_Shadow_Translucency" },
            new[] { "gIn_ViewZ", "gIn_Mv", "gIn_Penumbra", "gIn_Shadow_Translucency", "gIn_History", "gIn_HistoryLength", "gIn_Tiles", "gOut_Shadow_Translucency", "gOut_HistoryLength" },
            new[] { "gIn_ViewZ", "gIn_Penumbra", "gOut_Shadow_Translucency" },
            new[] { "gOut" },
            new[] { "gOut" },
        };
    }
}
