"""Check preserved static TAA/NRD history evidence; not a Unity rerun."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit", help="Check source at a captured revision")
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
        content = subprocess.run(["git", "show", f"{args.source_commit}:{path}"], cwd=REPO,
                                 check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
    else:
        content = (REPO / path).read_text(encoding="utf-8-sig")
    require(hashlib.sha256(content.encode()).hexdigest() == digest, "source changed: " + path)
before = [s for s in read("live-before.json")["samples"] if s["atRender"] and s["type"] == "SceneView"]
require(len(before) > 50 and all(s["nrd"] and not s["stable"] for s in before), "live starvation not reproduced")
require(len({s["viewHash"] for s in before}) == 1 and len({s["projectionHash"] for s in before}) > 10,
        "live motion/jitter isolation missing")
require(before[-1]["main"] == 1 and before[-1]["fast"] == 0, "live short history absent")
after = [s for s in read("live-after.json")["samples"] if s["atRender"] and s["type"] == "SceneView"]
require(len(after) > 50 and sum(s["stable"] for s in after) > 30, "live recovery missing")
require(after[-1]["main"] >= 30 and after[-1]["fast"] >= 6 and after[-1]["stable"], "live settled budget lost")
require(len({s["nonJitteredHash"] for s in after}) == 1 and len({s["projectionHash"] for s in after}) > 10,
        "live jitter/lens separation missing")
for mode in ("rg", "compat"):
    r = read("taa-" + mode + ".json");a, b = r["legacy"], r["repaired"]
    require(r["pass"] and a["engineFrames"] == b["engineFrames"] == 192, "actual TAA frames missing")
    require(a["jitteredFrames"] > 180 and b["jitteredFrames"] > 180, "URP jitter was bypassed")
    require(a["stableFrames"] == 0 and a["fast"] == 0 and b["stableFrames"] > 180,
            "stability control/repair failed")
    require(b["main"] >= 30 and b["fast"] >= 6 and b["previewSamples"] == 64, "settled sample budget missing")
    require(a["raw"] == b["raw"] and a["resets"] == b["resets"] == 1, "raw sampling/reset policy changed")
    require(b["filtered"]["normalizedTemporalVariance"] < a["filtered"]["normalizedTemporalVariance"] / 4,
            "TAA variance did not improve")
    require(r["lensChangeRestarts"] == 1 and r["worstJitterPixelError"] < .001, "lens/jitter contract failed")
    require(r["sceneClockProgressedWithFrozenGameTime"] and r["sceneClockElapsed"] >= .5 and r["sceneClockRenders"] >= 8,
            "Scene View frozen-game-clock protocol failed")
exits = read("exits.json")
require(len(exits) == 8 and all(x == 0 for x in exits.values()), "GPU qualification failed")
print("PASS: live projection-jitter starvation, actual URP TAA sequences, stable30/6, unchanged raw and real lens response; both paths and regressions")
print("Preserved Editor/static evidence; full motion/TAA and Player acceptance remain separate.")
