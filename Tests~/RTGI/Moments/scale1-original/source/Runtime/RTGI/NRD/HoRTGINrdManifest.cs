// Generated from actual pinned RELAX_DIFFUSE CPU descriptors and original resource headers.
namespace lilToon.URP.Extensions.RTGI.NRD { internal static class HoRTGINrdManifest {
internal const uint DenoiserType=10;
internal static readonly string[] Identifiers={
"RELAX_ClassifyTiles.cs.hlsl",
"RELAX_HitDistReconstruction.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE|MODE_5X5=0",
"RELAX_HitDistReconstruction.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE|MODE_5X5=1",
"RELAX_PrePass.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_TemporalAccumulation.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_HistoryFix.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_HistoryClamping.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_Copy.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_AntiFirefly.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_AtrousSmem.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_Atrous.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_SplitScreen.cs.hlsl|NRD_SIGNAL=DIFF|NRD_MODE=RADIANCE",
"RELAX_Validation.cs.hlsl",
"Clear.cs.hlsl|FLOAT=1",
"Clear.cs.hlsl|FLOAT=0",
};
internal static readonly string[][] Resources={
new[]{"gIn_ViewZ","gOut_Tiles"},
new[]{"gIn_Tiles","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_Mv","gIn_Normal_Roughness","gIn_ViewZ","gIn_DisocclusionThresholdMix","gPrev_Normal_Roughness","gPrev_ViewZ","gPrev_HistoryLength","gPrev_MateriallID","gIn_Diff","gHistory_DiffFast","gHistory_Diff","gIn_DiffConfidence","gOut_HistoryLength","gOut_Diff","gOut_DiffFast"},
new[]{"gIn_Tiles","gIn_HistoryLength","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_ViewZ","gIn_HistoryLength","gIn_DiffNoisy","gIn_Diff","gIn_DiffFast","gOut_HistoryLength","gOut_Diff","gOut_DiffFast"},
new[]{"gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Tiles","gIn_HistoryLength","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff_Variance","gIn_DiffConfidence","gOut_Diff_Variance","gOut_NormalRoughness","gOut_MaterialID","gOut_ViewZ"},
new[]{"gIn_Tiles","gIn_HistoryLength","gIn_Normal_Roughness","gIn_ViewZ","gIn_Diff_Variance","gIn_DiffConfidence","gOut_Diff_Variance"},
new[]{"gIn_ViewZ","gIn_Diff","gOut_Diff"},
new[]{"gIn_Normal_Roughness","gIn_ViewZ","gIn_Mv","gIn_HistoryLength","gOut_Validation"},
new[]{"gOut"},
new[]{"gOut"},
};
} }
