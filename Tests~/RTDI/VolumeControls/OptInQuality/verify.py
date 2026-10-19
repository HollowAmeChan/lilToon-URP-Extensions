"""Verify preserved explicit-enable / sampling-Volume qualification."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit")
args = parser.parse_args()


def require(ok, message):
    if not ok:
        raise SystemExit("FAIL: " + message)


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


manifest = read("evidence.json")
for path, digest in manifest["artifacts"].items():
    require(hashlib.sha256((HERE / path).read_bytes()).hexdigest() == digest, "artifact changed: " + path)
for path, digest in manifest["sources"].items():
    if args.source_commit:
        value = subprocess.run(["git", "show", f"{args.source_commit}:{path}"], cwd=REPO,
                               check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
    else:
        value = (REPO / path).read_text(encoding="utf-8-sig")
    require(hashlib.sha256(value.encode()).hexdigest() == digest, "source changed: " + path)

disabled = (
    "Unchecked enable override disables production",
    "Explicit Volume off disables production",
    "Inactive Volume component disables production",
    "Zero Volume weight disables production",
    "Feature without participating Volume stays disabled",
    "Profile without RTDI component stays disabled",
)
for path in ("rg", "compat"):
    report = read("volume-" + path + ".json")
    require(report["pass"] and report["featureUntouched"] and report["legacyFallback"], "Volume run failed")
    require(report["compatibility"] == (path == "compat") and len(report["checks"]) == 86, "Volume checks incomplete")
    for label in disabled:
        for suffix in ("", " clears published output", " preserves camera"):
            require(label + suffix in report["checks"], "disabled state unqualified: " + label)
    for label in ("Volume Power/triple execute actual GPU resource paths", "Volume temporal sampling reuses stable history",
                  "Weighted sampling sliders and toggles A", "Weighted sampling sliders and toggles B",
                  "Cleared quality overrides preserve serialized tuning", "Quality slider bounds"):
        require(label in report["checks"], "quality state unqualified: " + label)
    for actual, expected in (("cameraA", "expectedA"), ("cameraB", "expectedB")):
        require(max(abs(report[actual][c] - report[expected][c]) for c in "rgb") < .006, "camera composite mismatch")
    require(read("blend-" + path + ".json")["pass"], "blend regression failed")
    snapshot = read("snapshot-" + path + ".json")
    require(snapshot["pass"] and snapshot["oldBudget"] == 1 and snapshot["newBudget"] == 3, "snapshot regression failed")
    jitter = read("jitter-" + path + ".json")
    require(jitter["pass"] and jitter["sceneClockProgressedWithFrozenGameTime"], "TAA regression failed")
    fixed, old = jitter["repaired"], jitter["legacy"]
    require((fixed["main"], fixed["fast"], fixed["previewSamples"]) == (30, 6, 64), "settled history regressed")
    require(fixed["raw"] == old["raw"], "raw inputs differ in TAA comparison")
    require(fixed["filtered"]["normalizedTemporalVariance"] < old["filtered"]["normalizedTemporalVariance"] / 10,
            "TAA filtered variance regressed")
exits = read("exits.json")
require(len(exits) == 8 and all(code == 0 for code in exits.values()), "process failed")
print("PASS: explicit Volume opt-in, 86 checks per GPU path, sampling quality, independent cameras, composite/snapshot/TAA regressions")
print("Preserved Editor evidence; no fresh Unity run, Player build or Inspector screenshot.")
