"""Check the preserved Scene View evidence; this does not rerun Unity."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]


def require(condition, message):
    if not condition:
        raise SystemExit("FAIL: " + message)


def report(name):
    return json.loads((ROOT / "results" / name).read_text(encoding="utf-8-sig"))


manifest = json.loads((ROOT / "evidence.json").read_text(encoding="utf-8"))
for name, expected in manifest["artifactSha256"].items():
    require(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == expected, "artifact changed: " + name)
for name, expected in manifest["sourceSha256"].items():
    source = (REPO / name).read_text(encoding="utf-8-sig").encode("utf-8")
    require(hashlib.sha256(source).hexdigest() == expected, "source changed: " + name)

before = report("live-before.json")["samples"]
after = report("live-preview-fixed.json")["samples"]
require(any(x["diffuseHistory"] == x["specularHistory"] == 1 for x in before), "live starvation not reproduced")
require(after[-1]["diffuseHistory"] >= 30 and after[-1]["specularHistory"] >= 30
        and after[-1]["accumulatedPreviewFrames"] == 64, "live preview did not accumulate")
pair = report("live-reservoir-20-vs4.json")
require(pair["complete"] and not pair["error"] and len(pair["cases"]) == 2, "live pair incomplete")
old, new = pair["cases"]
require(old["reservoirHistory"] == 20 and new["reservoirHistory"] == 4, "wrong paired budgets")
require(new["combined"]["normalizedTemporalVariance"] < old["combined"]["normalizedTemporalVariance"] / 5,
        "pixel variance benefit missing")
require(new["coarseCombined"]["normalizedTemporalVariance"] < old["coarseCombined"]["normalizedTemporalVariance"] / 5,
        "coarse temporal benefit missing")
require(abs(new["combined"]["mean"] / old["combined"]["mean"] - 1) < .05, "ROI energy changed substantially")
for mode in ("rg", "compat"):
    result = report("slow-" + mode + ".json")
    a, b = result["legacy"], result["repaired"]
    require(result["pass"] and a["resets"] == 192 and b["resets"] == 1, "slow interval policy failed")
    require(b["diffuseHistory"] >= 30 and b["specularHistory"] >= 30 and result["changeRestarts"] == 1,
            "preview budget/change lifecycle failed")
    require(a["raw"] == b["raw"], "preview policy changed raw samples")
    require(b["filtered"]["normalizedTemporalVariance"] < a["filtered"]["normalizedTemporalVariance"] / 4,
            "controlled variance benefit missing")
require(all(x == 0 for x in report("regression-exits.json").values()), "qualification process failed")
require(report("renderer-change.json")["onlySettingChanged"] == "RTDI.maxHistory", "renderer change widened")
print("PASS: actual Scene View history, bounded preview sampling, 20/4 live pixel/coarse variance, two GPU paths and regressions")
print("Live results cover one fixed ROI; full-scene energy, zero residual noise and GPU performance are not certified.")
