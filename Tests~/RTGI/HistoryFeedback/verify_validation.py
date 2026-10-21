"""Verify isolated history candidates, quality evidence and actual GPU intervals."""
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
POLICIES = ["legacy", "packed-spatial", "packed-temporal"]


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def sha(value):
    return hashlib.sha256(value).hexdigest().upper()


def vector(value):
    return [value[c] for c in "xyz"]


def packet(value):
    return struct.pack("<3fIIfII", *vector(value["position"]), value["normal"], value["misc"], value["weight"], value["radiance"], value["unused"])


index = read(HERE / "source-index.json")
for digest, item in index.items():
    value = (HERE / item["path"]).read_bytes()
    assert sha(value) == digest and len(value) == item["bytes"]
for digest in read(HERE / "source-files.json").values():
    assert digest in index
logs = read(HERE / "log-index.json")
for item in logs.values():
    assert sha((HERE / item["path"]).read_bytes()) == item["sha256"]
for name, item in read(HERE / "capture-archives.json").items():
    packed = (HERE / item["path"]).read_bytes(); value = gzip.decompress(packed)
    assert sha(packed) == item["archiveSha256"] and len(packed) == item["archiveBytes"]
    assert sha(value) == item["rawSha256"] and len(value) == item["rawBytes"] and raw(HERE / name) == value

entries = {}
for directory in ["qualification-original", "matrix-original", "gpu-original"]:
    run = read(HERE / directory / "run.json")
    assert run["activeProcess"] is None and run["debugLayerRequested"]
    for source in run["sources"]:
        if source["path"].endswith(".dll"):
            assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
        else:
            assert source["sha256"] in index
    for entry in run["entries"]:
        assert entry["exitCode"] == 0 and entry["abortReason"] is None and entry["pass"] and entry["feedbackFunctional"]
        assert entry["marker"] and entry["fresh"] and entry["debugErrors"] == entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0
        report = HERE / "results" / (entry["label"] + ".json")
        assert sha(raw(report)) == entry["reportSha256"]
        log = (HERE / logs[entry["log"]]["path"]).read_text(encoding="utf-8-sig")
        assert not re.search(r"d3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|error CS|887a0006", log, re.I)
        entries[entry["label"]] = read(report)
assert len(entries) == 4 and raw(HERE / "run.json") == raw(HERE / "gpu-original/run.json")

for label, count, frames in [("qualification", 8, 803), ("matrix", 8, 7715)]:
    data = entries[label]; report = data["feedbackAudit"]
    assert data["frames"] == frames and report["functionalPass"] and report["restoredOriginal"] and report["restoredOriginalBound"]
    assert not report["qualityQualified"]
    assert [v["policy"] for v in report["variants"]] == POLICIES
    reference = report["variants"][0]["capture"]["referencePoints"]
    assert sum(p["converged"] for p in reference) == 15
    for variant in report["variants"]:
        capture = variant["capture"]; segments = capture["segments"]
        assert capture["functionalPass"] and capture["terminal"] and len(segments) == count
        assert capture["pairedEndpointsWithinInputPrecision"]
        for s in segments:
            assert (s["lightCount"], s["instances"], s["cutouts"]) == (18, 15, 12) and s["environment"] and s["alphaVisibility"]
            assert s["maxPrimaryError"] <= .0005 and s["actualTemporal"] == s["temporal"]
            assert s["firstShaderFrame"] == s["seed"] + s["warmup"]
            assert s["lastShaderFrame"] == s["firstShaderFrame"] + s["measured"] - 1
            assert s["lastApplicationFrame"] - s["firstApplicationFrame"] >= s["measured"] - 1
            assert s["full"]["pixels"] == 65536 and s["full"]["samples"] == 65536 * s["measured"]
            assert s["phaseZero"]["pixels"] == 4096 and s["phaseZero"]["phaseX"] == s["phaseZero"]["phaseY"] == 0
            for signal in ["raw", "filtered"]:
                for c in "xyz":
                    assert math.isclose(s["full"][signal][c], sum(f[signal][c] for f in s["frameMeans"]) / s["measured"], rel_tol=2e-6, abs_tol=1e-10)
            for domain in [s["full"], s["phaseZero"]]:
                gate = all(abs(domain["filtered"][c] - domain["raw"][c]) < max(.000005, abs(domain["raw"][c]) * .05) for c in "xyz")
                assert domain["energyGatePass"] == gate
        if label == "matrix":
            assert [s["seed"] for s in segments] == SEEDS and all(s["mode"] == "temporal-spatial" for s in segments)
            assert all(s["measured"] == 256 and s["warmup"] == 64 for s in segments)
    base = report["variants"][0]["capture"]["segments"]
    for variant in report["variants"][1:]:
        for a, b in zip(base, variant["capture"]["segments"]):
            assert (a["seed"], a["mode"]) == (b["seed"], b["mode"])
            for key in ["firstSecondaryHash", "lastSecondaryHash", "firstRisHash", "lastRisHash"]:
                assert a[key] == b[key]
            if label == "qualification" and a["mode"] != "temporal-spatial":
                assert a["frameMeans"] == b["frameMeans"] and a["points"] == b["points"]
    assert len(report["historyChecks"]) == 48
    assert sum(c["pixels"] for c in report["historyChecks"]) == 3145728
    for check in report["historyChecks"]:
        expected = 2 if check["policy"] == "packed-temporal" and check["mode"] in ["spatial", "temporal-spatial"] else 1
        assert check["sourceBuffer"] == expected and check["packetMismatches"] == check["radianceMismatches"] == check["normalMismatches"] == 0
        assert all(packet(a) == packet(b) for a, b in zip(check["selectedHistory"], check["selectedSource"]))

# Both packed-spatial and legacy are exactly identical in the observed domain.
matrix = entries["matrix"]["feedbackAudit"]
legacy, packed, temporal = [v["capture"]["segments"] for v in matrix["variants"]]
for a, b in zip(legacy, packed):
    assert a["full"] == b["full"] and a["phaseZero"] == b["phaseZero"] and a["frameMeans"] == b["frameMeans"] and a["points"] == b["points"]
old = read(HERE.parent / "Seeds/results/matrix.json")["seedAudit"]
for a, b in zip(legacy, [s for s in old["segments"] if s["mode"] == "temporal-spatial"]):
    assert a["full"] == b["full"] and a["frameMeans"] == b["frameMeans"]
assert all(not s["full"]["energyGatePass"] for v in matrix["variants"] for s in v["capture"]["segments"])

clock = entries["gpu-clock"]
assert clock["nativeResultSize"] == 48 and len(clock["samples"]) == 24
assert clock["longMedian"] > 2 * clock["shortMedian"] and clock["longMedian"] > 3 * clock["emptyMedian"]
for s in clock["samples"]:
    assert s["frequency"] > 0 and s["begin"] > 0 and s["completedFence"] >= s["targetFence"] > 0 and s["end"] >= s["begin"]
    if s["iterations"]: assert s["end"] > s["begin"]
gpu = entries["gpu-cost"]["feedbackAudit"]
assert gpu["gpuOnly"] and gpu["functionalPass"] and gpu["restoredOriginal"] and gpu["restoredOriginalBound"]
slots = set()
for variant in gpu["gpuVariants"]:
    assert len(variant["samples"]) == 480 and len(variant["summary"]) == 12
    for s in variant["samples"]:
        assert s["slot"] not in slots; slots.add(s["slot"])
        assert s["frequency"] > 0 and s["begin"] > 0 and s["completedFence"] >= s["targetFence"] > 0 and s["end"] >= s["begin"]
        assert math.isclose(s["milliseconds"], (s["end"] - s["begin"]) * 1000 / s["frequency"], rel_tol=1e-12, abs_tol=1e-12)
        if s["name"] in ["GI.Total", "GI.Final", "GI.NRD", "GI.Composite"]: assert s["end"] > s["begin"]
    for summary in variant["summary"]:
        values = sorted(s["milliseconds"] for s in variant["samples"] if s["name"] == summary["name"])
        assert len(values) == summary["count"] == 40
        assert summary["median"] == values[20] and summary["p95"] == values[38]

for directory in ["qualification-original", "matrix-original", "gpu-original"]:
    generated = HERE / directory / "generated"
    normalized = (generated / "OriginalNormalized.compute").read_text(encoding="utf-8-sig")
    store = "RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,0);"
    assert normalized.count(store) == 1
    for name, source in [("PackedSpatial.compute", "1"), ("PackedTemporal.compute", "(_HoRTGIFlags.y>.5?2:1)")]:
        candidate = (generated / name).read_text(encoding="utf-8-sig")
        replacement = "_HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0)]=_HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel," + source + ")];"
        assert candidate == normalized.replace(store, replacement)
        for data in [entries["qualification"], entries["matrix"], entries["gpu-cost"]]:
            item = next(s for s in data["feedbackAudit"]["sources"] if Path(s["path"]).name == name)
            assert sha((generated / name).read_bytes()) == item["sha256"] and len(item["kernels"]) == 8
for name, item in read(HERE / "image-artifacts.json").items():
    value = (HERE / name).read_bytes(); assert sha(value) == item["sha256"] and len(value) == item["bytes"]
    if name.endswith(".png"):
        assert value[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", value[16:24]) == (256, 256)
analysis = read(HERE / "analysis.json")
expected = summarize(matrix, old, gpu); expected["inputs"] = {
    "matrixSha256": sha(raw(HERE / "results/matrix.json")), "noReuseSourceSha256": sha(raw(HERE.parent / "Seeds/results/matrix.json")), "gpuReportSha256": sha(raw(HERE / "results/gpu-cost.json"))}
assert analysis == expected and not analysis["qualityQualified"] and not analysis["productionRetained"]
review = read(HERE / "review.json")
assert review["candidateAcceptedForIntegration"] and not review["productionRuntimeChanged"] and not review["qualityQualified"]
assert sha((HERE / "packed-temporal.patch").read_bytes()) == review["candidatePatchSha256"]
assert not subprocess.check_output(["git", "diff", "--name-only", review["baselineGitRevision"], "--", "Runtime/RTGI", "Runtime/RayTracing"], cwd=ROOT).strip()
for source in read(HERE / "gpu-original/run.json")["sources"]:
    assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
print("PASS: four actual processes, three policies, exact packed-history/domain controls, 8-seed gain, completed GPU intervals and original 5% failure preserved; production pending.")
