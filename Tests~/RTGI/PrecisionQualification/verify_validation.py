"""Verify final GI precision, original failures, real Players and bounded cost claims."""
import gzip
import hashlib
import json
import math
import re
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True
from analyze import summarize

HERE = Path(__file__).parent
ROOT = HERE.parents[2]


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def sha(value):
    return hashlib.sha256(value).hexdigest().upper()


index = read(HERE / "source-index.json")
objects = sorted({v["gitBlob"] for v in index.values() if "gitBlob" in v})
data = subprocess.check_output(["git", "cat-file", "--batch"], input=("\n".join(objects) + "\n").encode(), cwd=ROOT)
at = 0; blobs = {}
for oid in objects:
    end = data.index(b"\n", at); size = int(data[at:end].split()[2]); at = end + 1; blobs[oid] = data[at:at + size]; at += size + 1
trees = {}
for revision in {v["gitRevision"] for v in index.values() if "gitRevision" in v}:
    tree = {}
    for entry in subprocess.check_output(["git", "ls-tree", "-r", "-z", revision], cwd=ROOT).split(b"\0"):
        if entry:
            meta, name = entry.split(b"\t", 1); tree[name.decode()] = meta.split()[2].decode()
    trees[revision] = tree
for digest, item in index.items():
    if "gitBlob" in item:
        assert trees[item["gitRevision"]][item["gitPath"]] == item["gitBlob"]; value = blobs[item["gitBlob"]]
        if item["lineEndings"] == "lf": value = value.replace(b"\r\n", b"\n")
        if item["lineEndings"] == "crlf": value = value.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    else: value = (HERE / item["path"]).read_bytes()
    assert sha(value) == digest and len(value) == item["bytes"]
for digest in read(HERE / "source-files.json").values(): assert digest in index
logs = read(HERE / "log-index.json")
for item in logs.values(): assert sha((HERE / item["path"]).read_bytes()) == item["sha256"]
for name, item in read(HERE / "capture-archives.json").items():
    packed = (HERE / item["path"]).read_bytes(); value = gzip.decompress(packed)
    assert sha(packed) == item["archiveSha256"] and len(packed) == item["archiveBytes"]
    assert sha(value) == item["rawSha256"] and len(value) == item["rawBytes"] and value == raw(HERE / name)


def run(name):
    r = read(HERE / name); assert r["activeProcess"] is None and r["debugLayerRequested"]
    for source in r["sources"]:
        if source["path"].endswith(".dll"): assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
        else: assert source["sha256"] in index
    return r


def log(entry, key="log"):
    assert logs[entry[key]]["sha256"] == entry[key + "Sha256"]
    return (HERE / logs[entry[key]]["path"]).read_text(encoding="utf-8-sig")


def editor_entry(runfile, label, reportpath, exitcode):
    r = run(runfile); e = next(v for v in r["entries"] if v["label"] == label)
    value = raw(HERE / reportpath); assert sha(value) == e["reportSha256"]
    assert e["exitCode"] == exitcode and e["abortReason"] is None and e["fresh"]
    assert e["debugErrors"] == e["shaderCsDiagnostics"] == e["sourceDrift"] == 0
    assert not re.search(r"d3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Compute shader .*Property|Shader error|Shader warning", log(e), re.I)
    return e, json.loads(value.decode("utf-8-sig"))


for label in ["range-rg", "range-compat"]:
    _, d = editor_entry("rejected-emission-alpha/run.json", label, "rejected-emission-alpha/" + label + ".json", 1)
    assert not d["pass"] and "Precision raw emissive integral: 0.0001" in d["failure"]
    _, d = editor_entry("low-signal-failure/run.json", label, "low-signal-failure/" + label + ".json", 1)
    assert not d["pass"] and "Precision filtered HDR/low-signal mean: 0.0001" in d["failure"] and len(d["ranges"]) == 1
for label in ["range-rg", "range-compat"]:
    _, d = editor_entry("pool-only/run.json", label, "pool-only/results/" + label + ".json", 1)
    assert not d["pass"] and "Precision 5% energy failed for amplitudes: 0.0001" in d["failure"]
    assert [r["energyGatePass"] for r in d["ranges"]] == [False, True, True]
    assert d["ranges"][2]["slowMomentMax"] > 65504 and d["poolStates"][-1]["width"] == 32
_, control = editor_entry("pool-only/run.json", "low-control", "pool-only/results/low-control.json", 0)
assert control["pass"] and not control["ranges"][0]["energyGatePass"] and control["poolStates"][0]["rgba16Count"] == 4

for runfile, label in [("output32-run.json", "output32-rg"), ("final-run.json", "final-range-compat")]:
    _, d = editor_entry(runfile, label, "results/" + label + ".json", 0)
    assert d["pass"] and d["precision"] and len(d["ranges"]) == 3
    assert all(r["energyGatePass"] and r["firstFrameReset"] and r["slowMomentMean"] > 0 for r in d["ranges"])
    assert d["ranges"][2]["slowMomentMax"] > 65504 and math.isfinite(d["ranges"][2]["slowMomentMax"])
    for r in d["ranges"]:
        for c in "rgb":
            assert abs(r["filtered"][c] - r["raw"][c]) <= abs(r["raw"][c]) * .05 + r["amplitude"] * .0001
    assert any(s["label"] == "resize96" and s["width"] == 96 and s["rgba32Count"] == 4 for s in d["poolStates"])
    assert any(s["label"] == "re-enabled" and s["rgba32Count"] == 4 for s in d["poolStates"])
    assert any(s["label"] == "default constructor control" and s["rgba16Count"] == 4 and s["rgba32Count"] == 0 for s in d["poolStates"])
_, quality = editor_entry("final-run.json", "final-quality", "results/final-quality.json", 0)
assert quality["pass"] and quality["energyGatePass"] and quality["observedFrames"] == 256
assert quality["poolPrecision"]["mode"] == "production" and quality["poolPrecision"]["outputFormat"] == "R32G32B32A32_SFloat"
assert not quality["spatialAudit"]["full"]["energyGatePass"] and sum(not p["energyGatePass"] for p in quality["spatialAudit"]["phases"]) == 7

for runfile, labels in [("gpu-run.json", ["gpu-clock", "gpu-baseline", "gpu-rgba32"]), ("final-gpu-run.json", ["final-gpu"])]:
    for label in labels:
        _, d = editor_entry(runfile, label, "results/" + label + ".json", 0)
        if label == "gpu-clock":
            assert d["pass"] and len(d["samples"]) == 24 and d["longMedian"] > 2 * d["shortMedian"]
            continue
        assert d["gpuQualified"] and d["pass"]
        for s in d["gpuSamples"]:
            assert s["frequency"] > 0 and s["begin"] > 0 and s["end"] >= s["begin"] and s["completedFence"] >= s["targetFence"] > 0
            assert math.isclose(s["milliseconds"], (s["end"] - s["begin"]) * 1000 / s["frequency"], rel_tol=1e-12, abs_tol=1e-12)
        for summary in d["gpuSummary"]:
            values = sorted(s["milliseconds"] for s in d["gpuSamples"] if s["name"] == summary["name"])
            assert len(values) == summary["count"] == 40 and summary["median"] == values[20] and summary["p95"] == values[38]

known = read(HERE / "build-warning-review.json")["knownWarningLines"]
for runfile, folder, final in [("player-run.json", "player-results", False), ("final-player-run.json", "final-player-results", True)]:
    for e in run(runfile)["entries"]:
        assert e["buildExit"] == e["playerExit"] == 0 and e["pass"] and e["precisionProbe"] and e["historyProbe"] and e["probeDllAbsent"]
        assert e["buildDebugErrors"] == e["debugErrors"] == e["diagnostics"] == e["sourceDrift"] == 0 and not e["buildUnhandledDiagnostics"]
        assert sorted(e["buildKnownWarnings"]) == sorted(known) and "[HoGIRuntime] END pass=True" in log(e)
        path = HERE / folder / e["mode"] / "report.json"; assert sha(raw(path)) == e["reportSha256"]; d = read(path)
        assert d["pass"] and d["precisionProbe"] and d["renderCallbacks"] == 315 and len(d["checks"]) == 39
        assert len(d["historyChecks"]) == len(d["poolChecks"]) == 23 and all(h["packetMismatches"] == 0 for h in d["historyChecks"])
        assert d["spatialRelativeL1"] < .15 and d["filteredVariance"] < d["rawVariance"] * .4 and d["movingGeometryResetFrames"] == 24
        for state in d["poolChecks"]:
            if state["nrdPresent"]:
                assert state["highPrecisionPolicy"] and state["rgba32"] == 4 and state["rgba16"] == 0 and state["colorPoolBytes"] == 262144
                if final: assert state["signalFormat"] == "R16G16B16A16_SFloat" and state["outputFormat"] == "R32G32B32A32_SFloat"
            else: assert state["sample"] == 270 and state["releasedPriorPools"]
        output = Path(e["exe"]).parent; payload = read(HERE / folder / (e["mode"] + "-payload.json"))
        assert not any(Path(i["path"]).name.lower() == "hogigpuprobe.dll" for i in payload)
        for i in payload:
            value = (output / i["path"]).read_bytes(); assert sha(value) == i["sha256"] and len(value) == i["bytes"]
for name, item in read(HERE / "image-artifacts.json").items():
    value = (HERE / name).read_bytes(); assert sha(value) == item["sha256"] and len(value) == item["bytes"]
review = read(HERE / "review.json")
assert review["productionPrecisionRetained"] and not review["fullQualityQualified"] and not review["stableTargetBudgetQualified"]
assert read(HERE / "analysis.json") == summarize()
core = (ROOT / "Runtime/RTDI/NRD/HoRTDINrdContext.cs").read_text(encoding="utf-8-sig")
gi = (ROOT / "Runtime/RTGI/NRD/HoRTGINrdContext.cs").read_text(encoding="utf-8-sig")
assert "bool highPrecisionColorPool=false" in core and "ValidationPoolFormat(name,i,nativeFormat)" in core
assert "highPrecisionColorPool:true" in gi and "internal const float SignalScale=64" in gi
assert "desc.graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat;" in gi
for source in run("final-player-run.json")["sources"]:
    assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
print("PASS: 22 actual process logs, alpha/low-signal failures preserved, final HDR/lifecycle/Players qualified; full green, seven phases and stable target budget remain open.")
