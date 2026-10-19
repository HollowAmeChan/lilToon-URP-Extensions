"""Verify preserved RTDI blend evidence; does not rerun Unity."""
import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[3]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit", help="Check captured source at a historical revision")
args = parser.parse_args()


def require(ok, message):
    if not ok:
        raise SystemExit("FAIL: " + message)


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


m = read("evidence.json")
for name, digest in m["artifacts"].items():
    require(hashlib.sha256((HERE / name).read_bytes()).hexdigest() == digest, "artifact changed: " + name)
for name, digest in m["sources"].items():
    if args.source_commit:
        content = subprocess.run(["git", "show", f"{args.source_commit}:{name}"], cwd=REPO,
                                 check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
    else:
        content = (REPO / name).read_text(encoding="utf-8-sig")
    require(hashlib.sha256(content.encode()).hexdigest() == digest, "source changed: " + name)

required = {"linear HDR addition gain=0", "linear HDR addition gain=0.25",
            "linear HDR addition gain=1", "linear HDR addition gain=2",
            "additive does not duplicate emission in zero direct light",
            "added light fog transmission without duplicate fog colour",
            "transparent blends after additive composite", "additive consumes filtered publication",
            "blend switch preserves accumulation", "additive protects foreground Unlit depth"}
for color in ("gamma", "linear"):
    for path in ("rg", "compat"):
        for mode in ("blend", "replace"):
            r = read(f"{color}-{path}-{mode}.json")
            require(r["pass"] and r["compatibility"] == (path == "compat") and r["colorSpace"].lower() == color,
                    "wrong/incomplete GPU case")
            require(len(r["checks"]) == 18, "GPU checks missing")
            if mode == "blend":
                require(required <= set(r["checks"]), "blend contracts missing")
exits = read("exits.json")
require(len(exits) == 10 and all(code == 0 for code in exits.values()), "GPU/configuration process failed")
for name in ("original", "replace", "additive"):
    data = (HERE / "images" / (name + ".png")).read_bytes()
    require(data[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", data[16:24]) == (384, 256), "camera capture missing")
print("PASS: both blend modes in Gamma/Linear x RG/compatibility, gains, emission/fog, depth/transparency, history and legacy defaults; 8 GPU cases")
print("Preserved controlled Editor evidence; no new Player or full-scene quality certification.")
