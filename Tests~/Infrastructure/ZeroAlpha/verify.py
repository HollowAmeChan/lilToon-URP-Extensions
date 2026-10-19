"""Verify preserved zero-opacity infrastructure output evidence."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--source-commit")
parser.add_argument("--liltoon-path", type=Path, default=REPO.parent / "lilToon")
parser.add_argument("--liltoon-source-commit")
args = parser.parse_args()


def require(ok, message):
    if not ok:
        raise SystemExit("FAIL: " + message)


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


m = read("evidence.json")
for path, digest in m["artifacts"].items():
    require(hashlib.sha256((HERE / path).read_bytes()).hexdigest() == digest, "artifact changed: " + path)
for root, revision, sources in ((REPO, args.source_commit, m["sources"]),
                                (args.liltoon_path, args.liltoon_source_commit, m["liltoonSources"])):
    for path, digest in sources.items():
        if revision:
            value = subprocess.run(["git", "show", f"{revision}:{path}"], cwd=root,
                                   check=True, stdout=subprocess.PIPE).stdout.decode("utf-8-sig").replace("\r\n", "\n")
        else:
            value = (root / path).read_text(encoding="utf-8-sig")
        require(hashlib.sha256(value.encode()).hexdigest() == digest, "source changed: " + path)
for mode in ("rg", "compat"):
    r = read(mode + ".json")
    require(r["pass"] and r["checks"] == 28 and r["actualObjectSamples"] > 1, "native writer qualification incomplete")
    cases = {c["name"]: c["outputs"] for c in r["cases"]}
    back = cases["back-only"]
    for name in ("transparent-zero", "transparent-zero-cutoff-zero", "texture-alpha-zero"):
        error = max(abs(a[c] - b[c]) for a, b in zip(cases[name], back) for c in "rgba")
        require(error < .002, "invisible fragment overwrote rear output: " + name)
    front = cases["transparent-visible"]
    require(abs(front[0]["r"] - 2) < .01 and abs(back[0]["r"] - 3) < .01, "depth control missing")
    require(front[1]["g"] > .9 and back[1]["r"] > .9, "surface-colour control missing")
    require(max(abs(front[2][c] - back[2][c]) for c in "rgba") > .001, "native identity positive control absent")
    require(cases["opaque-colour-alpha-zero"][1]["g"] > .9, "opaque alpha semantics changed")
require(all(x == 0 for x in read("exits.json").values()), "zero-alpha process failed")
require(len(read("regressions.json")) == 4 and all(x == 0 for x in read("regressions.json").values()), "regression process failed")
print("PASS: actual GB/SB/OB/AC outputs preserve rear surface for zero opacity, real MSAA native control, opaque semantics and both render paths")
print("Semantic/outline payload coverage and arbitrary custom shader opacity remain bounded as documented.")
