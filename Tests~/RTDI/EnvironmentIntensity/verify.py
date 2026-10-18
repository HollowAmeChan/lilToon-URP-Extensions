"""Check preserved RTDI environment-intensity evidence, not a Unity rerun."""
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
        content = subprocess.run(["git", "show", f"{args.source_commit}:{path}"], cwd=REPO,
                                 check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
    else:
        content = (REPO / path).read_text(encoding="utf-8-sig")
    require(hashlib.sha256(content.encode()).hexdigest() == digest, "source changed: " + path)
for mode, checks in (("rg", 40), ("compat", 44)):
    r = read("intensity-" + mode + ".json")
    require(r["pass"] and r["checks"] == checks, "GPU checks missing: " + mode)
    cases = {c["intensity"]: c for c in r["cases"]}
    for gain in (1, .25, 2, 0):
        c = cases[gain]
        require(c["sampling"] == (gain > 0), "sky sampling/zero mismatch")
        for channel, value in (("r", 1), ("g", .5), ("b", .25)):
            require(abs(c["diffuse"][channel] - value * gain) < .03, "independent Lambert gain mismatch")
            require(abs(c["specular"][channel] - cases[1]["specular"][channel] * gain) < .0001, "specular gain mismatch")
    require(cases[0]["risCount"] == 1, "zero sky retained RIS storage")
    require(read("snapshot-" + mode + ".json")["pass"], "immutable pass snapshot failed")
require(read("legacy.json") == {"pass": True, "serializedFieldAbsent": True, "loadedIntensity": 1}, "old asset default changed")
require(read("composite.json")["pass"], "normal camera regression failed")
exits = read("exits.json")
require(len(exits) == 6 and all(x == 0 for x in exits.values()), "qualification process failed")
print("PASS: sky gains0/.25/1/2, both lobes and GPU paths, history/zero lifetime, analytic independence, shared sky/backdrop, legacy default and snapshots")
print("Preserved evidence only; no new Player deployment or full-scene brightness acceptance.")
