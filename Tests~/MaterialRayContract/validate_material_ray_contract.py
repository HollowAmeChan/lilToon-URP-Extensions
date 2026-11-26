#!/usr/bin/env python3
"""Static contract audit for lilToon/HoURP transparent and ray materials.

The audit deliberately does not open Unity or mutate BREAK_URP.  It records
source evidence for the contracts that can be checked without a GPU and emits
an explicit ``unverified`` section for the runtime checks that still require
an isolated Unity/D3D12 project.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable


@dataclass
class Check:
    id: str
    area: str
    status: str
    description: str
    evidence: list[dict]
    caveat: str = ""


class Audit:
    def __init__(self, root: Path, liltoon: Path):
        self.root = root.resolve()
        self.liltoon = liltoon.resolve()
        self.checks: list[Check] = []

    def source(self, path: Path) -> tuple[str, Path]:
        p = path.resolve()
        if not p.exists():
            return "", p
        return p.read_text(encoding="utf-8-sig", errors="replace"), p

    @staticmethod
    def evidence(path: Path, text: str, patterns: Iterable[str]) -> list[dict]:
        rows = []
        for pattern in patterns:
            # Whole-source matching also records multi-line producer/consumer
            # chains, rather than treating an isolated symbol as sufficient.
            rx = re.compile(pattern, re.IGNORECASE | re.MULTILINE)
            hits = [text.count("\n", 0, match.start()) + 1 for match in rx.finditer(text)]
            rows.append({"pattern": pattern, "lines": hits[:8]})
        return rows

    def require(self, id: str, area: str, description: str, files: list[Path], patterns: dict[Path, list[str]], caveat: str = ""):
        evidence = []
        missing = []
        failed = []
        for file in files:
            text, path = self.source(file)
            rel = str(path.relative_to(self.root)) if path.is_relative_to(self.root) else str(path)
            if not text:
                missing.append(rel)
                continue
            entries = self.evidence(path, text, patterns.get(file, []))
            evidence.append({"file": rel, "sha256": hashlib.sha256(text.replace("\r\n", "\n").encode()).hexdigest(), "matches": entries})
            if any(not e["lines"] for e in entries):
                failed.append(rel)
        status = "pass" if not missing and not failed else "fail"
        detail = description
        if missing:
            detail += " Missing: " + ", ".join(missing)
        if failed:
            detail += " Pattern not found in: " + ", ".join(failed)
        self.checks.append(Check(id, area, status, detail, evidence, caveat))

    def note(self, id: str, area: str, description: str, caveat: str):
        self.checks.append(Check(id, area, "unverified", description, [], caveat))

    def run(self):
        r = self.root
        l = self.liltoon
        self.require("oit.resolve", "transparent/OIT", "Weighted OIT clips alpha and writes weighted accumulation plus revealage.",
                     [r / "Runtime/OIT/WeightedOIT.hlsl"], {r / "Runtime/OIT/WeightedOIT.hlsl": [r"clip\s*\(\s*color\.a\s*-\s*_lilOITAlphaClipThreshold", r"output\.accumulation\s*=", r"output\.revealage\s*="]},
                     "Static shader contract only; blend order and visual correctness need GPU capture.")
        self.require("oit.feature", "transparent/OIT", "Ho-WeightedOIT resets camera state and sets up/enqueues clear, opaque copy, accumulation and composite in order.",
                     [r / "Runtime/OIT/WeightedOITRendererFeature.cs"], {r / "Runtime/OIT/WeightedOITRendererFeature.cs": [
                         r"resetPass\s*=\s*new\s+WeightedOITResetPass\s*\(",
                         r"RenderPipelineManager\.beginCameraRendering\s*\+=\s*ResetOITState",
                         r"Shader\.SetGlobalFloat\(WeightedOITShaderConstants\.OITActiveId,\s*0\.0f\)",
                         r"clearPass\.Setup\(settings,\s*renderTargets\)",
                         r"opaqueCopyPass\.SetupRenderGraph\(settings,\s*renderTargets\)",
                         r"accumulationPass\.SetupRenderGraph\(settings,\s*renderTargets\)",
                         r"compositePass\.SetupRenderGraph\(settings,\s*renderTargets,\s*compositeMaterial\)",
                         r"HoDebugPerformanceRegistry\.EnqueuePass\(renderer,\s*renderingData\.cameraData\.camera,\s*clearPass,\s*Performance\);[\s\S]*?"
                         r"HoDebugPerformanceRegistry\.EnqueuePass\(renderer,\s*renderingData\.cameraData\.camera,\s*opaqueCopyPass,\s*Performance\);[\s\S]*?"
                         r"HoDebugPerformanceRegistry\.EnqueuePass\(renderer,\s*renderingData\.cameraData\.camera,\s*accumulationPass,\s*Performance\);[\s\S]*?"
                         r"HoDebugPerformanceRegistry\.EnqueuePass\(renderer,\s*renderingData\.cameraData\.camera,\s*compositePass,\s*Performance\);"]},
                     "Source scheduling and state-reset contract only; resource bindings and pass execution still need GPU qualification.")
        self.require("liltoon.alpha", "transparent/Cutout", "lilToon forward alpha uses cutoff and Ho surface alpha rejects invisible ordinary transparent fragments.",
                     [l / "Assets/lilToon/Shader/Includes/lil_common_frag_alpha.hlsl", l / "Assets/lilToon/Shader/Includes/lil_ho_surface_alpha.hlsl"], {
                         l / "Assets/lilToon/Shader/Includes/lil_common_frag_alpha.hlsl": [r"clip\s*\(\s*fd\.col\.a\s*-\s*_Cutoff"],
                         l / "Assets/lilToon/Shader/Includes/lil_ho_surface_alpha.hlsl": [r"LIL_RENDER\s*==\s*2", r"clip\s*\(\s*alpha\s*-\s*_Cutoff", r"clip\s*\(\s*alpha\s*>\s*0\.0"]})
        oit_shaders = [l / "Assets/lilToon/Shader" / name for name in (
            "lts_trans.shader", "lts_trans_o.shader", "lts_twotrans.shader", "lts_twotrans_o.shader")]
        oit_patterns = {shader: [
            r'Name\s+"LILTOON_OIT"(?:(?!ENDHLSL)[\s\S])*?Tags\s*\{\s*"LightMode"\s*=\s*"lilToonOIT"\s*\}'
            r'(?:(?!ENDHLSL)[\s\S])*?Blend\s+0\s+One\s+One\s+Blend\s+1\s+Zero\s+OneMinusSrcColor'
            r'(?:(?!ENDHLSL)[\s\S])*?#define\s+LIL_OIT_PASS\b'
            r'(?:(?!ENDHLSL)[\s\S])*?#include\s+"Includes/lil_pass_forward\.hlsl"'] for shader in oit_shaders}
        oit_patterns.update({
            l / "Assets/lilToon/Shader/Includes/lil_pass_forward.hlsl": [r'#include\s+"lil_pass_forward_normal\.hlsl"'],
            l / "Assets/lilToon/Shader/Includes/lil_oit.hlsl": [
                r"output\.accumulation\s*=\s*float4\(color\.rgb\s*\*\s*weight,\s*alpha\s*\*\s*weight\)",
                r"output\.revealage\s*=\s*float4\(alpha,",
                r"#define\s+LIL_OIT_RETURN\(color,\s*positionCS\)\s+return\s+lilOITOutputColor\(color,"],
            l / "Assets/lilToon/Shader/Includes/lil_pass_forward_normal.hlsl": [
                r'#if\s+defined\(LIL_OIT_PASS\)\s+#include\s+"lil_oit\.hlsl"',
                r"#define\s+LIL_FORWARD_FRAGMENT_RETURN_TYPE\s+LIL_OIT_FRAGMENT_RETURN_TYPE",
                r"clip\(_lilOITEnabled\s*-\s*0\.5\)",
                r"clip\(0\.5\s*-\s*_lilOITEnabled\s*\*\s*_lilOITActive\)",
                r"#if\s+defined\(LIL_OIT_PASS\)\s+LIL_OIT_RETURN\(fd\.col,\s*input\.positionCS\)"]})
        self.require("liltoon.oit-entry", "transparent/OIT", "Native transparent/OIT Passes route through the normal forward consumer into gated weighted MRT output.",
                     list(oit_patterns), oit_patterns,
                     "The retired Hair include is not a native consumer. This source chain does not establish GPU blend/depth correctness.")
        self.require("rtgi.sky-exclusion", "RTGI sky exclusion", "RTGI exposes explicit excluded material inputs and forwards them to the ray scene synchronizer.",
                     [r / "Runtime/RTGI/HoRTGISettings.cs", r / "Runtime/RTGI/HoRTGIRendererFeature.cs", r / "Runtime/RT/HoRTScene.cs"], {
                         r / "Runtime/RTGI/HoRTGISettings.cs": [r"excludedRayMaterials", r"includeDirectEnvironment", r"useEnvironment"],
                         r / "Runtime/RTGI/HoRTGIRendererFeature.cs": [r"new HoRTViewRequest", r"excludedRayMaterials", r"useEnvironment", r"includeDirectEnvironment"],
                         r / "Runtime/RT/HoRTScene.cs": [r"excludedMaterials", r"excluded\.Contains", r"excluded by material filter"]})
        self.require("rtgi.material-domain", "Ray material domain", "GI adapters preserve source properties while rejecting unsupported animated/parallax material domains.",
                     [r / "Runtime/RTGI/HoRTGISecondaryMaterials.cs", r / "ShaderLibrary/RTGI/HoRTGISecondaryMaterial.hlsl"], {
                         r / "Runtime/RTGI/HoRTGISecondaryMaterials.cs": [r"CopyPropertiesFromMaterial", r"HoRTCutoutSupport\.Supports", r"remains opaque to RT"],
                         r / "ShaderLibrary/RTGI/HoRTGISecondaryMaterial.hlsl": [r"perceptualRoughness\s*=\s*saturate\(1-smoothness\)", r"metallic\s*=\s*saturate", r"reflectance\s*=\s*saturate"]})
        self.require("ray.surface-material", "Ray material domain", "Primary ray surface reads SB material channels as perceptual roughness/metallic and validates finite data.",
                     [r / "Runtime/RT/Shaders/HoRTPrimarySurface.hlsl", r / "Runtime/RT/HoRTPrimarySurfaceInputs.cs"], {
                         r / "Runtime/RT/Shaders/HoRTPrimarySurface.hlsl": [r"value\.perceptualRoughness\s*=\s*material\.r", r"value\.metallic\s*=\s*material\.g", r"value\.reflectance\s*=\s*SAMPLE_TEXTURE2D_X_LOD", r"HoRTPrimaryFinite3"],
                         r / "Runtime/RT/HoRTPrimarySurfaceInputs.cs": [r"materialTexture", r"reflectionTexture", r"attributes"]})
        self.require("plr.material-gate", "PLR/reflection", "PLR composite derives smoothness from perceptual roughness and gates by metallic/F0/strength.",
                     [r / "Runtime/PlanarReflection/HoPlanarReflectionComposite.shader", r / "Runtime/PlanarReflection/HoPlanarReflectionRendererFeature.cs", l / "Assets/lilToon/Shader/Includes/lil_common_frag.hlsl"], {
                         r / "Runtime/PlanarReflection/HoPlanarReflectionComposite.shader": [r"perceptualRoughness", r"smoothness\s*=\s*1\.0h\s*-\s*perceptualRoughness", r"metallic", r"reflectance"],
                         r / "Runtime/PlanarReflection/HoPlanarReflectionRendererFeature.cs": [r"minSmoothness", r"_UsePlanarReflection", r"reflectionMaterial"],
                         l / "Assets/lilToon/Shader/Includes/lil_common_frag.hlsl": [r"_UsePlanarReflection", r"_PlanarReflectionStrength", r"smoothnessFade"]})
        self.require("apv.variant", "APV/probe", "Native Standard/Cutout variants reach the unified environment evaluator, which samples/evaluates APV only for non-lightmapped receivers.",
                     [l / "Assets/lilToon/Shader/lts.shader", l / "Assets/lilToon/Shader/lts_cutout.shader", l / "Assets/lilToon/Shader/Includes/lil_common_vert.hlsl", l / "Assets/lilToon/Shader/Includes/lil_common_macro.hlsl", l / "Assets/lilToon/Shader/Includes/lil_common_functions.hlsl", r / "Runtime/Debug/Shaders/HoDebugTile.shader"], {
                         l / "Assets/lilToon/Shader/lts.shader": [r"ProbeVolumeVariants\.hlsl", r"_REFLECTION_PROBE_BLENDING"],
                         l / "Assets/lilToon/Shader/lts_cutout.shader": [r"ProbeVolumeVariants\.hlsl", r"_REFLECTION_PROBE_BLENDING"],
                         l / "Assets/lilToon/Shader/Includes/lil_common_vert.hlsl": [r"LIL_CALC_MAINLIGHT\(vertexInput,\s*lightdataInput\)"],
                         l / "Assets/lilToon/Shader/Includes/lil_common_macro.hlsl": [r"#define\s+LIL_CALC_MAINLIGHT\(i,o\)[\s\S]*?lilGetEnvironmentLighting\(i\.positionWS,\s*vertexNormalInput\.normalWS,"],
                         l / "Assets/lilToon/Shader/Includes/lil_common_functions.hlsl": [
                             r"void\s+lilEvaluateToonAPV\([^\{]+\{[\s\S]*?EvaluateAdaptiveProbeVolume\(apvSample,\s*N,\s*-N,\s*shMax,\s*shMin\)",
                             r"#if\s*!defined\(LIL_USE_LIGHTMAP\)\s*&&\s*!defined\(LIL_USE_DYNAMICLIGHTMAP\)",
                             r"#if\s+defined\(PROBE_VOLUMES_L1\)\s*\|\|\s*defined\(PROBE_VOLUMES_L2\)",
                             r"if\s*\(_EnableProbeVolumes\)\s*\{\s*APVSample\s+apvSample\s*=\s*SampleAPV\(positionWS,\s*normalWS,\s*lilGetRenderingLayer\(\)",
                             r"if\s*\(apvSample\.status\s*!=\s*APV_SAMPLE_STATUS_INVALID\)\s*\{\s*apvSample\.Decode\(\)",
                             r"lilEvaluateToonAPV\(apvSample,\s*sampleDirection,\s*shMax,\s*shMin\)"],
                         r / "Runtime/Debug/Shaders/HoDebugTile.shader": [r"SampleAPV", r"EvaluateAdaptiveProbeVolume", r"APV_SAMPLE_STATUS_INVALID"]},
                     "Source capability and consumer routing only; no claim that APV data is baked, loaded, valid or retained in Player.")
        self.note("gpu.transparent-cutout", "GPU qualification", "Render a real lilToon opaque/transparent/cutout/OIT matrix and inspect depth, coverage, ordering and shadow outputs.", "UNVERIFIED here: requires Unity 6000.3.x, URP renderer, D3D12 and actual GPU readback.")
        self.note("gpu.rtgi-sky", "GPU qualification", "Toggle an excluded sky shell and compare RTGI sky radiance while preserving raster output.", "UNVERIFIED here: source path proves the filter exists, not that the configured material is present or the result is visually correct.")
        self.note("gpu.plr-probe", "GPU qualification", "Probe PLR with metallic/smoothness extremes and verify reflection probe/APV debug views in Frame Debugger.", "UNVERIFIED here: requires a baked/loaded probe volume and reflection capture; static source cannot prove resource validity.")
        return self

    def report(self) -> dict:
        static = [c for c in self.checks if c.status != "unverified"]
        return {
            "schema": "ho.material-ray-contract.v1",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "repository": str(self.root),
            "liltoon": str(self.liltoon),
            "pass": all(c.status == "pass" for c in static),
            "staticPass": sum(c.status == "pass" for c in static),
            "staticFail": sum(c.status == "fail" for c in static),
            "unverified": sum(c.status == "unverified" for c in self.checks),
            "checks": [asdict(c) for c in self.checks],
        }

    def markdown(self, report: dict) -> str:
        lines = ["# lilToon / HoURP material-ray contract audit", "", f"Native-source audit captured: `{report['generatedUtc']}`", "", "This is a source-only audit. It does not modify BREAK_URP or run Unity.", "", "| Check | Area | Status |", "|---|---|---|"]
        for c in report["checks"]:
            lines.append(f"| `{c['id']}` | {c['area']} | **{c['status'].upper()}** |")
        lines += ["", f"Static checks: **{report['staticPass']} pass**, **{report['staticFail']} fail**. Runtime checks awaiting GPU/Unity: **{report['unverified']}**.", "", "## Evidence", ""]
        for c in report["checks"]:
            if not c["evidence"]:
                continue
            lines += [f"### `{c['id']}`", "", c["description"]]
            for e in c["evidence"]:
                matches = ", ".join(f"{m['pattern']} @ {m['lines']}" for m in e["matches"])
                lines.append(f"- `{e['file']}` ({e['sha256'][:16]}…): {matches}")
            if c["caveat"]:
                lines.append(f"- Caveat: {c['caveat']}")
            lines.append("")
        lines += ["## Re-run", "", "```powershell", "python Tests~/MaterialRayContract/validate_material_ray_contract.py", "```", "", "Unity follow-up is intentionally separate from this static command. Use an isolated project and invoke the existing RTGI/Cutout/PLR validation entry points; do not run against BREAK_URP.", ""]
        return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--liltoon", type=Path, default=None, help="Adjacent lilToon checkout (default: sibling of --repo)")
    parser.add_argument("--out", type=Path, default=None, help="Output directory (default: this directory)")
    args = parser.parse_args()
    root = args.repo.resolve()
    liltoon = (args.liltoon or root.parent / "lilToon").resolve()
    out = (args.out or Path(__file__).resolve().parent).resolve()
    out.mkdir(parents=True, exist_ok=True)
    audit = Audit(root, liltoon).run()
    report = audit.report()
    (out / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (out / "report.md").write_text(audit.markdown(report), encoding="utf-8")
    print(f"STATIC {'PASS' if report['pass'] else 'FAIL'}: {report['staticPass']} pass, {report['staticFail']} fail; {report['unverified']} GPU/Unity checks unverified")
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
