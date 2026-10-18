"""Check the preserved low-FPS fast-history repair evidence, not a Unity rerun."""
import hashlib
import json
import argparse
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]


def require(ok, message):
    if not ok:
        raise SystemExit("FAIL: " + message)


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


manifest = read("evidence.json")
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit", help="Check the captured historical source instead of the current checkout")
args = parser.parse_args()


def source_bytes(name):
    if args.source_commit:
        return subprocess.run(["git", "show", f"{args.source_commit}:{name}"], cwd=REPO,
                              check=True, stdout=subprocess.PIPE).stdout
    return (REPO / name).read_bytes()


for name, digest in manifest["artifacts"].items():
    content = source_bytes((HERE / name).relative_to(REPO).as_posix()) if name == "verify.py" and args.source_commit else (HERE / name).read_bytes()
    require(hashlib.sha256(content).hexdigest() == digest, "artifact changed: " + name)
for name, digest in manifest["sources"].items():
    require(hashlib.sha256(source_bytes(name).decode("utf-8-sig").replace("\r\n", "\n").encode()).hexdigest() == digest,
            "source changed: " + name)

live = read("live-pair.json")
require(live["complete"] and not live["error"], "live observation incomplete")
old, new = live["cases"]
require(old["mode"] == "legacyFastZero" and new["mode"] == "production", "unexpected live modes")
for case in (old, new):
    require(case["resets"] == 0 and case["stableFrames"] == 96 and case["main"] >= 30, "live stability/budget lost")
require(old["fast"] == 0 and new["fast"] >= 6, "fast-history control missing")
a = {s["name"]: s for s in old["signals"]}
b = {s["name"]: s for s in new["signals"]}
for name in ("rawDiffuse", "rawSpecular"):
    require(a[name] == b[name], "raw statistics changed: " + name)
for name, factor in (("filteredDiffuse", 40), ("filteredSpecular", 15), ("cameraOutput", 40)):
    require(b[name]["blocks"]["normalizedTemporalVariance"] < a[name]["blocks"]["normalizedTemporalVariance"] / factor,
            "normalized coarse benefit missing: " + name)
for filtered, raw in (("filteredDiffuse", "rawDiffuse"), ("filteredSpecular", "rawSpecular")):
    require(a[filtered]["pixels"]["mean"] / a[raw]["pixels"]["mean"] < .25, "old brightness-loss control missing")
    require(abs(b[filtered]["pixels"]["mean"] / b[raw]["pixels"]["mean"] - 1) < .1, "finite-window mean mismatch")
for mode in ("rg", "compat"):
    r = read("slow-" + mode + ".json")
    require(r["pass"] and r["legacy"]["resets"] == 192 and r["repaired"]["resets"] == 1 and r["changeRestarts"] == 1,
            "slow/reset response regression: " + mode)
    for key in ("diffuseHistory", "specularHistory"):
        require(r["repaired"][key] >= 30, "main budget collapsed")
    for key in ("diffuseFast", "specularFast"):
        require(r["repaired"][key] >= 6, "fast budget collapsed")
    require(r["legacy"]["raw"] == r["repaired"]["raw"], "controlled raw changed")
require(all(value == 0 for value in read("regression-exits.json").values()), "regression process failed")
print("PASS: live fast-history0/6, identical raw statistics, normalized coarse variance, restored ROI brightness, two GPU paths and camera timing")
print("One fixed ROI; nonzero residual variance; normalized benefits are not absolute-noise or convergence-time claims.")
if args.source_commit:
    print("Historical source checked at " + args.source_commit + "; this verifier does not rerun the current runtime.")
