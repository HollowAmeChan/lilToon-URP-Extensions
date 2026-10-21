"""Verify the actual fixed seed matrix, rejected byte gate and bounded conclusions."""
import gzip
import hashlib
import json
import math
import re
import struct
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
from analyze import summarize

HERE = Path(__file__).parent
ROOT = HERE.parents[2]
SEEDS = [0, 104729, 1153305, 2201881, 3250457, 4299033, 5347609, 6396185]
MODES = ["none", "temporal", "spatial", "temporal-spatial"]


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def digest(value):
    return hashlib.sha256(value).hexdigest().upper()


def vector(v):
    return [v[c] for c in "xyz"]


def close(a, b):
    assert math.isclose(a, b, rel_tol=2e-6, abs_tol=1e-10), (a, b)


index = read(HERE / "source-index.json")
for sha, item in index.items():
    value = (HERE / item["path"]).read_bytes()
    assert digest(value) == sha and len(value) == item["bytes"]
for sha in read(HERE / "source-files.json").values():
    assert sha in index
logs = read(HERE / "log-index.json")
for item in logs.values():
    assert digest((HERE / item["path"]).read_bytes()) == item["sha256"]
for name, item in read(HERE / "capture-archives.json").items():
    packed = (HERE / item["path"]).read_bytes(); value = gzip.decompress(packed)
    assert digest(packed) == item["archiveSha256"] and len(packed) == item["archiveBytes"]
    assert digest(value) == item["rawSha256"] and len(value) == item["rawBytes"]
    assert value == raw(HERE / name)

attempts = {}
for folder, expected_exit, report in [
    ("rejected-paired-hash", 1, "rejected-paired-hash/report.json"),
    ("qualification-original", 0, "results/qualification.json"),
    ("rejected-matrix-paired-hash", 1, "rejected-matrix-paired-hash/report.json"),
    ("matrix-original", 0, "results/matrix.json"),
]:
    run = read(HERE / folder / "run.json"); assert run["activeProcess"] is None and run["debugLayerRequested"]
    for source in run["sources"]:
        if source["path"].endswith(".dll"):
            assert digest((ROOT / source["path"]).read_bytes()) == source["sha256"]
        else:
            assert source["sha256"] in index
    entry = run["entries"][0]; assert entry["exitCode"] == expected_exit and entry["fresh"] and entry["abortReason"] is None
    assert entry["debugErrors"] == entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0
    assert digest(raw(HERE / report)) == entry["reportSha256"]
    log = (HERE / logs[entry["log"]]["path"]).read_text(encoding="utf-8-sig")
    assert not re.search(r"d3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|error CS|887a0006", log, re.I)
    data = read(HERE / report)
    if expected_exit:
        assert not data["pass"] and "Paired modes did not use identical" in data["error"]
    else:
        assert data["pass"] and entry["seedFunctional"] and entry["marker"] and data["seedAudit"]["terminal"]
    attempts[folder] = (run, data, log)

assert raw(HERE / "run.json") == raw(HERE / "matrix-original/run.json")
matrix = attempts["matrix-original"][1]["seedAudit"]
assert matrix["seedCount"] == 8 and matrix["warmup"] == 64 and matrix["measured"] == 256
assert matrix["functionalPass"] and matrix["pairedEndpointsWithinInputPrecision"] and matrix["pairedEndpointsBitwiseEqual"]
assert matrix["maxPairedHitPositionError"] == matrix["maxPairedMissDirectionError"] == matrix["maxPairedNormalQuantizationSteps"] == 0
assert len(matrix["segments"]) == 32 and attempts["matrix-original"][1]["frames"] == 10274
assert sum(s["warmup"] + s["measured"] for s in matrix["segments"]) == 10240
references = {}
for item in matrix["referenceInputs"]:
    assert digest((ROOT / item["path"]).read_bytes()) == item["sha256"]
    for point in read(ROOT / item["path"])["denseReference"]["points"]:
        references[(point["x"], point["y"])] = point
assert sum(p["converged"] for p in matrix["referencePoints"]) == 15
for p in matrix["referencePoints"]:
    expected = references[(p["x"], p["y"])]; assert p["converged"] == expected["converged"]
    for key in ["referenceMean", "batchMeanStandardError", "primaryWorld", "normal", "albedo"]:
        assert struct.pack("<3f", *vector(p[key])) == struct.pack("<3f", *vector(expected[key]))

for mode_index, mode in enumerate(MODES):
    selected = [s for s in matrix["segments"] if s["mode"] == mode]
    assert [s["seed"] for s in selected] == SEEDS
    assert len({s["lightingSignature"] for s in selected}) == 1
    assert len({s["sceneRevision"] for s in selected}) == len({s["skyRevision"] for s in selected}) == 1
    for s in selected:
        assert s["temporal"] == s["actualTemporal"] == bool(mode_index & 1) and s["spatial"] == bool(mode_index & 2)
        assert s["firstShaderFrame"] == s["seed"] + 64 and s["lastShaderFrame"] == s["seed"] + 319
        assert s["lastApplicationFrame"] - s["firstApplicationFrame"] >= 255
        assert s["finishedEditorSeconds"] > s["startedEditorSeconds"]
        assert s["lightCount"] == 18 and s["instances"] == 15 and s["cutouts"] == 12 and s["environment"] and s["alphaVisibility"]
        assert 0 <= s["maxPrimaryError"] <= .0005
        assert s["full"]["pixels"] == 65536 and s["full"]["samples"] == 65536 * 256
        assert s["phaseZero"]["pixels"] == 4096 and s["phaseZero"]["samples"] == 4096 * 256
        assert s["phaseZero"]["phaseX"] == s["phaseZero"]["phaseY"] == 0
        assert len(s["frameMeans"]) == 256
        for signal in ["raw", "filtered"]:
            for c in "xyz":
                close(s["full"][signal][c], sum(f[signal][c] for f in s["frameMeans"]) / 256)
        for domain in [s["full"], s["phaseZero"]]:
            gate = all(abs(domain["filtered"][c] - domain["raw"][c]) < max(.000005, abs(domain["raw"][c]) * .05) for c in "xyz")
            assert domain["energyGatePass"] == gate
        for p in s["points"]:
            for signal in ["raw", "filtered"]:
                assert len(p[signal + "BlockMeans"]) == 8
                for c in "xyz":
                    close(p[signal][c], sum(v[c] for v in p[signal + "BlockMeans"]) / 8)
                    assert math.isfinite(p[signal + "Variance"][c]) and p[signal + "Variance"][c] >= 0
        for endpoint in ["first", "last"]:
            value = b"".join(struct.pack("<3fIII3ffII", *vector(v["position"]), v["normal"], v["diffuse"], v["specular"],
                                        *vector(v["emission"]), v["pdf"], v["geometric"], v["flags"]) for v in s[endpoint + "Secondary"])
            assert digest(value) == s[endpoint + "SecondaryHash"]
for seed in SEEDS:
    selected = [s for s in matrix["segments"] if s["seed"] == seed]
    for key in ["firstSecondaryHash", "lastSecondaryHash", "firstRisHash", "lastRisHash"]:
        assert len({s[key] for s in selected}) == 1

# The rejected byte-gate matrix is retained as a failure, not silently recolored.
rejected = read(HERE / "rejected-matrix-paired-hash/seed-progress.json")
assert len(rejected["segments"]) == 32 and not rejected["terminal"]
counts = {"position": 0, "normal": 0}; max_hit = max_miss = max_steps = 0
for seed in SEEDS:
    selected = [s for s in rejected["segments"] if s["seed"] == seed]; base = selected[0]
    for s in selected[1:]:
        for endpoint in ["first", "last"]:
            assert s[endpoint + "RisHash"] == base[endpoint + "RisHash"]
            for i, (a, b) in enumerate(zip(base[endpoint + "Secondary"], s[endpoint + "Secondary"])):
                for key in ["flags", "diffuse", "specular", "emission", "pdf", "geometric"]:
                    assert a[key] == b[key]
                distance = math.dist(vector(a["position"]), vector(b["position"]))
                counts["position"] += a["position"] != b["position"]
                counts["normal"] += a["normal"] != b["normal"]
                max_hit = max(max_hit, distance if a["flags"] == 1 else 0)
                max_miss = max(max_miss, distance if a["flags"] != 1 else 0)
                if a["flags"] == 1:
                    assert distance <= .0005  # Existing position bound, unchanged.
                else:
                    origin = vector(rejected["referencePoints"][i]["primaryWorld"])
                    da = [x - y for x, y in zip(vector(a["position"]), origin)]
                    db = [x - y for x, y in zip(vector(b["position"]), origin)]
                    ra = math.sqrt(sum(x*x for x in da)); rb = math.sqrt(sum(x*x for x in db))
                    assert abs(ra - 100000) <= 8 * 2**-23 * 100000 and abs(rb - 100000) <= 8 * 2**-23 * 100000
                    assert math.dist([x / ra for x in da], [x / rb for x in db]) <= 1e-6
                steps = max(abs((a["normal"] & 65535) - (b["normal"] & 65535)), abs((a["normal"] >> 16) - (b["normal"] >> 16)))
                max_steps = max(max_steps, steps)
assert counts == {"position": 504, "normal": 18} and max_steps == 1
close(max_hit, 1.2217680062389159e-5); close(max_miss, .0087890625)

review = read(HERE / "review.json")
assert review["diagnosticQualified"] and not review["qualityQualified"] and not review["productionRuntimeChanged"]
assert digest((HERE / "paired-contract-correction.patch").read_bytes()) == review["pairedContractCorrectionPatchSha256"]
analysis = read(HERE / "analysis.json")
assert analysis == summarize(matrix, digest(raw(HERE / "results/matrix.json")))
assert not analysis["qualityQualified"] and all(m["fullEnergyGatePassingSegments"] == 0 for m in analysis["modes"])
for source in attempts["matrix-original"][0]["sources"]:
    assert digest((ROOT / source["path"]).read_bytes()) == source["sha256"]
changed = subprocess.check_output(["git", "diff", "--name-only", review["baselineGitRevision"], "--", "Runtime/RTGI", "Runtime/RayTracing"], cwd=ROOT)
assert not changed.strip()
print("PASS: four actual processes retained, qualified 32-segment/8-seed matrix, numeric contract, old failures and full 5% failure preserved.")
