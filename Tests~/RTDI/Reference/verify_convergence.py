"""Validate the captured comparison without relabelling known quality failures."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
RESULTS = ROOT / "results"


def load(name):
    return json.loads((RESULTS / name).read_text(encoding="utf-8-sig"))


def late(report, signal):
    return next(x for x in report["results"] if x["signal"] == signal and x["lastFrame"] == 512)


def check(condition, message):
    if not condition:
        raise SystemExit("FAIL: " + message)


baseline = load("baseline-static.json")
adaptive = load("adaptive-static-rg.json")
compat = load("adaptive-static-compat.json")
before = late(baseline, "filteredCombined")
after = late(adaptive, "filteredCombined")
check(after["normalizedTemporalVariance"] < before["normalizedTemporalVariance"] * .1,
      "static variance improvement is less than 10x")
for signal in ("rawDiffuse", "rawSpecular", "rawCombined"):
    a, b = late(baseline, signal), late(adaptive, signal)
    check(a == b, "history policy changed raw samples: " + signal)
for report in (adaptive, compat):
    check(report["historyResets"] == 1 and report["stableFrames"] > 400,
          "static history was repeatedly reset or failed to settle")
for signal in ("rawCombined", "filteredCombined"):
    check(late(adaptive, signal) == late(compat, signal), "RG/compatibility convergence differs")

for filename, filtered in (("reference-rr.json", "rrCombined"),
                           ("reference-native-relax.json", "relaxCombined")):
    report = load(filename)
    check(not report["emptyEmitterGuard"], "reference omitted its preparation pass")
    raw, output = late(report, "rawCombined"), late(report, filtered)
    check(raw["mean"] > 0 and output["mean"] > 0, "reference output is black")
    check(output["normalizedTemporalVariance"] < raw["normalizedTemporalVariance"] * .01,
          "reference reconstruction failed to reduce variance")
    check(abs(raw["mean"] / late(adaptive, "rawCombined")["mean"] - 1) < .03,
          "reference and Ho raw energy differ by more than 3%")

for filename in ("adaptive-dynamic-rg.json", "adaptive-dynamic-compat.json",
                 "adaptive-specular-motion.json", "reuse-noise.json", "primary-motion.json"):
    check(load(filename)["qualityPass"], "regression gate failed: " + filename)

old_motion = {x["mode"]: x for x in load("baseline-motion.json")["cases"]}
for filename in ("adaptive-motion-rg.json", "adaptive-motion-compat.json"):
    report = load(filename)
    check(not report["qualityPass"], "the known strict step failure was hidden")
    for case in report["cases"]:
        check(case["rawPass"], "motion raw energy failed")
        if old_motion[case["mode"]]["filteredPass"]:
            check(case["filteredPass"], "new failed motion mode: " + case["mode"])
    step = next(x for x in report["cases"] if x["mode"] == "casterStep")
    check(step["transitionChangedL1"] <= old_motion["casterStep"]["transitionChangedL1"],
          "strict step response worsened")
    check(not step["filteredPass"] and step["transitionChangedL1"] >= .35,
          "strict step threshold was relaxed")
for filename in ("environment-rg.json", "environment-compat.json"):
    report = load(filename)
    check(not report["qualityPass"] and sum(x["pass"] for x in report["cases"]) == 2,
          "existing mixed-specular failure was erased")

for filename in ("player-rg-suite.json", "player-compat-suite.json", "player-dx11.json",
                 "player-rg-convergence.json", "player-compat-convergence.json"):
    report = load(filename)
    check(report["pass"] and not report["error"], "actual Player failed: " + filename)
    if "convergence" in filename:
        check(report["observedFrames"] == 192 and report["stableFrames"] > 100,
              "Player did not observe actual stable application frames")
        check(report["staticHistoryBudget"] > report["motionHistoryBudget"]
              and report["motionRestarts"] == 1 and report["oldColourTail"] < .001,
              "Player stability/response transition failed")

manifest_path = ROOT / "evidence.json"
if manifest_path.exists():
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    for filename, expected in manifest["reportSha256"].items():
        check(hashlib.sha256((RESULTS / filename).read_bytes()).hexdigest() == expected,
              "captured report changed: " + filename)
    repo = ROOT.parents[2]
    for filename, expected in manifest["sourceSha256"].items():
        source = (repo / filename).read_text(encoding="utf-8-sig").encode("utf-8")
        check(hashlib.sha256(source).hexdigest() == expected,
              "audited source changed: " + filename)
    for filename, expected in manifest["binarySha256"].items():
        path = repo / filename
        if path.exists():
            check(hashlib.sha256(path.read_bytes()).hexdigest() == expected,
                  "audited binary changed: " + filename)

print("PASS: reference reconstruction, unchanged raw, >10x static improvement, runtime transitions and Player")
print("KNOWN QUALITY FAILURES: 60 Hz caster step; mixed-environment specular (2/4).")
print("These remain failures; this verifier does not certify complete RTDI image quality.")
