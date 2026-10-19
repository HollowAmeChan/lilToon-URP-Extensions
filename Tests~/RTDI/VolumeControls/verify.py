"""Verify preserved RTDI Volume controls evidence, not a Unity rerun."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit", help="Check captured source at a historical revision")
args = parser.parse_args()


def require(ok, message):
    if not ok:
        raise SystemExit("FAIL: " + message)


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


m = read("evidence.json")
for path, digest in m["artifacts"].items():
    require(hashlib.sha256((HERE / path).read_bytes()).hexdigest() == digest, "artifact changed: " + path)
for path, digest in m["sources"].items():
    if args.source_commit:
        value = subprocess.run(["git", "show", f"{args.source_commit}:{path}"], cwd=REPO,
                               check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
    else:
        value = (REPO / path).read_text(encoding="utf-8-sig")
    require(hashlib.sha256(value.encode()).hexdigest() == digest, "source changed: " + path)
for path in ("rg", "compat"):
    r = read("volume-" + path + ".json")
    require(r["pass"] and r["legacyFallback"] and r["featureUntouched"] and len(r["checks"]) == 28,
            "Volume qualification incomplete")
    require(r["compatibility"] == (path == "compat") and r["localWeight"] == .5, "wrong local-Volume case")
    for actual, expected in (("cameraA", "expectedA"), ("cameraB", "expectedB")):
        require(max(abs(r[actual][c] - r[expected][c]) for c in "rgb") < .006, "camera Volume output mismatch")
    require(abs(r["cameraA"]["r"] - r["cameraB"]["r"]) > .05, "independent camera gains not visible")
    require("Volume-only denoising enters Scene View preview despite legacy Feature being off" in r["checks"],
            "Scene View Volume-only denoising untested")
    require(read("blend-" + path + ".json")["pass"] and read("slow-" + path + ".json")["pass"], "regression failed")
require(read("sky.json")["pass"] and read("snapshot.json")["pass"], "sky/snapshot regression failed")
exits = read("exits.json")
require(len(exits) == 8 and all(code == 0 for code in exits.values()), "qualification process failed")
print("PASS: actual Volume appearance, local weights, independent cameras, immutable Feature, legacy fallback, history and sliders; both GPU paths and regressions")
print("Preserved Editor evidence; no new Player or Inspector screenshot qualification.")
