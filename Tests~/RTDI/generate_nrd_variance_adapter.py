"""Regenerate/check DenseDI moment and material-guide adaptations of NRD 4.17.3."""
import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
VENDOR = ROOT / "Runtime/RTDI/ThirdParty/NRD"
OUTPUT = ROOT / "Runtime/RTDI/Shaders/NRD/HoRTDINrdAtrousSmem.hlsl"
HELPER = """void computeVariance(
    int2 threadPos
#if( NRD_SPEC )
    ,out float specularVariance
#endif
#if( NRD_DIFF )
    ,out float diffuseVariance
#endif
)
{
#if( NRD_SPEC )
    specularVariance = 0;
#endif
#if( NRD_DIFF )
    diffuseVariance = 0;
#endif
    static const float kernel[2][2] =
    {
        { 1.0 / 4.0, 1.0 / 8.0 },
        { 1.0 / 8.0, 1.0 / 16.0 }
    };
    int2 center = threadPos + int2(NRD_BORDER, NRD_BORDER);
    [unroll]
    for (int dx = -1; dx <= 1; dx++)
    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        int2 p = center + int2(dx, dy);
        float k = kernel[abs(dx)][abs(dy)];
#if( NRD_SPEC )
        float4 valueSpec = s_Spec[p.y][p.x];
        float lumaSpec = Color::Luminance(valueSpec.rgb);
        specularVariance += k * max(0, valueSpec.a - lumaSpec * lumaSpec);
#endif
#if( NRD_DIFF )
        float4 valueDiff = s_Diff[p.y][p.x];
        float lumaDiff = Color::Luminance(valueDiff.rgb);
        diffuseVariance += k * max(0, valueDiff.a - lumaDiff * lumaDiff);
#endif
    }
}

"""


def original(name):
    source = VENDOR / name
    raw = source.read_bytes()
    text = source.read_text(encoding="utf-8-sig")
    expected = json.loads((VENDOR / "provenance.json").read_text())["files"][source.name]["sha256"]
    candidates = (raw, text.encode(), text.replace("\n", "\r\n").encode())
    if expected not in {hashlib.sha256(data).hexdigest() for data in candidates}:
        raise ValueError("Pinned original NRD source changed; re-audit the adaptation")
    return text, expected


def generate():
    text, expected = original("RELAX_AtrousSmem.cs.hlsl")
    if text.count("void computeVariance(") != 1 or text.count("void Preload(") != 1:
        raise ValueError("Unexpected NRD function layout")
    begin, end = text.index("void computeVariance("), text.index("void Preload(")
    if end <= begin:
        raise ValueError("Unexpected NRD function order")
    derived = text[:begin] + HELPER + text[end:]
    derived = re.sub(r'#include "([^\"]+)"', r'#include "../../ThirdParty/NRD/\1"', derived)
    derived = guide_include(derived)
    derived = derived.replace("groupshared float4 s_Normal_Roughness[BUFFER_Y][BUFFER_X];", "groupshared float4 s_Normal_Roughness[BUFFER_Y][BUFFER_X];\ngroupshared float3 s_SpecularMaterial[BUFFER_Y][BUFFER_X];")
    derived = derived.replace("// Helper functions", "groupshared float s_SpecularVarianceSamples[BUFFER_Y][BUFFER_X];\n\n// Helper functions")
    derived = derived.replace("    globalPos = clamp(globalPos, 0, gRectSize - 1.0);", "    globalPos = clamp(globalPos, 0, gRectSize - 1.0);\n    // Clamping may adopt fast color: use the shorter budget conservatively.\n    s_SpecularVarianceSamples[sharedPos.y][sharedPos.x] = max(1.0, min(255.0 * gIn_HistoryLength[globalPos], gSpecMaxFastAccumulatedFrameNum + 1.0));")
    derived = derived.replace("    float materialID;", "    s_SpecularMaterial[sharedPos.y][sharedPos.x] = HoNrdSpecularGuide(globalPos);\n    float materialID;")
    derived = derived.replace("wSpecular *= CompareMaterials(sampleMaterialID, centerMaterialID, gSpecMinMaterial);", "wSpecular *= CompareMaterials(sampleMaterialID, centerMaterialID, gSpecMinMaterial);\n                wSpecular *= HoNrdSpecularMaterialWeight(s_SpecularMaterial[sharedMemoryIndex.y][sharedMemoryIndex.x], s_SpecularMaterial[sharedMemoryIndexP.y][sharedMemoryIndexP.x]);")
    # Match the later A-trous kernels: propagate uncertainty of the weighted
    # color rather than reclassifying real spatial shading contrast as noise.
    patches = {
        "        float4 sumSpecularIlluminationAnd2ndMoment = 0;": "        float4 sumSpecularIlluminationAnd2ndMoment = 0;\n        float sumSpecularTemporalVariance = 0;",
        "                sumSpecularIlluminationAnd2ndMoment += wSpecular * sampleSpecularIlluminationAnd2ndMoment;": "                sumSpecularIlluminationAnd2ndMoment += wSpecular * sampleSpecularIlluminationAnd2ndMoment;\n                sumSpecularTemporalVariance += wSpecular * wSpecular * sampleSpecularVar;",
        "        float specularVariance = max(0, specular2ndMoment - specular1stMoment * specular1stMoment);": "        float specularVariance = sumSpecularTemporalVariance / (sumWSpecular * sumWSpecular);",
        "        float4 sumDiffuseIlluminationAnd2ndMoment = 0;": "        float4 sumDiffuseIlluminationAnd2ndMoment = 0;\n        float sumDiffuseTemporalVariance = 0;",
        "                sumDiffuseIlluminationAnd2ndMoment += wDiffuse * sampleDiffuseIlluminationAnd2ndMoment;": "                sumDiffuseIlluminationAnd2ndMoment += wDiffuse * sampleDiffuseIlluminationAnd2ndMoment;\n                float sampleDiffuseTemporalVar = max(0.0, sampleDiffuseIlluminationAnd2ndMoment.a - sampleDiffuseLuminance * sampleDiffuseLuminance);\n                sumDiffuseTemporalVariance += wDiffuse * wDiffuse * sampleDiffuseTemporalVar;",
        "        float diffuseVariance = max(0, diffuse2ndMoment - diffuse1stMoment * diffuse1stMoment);": "        float diffuseVariance = sumDiffuseTemporalVariance / (sumWDiffuse * sumWDiffuse);",
    }
    for old, new in patches.items():
        if derived.count(old) != 1: raise ValueError("Unexpected first A-trous variance propagation: " + old)
        derived = derived.replace(old, new)
    old = "float specularLuminanceW = abs(centerSpecularLuminance - sampleSpecularLuminance) * specularPhiLIlluminationInv;"
    new = """float sampleSpecularVar = max(0.0, sampleSpecularIlluminationAnd2ndMoment.a - sampleSpecularLuminance * sampleSpecularLuminance) / s_SpecularVarianceSamples[sharedMemoryIndexP.y][sharedMemoryIndexP.x];
                float centerRawSpecularVar = max(0.0, s_Spec[sharedMemoryIndex.y][sharedMemoryIndex.x].a - centerSpecularLuminance * centerSpecularLuminance) / s_SpecularVarianceSamples[sharedMemoryIndex.y][sharedMemoryIndex.x];
                // Either precise endpoint can reject contrast: a bright noisy
                // center must not alone decide the range bandwidth.
                float pairSpecularVar = min(centerRawSpecularVar, sampleSpecularVar);
                float specularLuminanceW = abs(centerSpecularLuminance - sampleSpecularLuminance) / max(1.0e-4, gSpecPhiLuminance * sqrt(pairSpecularVar));"""
    if derived.count(old) != 1: raise ValueError("Unexpected smem specular luminance weight")
    derived = derived.replace(old, new)
    notice = ("// Ho-RTDI dense-signal adaptation of NRD 4.17.3 RELAX_AtrousSmem.\n"
              f"// Original SHA256: {expected}\n"
              "// Adapted: temporal variance, accumulated specular uncertainty/pair bandwidth, diffuse/specular weighted propagation, F0 weights.\n"
              "// Original source/license remain in ../../ThirdParty/NRD.\n")
    return notice + derived


def guide_include(text):
    marker = '#include "../../ThirdParty/NRD/RELAX_Common.hlsli"'
    if text.count(marker) != 1:
        raise ValueError("Unexpected NRD common include")
    return text.replace(marker, marker + '\n#include "HoRTDINrdMaterialGuide.hlsl"')


def generate_atrous():
    text, expected = original("RELAX_Atrous.cs.hlsl")
    text = re.sub(r'#include "([^\"]+)"', r'#include "../../ThirdParty/NRD/\1"', text)
    text = guide_include(text)
    text = text.replace("    float3 centerV = -normalize(centerWorldPos);", "    float3 centerV = -normalize(centerWorldPos);\n    float3 centerSpecularMaterial = HoNrdSpecularGuide(pixelPos);")
    text = text.replace("wSpecular *= CompareMaterials(sampleMaterialID, centerMaterialID, gSpecMinMaterial);", "wSpecular *= CompareMaterials(sampleMaterialID, centerMaterialID, gSpecMinMaterial);\n                wSpecular *= (isInside ? HoNrdSpecularMaterialWeight(centerSpecularMaterial, HoNrdSpecularGuide(p)) : 0.0);")
    old = "float specularLuminanceW = abs(centerSpecularLuminance - sampleSpecularLuminance) * specularPhiLIlluminationInv;"
    new = """float pairSpecularVar = min(centerSpecularVar, max(0.0, sampleSpecularIlluminationAndVariance.a));
                float specularLuminanceW = abs(centerSpecularLuminance - sampleSpecularLuminance) / max(1.0e-4, gSpecPhiLuminance * sqrt(pairSpecularVar));"""
    if text.count(old) != 1: raise ValueError("Unexpected atrous specular luminance weight")
    text = text.replace(old, new)
    old = "float diffuseLuminanceW = abs(centerDiffuseLuminance - sampleDiffuseLuminance) * diffusePhiLIlluminationInv;"
    new = """float pairDiffuseVar = min(max(0.0, centerDiffuseVar), max(0.0, sampleDiffuseIlluminationAndVariance.a));
                float diffuseLuminanceW = abs(centerDiffuseLuminance - sampleDiffuseLuminance) / max(1.0e-4, gDiffPhiLuminance * sqrt(pairDiffuseVar));"""
    if text.count(old) != 1: raise ValueError("Unexpected atrous diffuse luminance weight")
    text = text.replace(old, new)
    old = "    float4 filteredDiffuseIlluminationAndVariance = float4(sumDiffuseIlluminationAndVariance / float4(sumWDiffuse.xxx, sumWDiffuse * sumWDiffuse));"
    new = """    // Use fixed pair exchange coefficients instead of a row-dependent
    // normalization. Rejected neighbor weight stays at the center.
    const float diffuseCenterKernel = 0.44198 * 0.44198;
    float retainedDiffuseWeight = saturate(1.0 - (sumWDiffuse - diffuseCenterKernel));
    float4 diffuseNeighborSum = sumDiffuseIlluminationAndVariance - centerDiffuseIlluminationAndVariance * float4(diffuseCenterKernel.xxx, diffuseCenterKernel * diffuseCenterKernel);
    float4 filteredDiffuseIlluminationAndVariance = diffuseNeighborSum + centerDiffuseIlluminationAndVariance * float4(retainedDiffuseWeight.xxx, retainedDiffuseWeight * retainedDiffuseWeight);"""
    if text.count(old) != 1: raise ValueError("Unexpected atrous diffuse normalization")
    text = text.replace(old, new)
    return f"// Ho-RTDI F0/diffuse-specular pair-bandwidth adaptation of NRD 4.17.3. Original SHA256: {expected}\n" + text


def generate_temporal():
    text, expected = original("RELAX_TemporalAccumulation.cs.hlsl")
    text = re.sub(r'#include "([^\"]+)"', r'#include "../../ThirdParty/NRD/\1"', text)
    text = guide_include(text)
    marker = "    float specHistoryLength = historyLength;"
    if text.count(marker) != 1:
        raise ValueError("Unexpected NRD specular budget location")
    text = text.replace(marker, "    float materialMatch = HoNrdSpecularHistoryMatch(pixelPos, prevUVSMB);\n    specMaxAccumulatedFrameNum *= materialMatch;\n    specMaxFastAccumulatedFrameNum *= materialMatch;\n" + marker)
    text = text.replace('#include "HoRTDINrdMaterialGuide.hlsl"', '#include "HoRTDINrdMaterialGuide.hlsl"' + '''
// Current and previous signal domains are GPU-selected, independent of exposure.
// Scale RGB linearly and second moments quadratically; hit distances stay intact.
StructuredBuffer<uint> _HoRTDINrdScale;
float HoNrdHistoryScale() { return asfloat(_HoRTDINrdScale[1]) / asfloat(_HoRTDINrdScale[2]); }
float4 HoNrdScaleMoment(float4 value)
{
    float ratio = HoNrdHistoryScale();
    return value * float4(ratio.xxx, ratio * ratio);
}
''')
    markers = {
        '    prevDiffuseIllumAnd2ndMoment = max(prevDiffuseIllumAnd2ndMoment, 0);': '    prevDiffuseIllumAnd2ndMoment = HoNrdScaleMoment(max(prevDiffuseIllumAnd2ndMoment, 0));',
        '    prevDiffuseResponsiveIllum = max(diff.rgb, 0);': '    prevDiffuseResponsiveIllum = max(diff.rgb, 0) * HoNrdHistoryScale();',
        'prevSpecularIllumAnd2ndMoment = max(prevSpecularIllumAnd2ndMoment, 0.0);': 'prevSpecularIllumAnd2ndMoment = HoNrdScaleMoment(max(prevSpecularIllumAnd2ndMoment, 0.0));',
        '    prevSpecularResponsiveIllum = max(spec.rgb, 0);': '    prevSpecularResponsiveIllum = max(spec.rgb, 0) * HoNrdHistoryScale();',
        '        prevSpecularResponsiveIllum = max(prevSpecularResponsiveIllum, 0.0);': '        prevSpecularResponsiveIllum = max(prevSpecularResponsiveIllum, 0.0);\n        prevSpecularResponsiveIllum.rgb *= HoNrdHistoryScale();',
    }
    for old, new in markers.items():
        if text.count(old) != (2 if old.startswith('prevSpecularIllum') else 1):
            raise ValueError("Unexpected NRD signal-history layout: " + old)
        text = text.replace(old, new)
    return f"// Ho-RTDI F0/history-domain adaptation of NRD 4.17.3. Original SHA256: {expected}\n" + text


def generate_clamping():
    text, expected = original("RELAX_HistoryClamping.cs.hlsl")
    old = "    if (historyLength <= gHistoryFixFrameNum)\n        outSpecular = outSpecularResponsive;"
    if text.count(old) != 1:
        raise ValueError("Unexpected NRD responsive specular copy")
    new = """    if (historyLength <= gHistoryFixFrameNum)
    {
        // With HistoryFix disabled, fast alpha is hit distance, not a moment.
        // Preserve Temporal's second moment while adopting responsive color.
        if (gHistoryFixFrameNum == 1.0)
            outSpecular.rgb = outSpecularResponsive.rgb;
        else
            outSpecular = outSpecularResponsive;
    }"""
    derived = text.replace(old, new)
    derived = re.sub(r'#include "([^\"]+)"', r'#include "../../ThirdParty/NRD/\1"', derived)
    notice = ("// Ho-RTDI adaptation of NRD 4.17.3 RELAX_HistoryClamping.\n"
              f"// Original SHA256: {expected}\n"
              "// Only the disabled-HistoryFix specular copy changes; fast alpha is hit distance.\n"
              "// Original source/license remain in ../../ThirdParty/NRD.\n")
    return notice + derived


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    outputs = {OUTPUT: generate(), OUTPUT.with_name("HoRTDINrdHistoryClamping.hlsl"): generate_clamping(), OUTPUT.with_name("HoRTDINrdAtrous.hlsl"): generate_atrous(), OUTPUT.with_name("HoRTDINrdTemporal.hlsl"): generate_temporal()}
    for path, generated in outputs.items():
        if args.check:
            if path.read_text(encoding="utf-8-sig") != generated:
                raise SystemExit(f"DenseDI adaptation differs from pinned reconstruction: {path}")
        else:
            path.write_text(generated, encoding="utf-8", newline="\n")
            print(path)
    if args.check:
        print("PASS: original SHAs and all DenseDI adaptations match")
