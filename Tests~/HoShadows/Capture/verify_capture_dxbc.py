"""Verify actual capture fragment instructions with the Skin package's D3D helper."""

import argparse
import importlib.util
import json
import re
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("results", type=Path)
    parser.add_argument("skin_package", type=Path)
    options = parser.parse_args()
    helper_path = options.skin_package / "Tests~/HoShadows/disassemble_dxbc.py"
    spec = importlib.util.spec_from_file_location("skin_dxbc", helper_path)
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    compile_report = json.loads((options.results / "capture-managed-dxbc.json").read_text(encoding="utf-8-sig"))
    suffix = r"(?:_indexable\([^)]*\)\([^)]*\))?(?:_aoffimmi\([^)]*\))?"
    checks = []
    for variant in compile_report["variants"]:
        artifact = options.results / variant["artifact"]
        assembly = helper.disassemble(helper.dxbc_payload(artifact.read_bytes()))
        artifact.with_suffix(".asm").write_text(assembly, encoding="utf-8")
        loads = {
            name: len(helper.matching_ops(assembly, r"ld" + suffix, "t" + register))
            for kind, register, name in (entry.split(":", 2) for entry in variant["bindings"])
            if kind == "Texture" and name.startswith("_HoShadowsCapture")
        }
        comparison_count = len(re.findall(rf"^\s*sample_c(?:_lz)?{suffix}[ \t]", assembly, re.M))
        checks.append({"artifact": artifact.name, "shader": variant["shader"], "keywords": variant["keywords"],
                       "comparisonInstructions": comparison_count, "captureTextureLoads": loads,
                       "passed": bool(loads) and all(loads.values()) and comparison_count == 0})
    passed = compile_report["passed"] and len(checks) == 4 and all(check["passed"] for check in checks)
    (options.results / "capture-managed-disassembly.json").write_text(
        json.dumps({"passed": passed, "variants": checks}, indent=2) + "\n", encoding="utf-8")
    if not passed:
        raise SystemExit("Expected four Capture variants with screen loads and zero shadow comparisons.")


if __name__ == "__main__":
    main()
