"""Verify the rollback's bounded evidence without relabeling historical runs."""
import hashlib
import json
from pathlib import Path
import subprocess

folder = Path(__file__).resolve().parent
root = folder.parents[2]


def read(name):
    return json.loads((folder / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def require(condition, message):
    if not condition:
        raise SystemExit("FAIL: " + message)


review = read("review.json")
baseline = review["baselineCommit"]
subprocess.run(["git", "diff", "--exit-code", baseline, "--", "Runtime", "Editor"],
               cwd=root, check=True)
untracked = subprocess.check_output(
    ["git", "ls-files", "--others", "--exclude-standard", "Runtime", "Editor"], cwd=root)
require(not untracked.strip(), "Untracked runtime/editor sources outside baseline")

for item in read("artifacts.json"):
    require(sha(folder / item["path"]) == item["sha256"], "Evidence changed: " + item["path"])
    committed = subprocess.check_output(
        ["git", "show", "HEAD:Tests~/RT/Rollback/" + item["path"]], cwd=root)
    require(hashlib.sha256(committed).hexdigest().upper() == item["sha256"],
            "Committed evidence differs from raw bytes: " + item["path"])

count = 0
for accepted in review["accepted"]:
    run = read(accepted["run"])
    require(bool(run["activeProcess"]) == accepted["partialRun"], "Run completion scope changed")
    require(sha(root / "Tests~/RT/run_validation.ps1") == run["runnerSha256"], "Runner changed")
    # The incomplete runner did not reach its final source guard. Independently
    # check every original fingerprint before accepting its completed entries.
    for source in read(run["sourcesFile"]):
        require(sha(root / source["path"]) == source["sha256"], "Source drift: " + source["path"])
    entries = {entry["name"]: entry for entry in run["entries"]}
    require(set(entries) == set(accepted["names"]), "Unexpected case set")
    for name in accepted["names"]:
        entry = entries[name]
        require(entry["pass"] and entry["marker"] and entry["actualExit"] == 0
                and not entry["diagnostics"] and entry["abort"] is None, name + " failed")
        require(sha(folder / entry["logFile"]) == entry["logSha256"], name + " log changed")
        count += 1
require(count == 7, "Expected seven completed GPU cases")

for mode in ("rg", "compat"):
    report = read(f"self-{mode}-20261028T195010839-self-report.json")
    require(report["pass"] and report["validPixels"] == 143131
            and report["falseShadowPixels"] == 0 and report["jitterFlips"] == 0
            and report["contactBlocked"] == 8448 and report["skinnedValid"] == 33792
            and report["skinnedFalse"] == 0, mode + " bounded self-shadowing contract")
    require(read(f"report-multi-{mode}-20261028T195010839.json")["pass"], mode + " multi-light")
    for projection in ("perspective", "ortho"):
        suffix = f"-20261028T195010839-self-{projection}.rgba32"
        require(sha(folder / ("self-rg" + suffix)) == sha(folder / ("self-compat" + suffix)),
                "Raw signal differs across paths: " + projection)

export = read("report-export-rg-20261028T195428259.json")
require(export["pass"] and export["samples"] == 16
        and export["multiMse"] < export["singleMse"]
        and export["multiRampMse"] < export["singleRampMse"]
        and export["arithmeticMaxError"] < 1e-6, "Same-frame SIGMA contract")
require(not read("run-20261028T194923521.json")["entries"][0]["pass"],
        "Historical stale-probe failure must remain rejected")
require(not review["productionQualifiedForRealCharacter"], "Real-character acceptance is absent")
print("PASS: pre-curved Runtime/Editor baseline, 7 bounded GPU cases, unchanged sources/evidence; real-character quality remains unqualified")
