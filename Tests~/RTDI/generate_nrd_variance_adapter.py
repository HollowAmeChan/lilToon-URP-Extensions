"""Regenerate/check the two narrow DenseDI adaptations of pinned NRD 4.17.3."""
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
    notice = ("// Ho-RTDI dense-signal adaptation of NRD 4.17.3 RELAX_AtrousSmem.\n"
              f"// Original SHA256: {expected}\n"
              "// Only computeVariance changes: blur per-pixel temporal variance,\n"
              "// excluding between-pixel lighting/material contrast from noise variance.\n"
              "// Original source/license remain in ../../ThirdParty/NRD.\n")
    return notice + derived


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
    outputs = {OUTPUT: generate(), OUTPUT.with_name("HoRTDINrdHistoryClamping.hlsl"): generate_clamping()}
    for path, generated in outputs.items():
        if args.check:
            if path.read_text(encoding="utf-8-sig") != generated:
                raise SystemExit(f"DenseDI adaptation differs from pinned reconstruction: {path}")
        else:
            path.write_text(generated, encoding="utf-8", newline="\n")
            print(path)
    if args.check:
        print("PASS: original SHAs and both narrow adaptations match")
