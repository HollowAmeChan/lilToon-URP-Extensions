"""Verify captured normal-camera evidence; this does not launch Unity."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]


def require(condition, message):
    if not condition:
        raise SystemExit("FAIL: " + message)


manifest = json.loads((ROOT / "evidence.json").read_text(encoding="utf-8"))
for filename, expected in manifest["artifactSha256"].items():
    require(hashlib.sha256((ROOT / filename).read_bytes()).hexdigest() == expected,
            "captured artifact changed: " + filename)
for filename, expected in manifest["sourceSha256"].items():
    source = (REPO / filename).read_text(encoding="utf-8-sig").encode("utf-8")
    require(hashlib.sha256(source).hexdigest() == expected, "audited source changed: " + filename)
for filename, expected in manifest["binarySha256"].items():
    path = REPO / filename
    if path.exists():
        require(hashlib.sha256(path.read_bytes()).hexdigest() == expected,
                "captured build binary changed: " + filename)

for colour in ("gamma", "linear"):
    for mode in ("rg", "compat"):
        name = f"editor-{colour}-{mode}.json"
        report = json.loads((ROOT / "results" / name).read_text(encoding="utf-8-sig"))
        require(report["pass"] and report["compatibility"] == (mode == "compat")
                and report["colorSpace"].lower() == colour, "Editor path/colour failed: " + name)
        require(len(report["checks"]) == 18 and len(set(report["checks"])) == 18,
                "incomplete Editor assertions: " + name)

for mode in ("rg", "compat"):
    name = f"player-linear-{mode}.json"
    report = json.loads((ROOT / "results" / name).read_text(encoding="utf-8-sig"))
    require(report["pass"] and not report["error"] and report["serializedComposite"],
            "Player failed or did not use the serialized setting: " + name)
    require(report["api"] == "Direct3D12"
            and report["mode"] == ("compatibility" if mode == "compat" else "RenderGraph"),
            "wrong Player API/mode: " + name)
    require(report["observedFrames"] == 96 and report["automaticCaptures"] >= 96,
            "automatic camera observation incomplete: " + name)
    require(report["automaticCameraBegins"] >= 96 and report["automaticContexts"] > 0,
            "hidden host updates were mistaken for actual camera renders: " + name)
    for field in ("disabledError", "opaqueError", "filteredError", "shadowError", "weightError", "reenabledError"):
        require(0 <= report[field] < .004, "camera error exceeded the original gate: " + field)
    require(report["emission"] > 1, "HDR emission was lost")

unsupported = json.loads((ROOT / "results" / "player-dx11.json").read_text(encoding="utf-8-sig"))
require(unsupported["pass"] and unsupported["api"] == "Direct3D11" and not unsupported["error"],
        "unsupported platform activated or failed")
require(all(value == 0 for value in manifest["qualificationExitCodes"].values()),
        "a qualification process failed")
regression = json.loads((ROOT / "results/regression-exits.json").read_text(encoding="utf-8"))
baseline = json.loads((ROOT / "results/baseline-regression-exits.json").read_text(encoding="utf-8"))
for mode in ("rg", "compat"):
    require(regression["composite-regression-core-" + mode] == baseline["composite-baseline-core-" + mode] == 1,
            "known full-core failure was erased or changed")
require(regression["composite-regression-nrd-compat"] == 0, "NRD compatibility regression failed")
require(not manifest["fullNprIntegration"] and not manifest["gpuBudgetEstablished"],
        "evidence overstates NPR/performance coverage")
print("PASS: normal-camera replacement, 4 Editor colour/path cases, 2 actual Mono Players and DX11 inactive")
print("KNOWN FAILURE: full-core Power bright-identity-swap variance gate fails on both f537c26 and the current source, both paths.")
print("NPR/GI artistic integration, object motion and GPU budgets remain outside this acceptance.")
