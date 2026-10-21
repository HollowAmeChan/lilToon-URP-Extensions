"""Verify the format experiment, precise captures and all rejected measurements."""
import gzip
import hashlib
import json
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
for digest, item in index.items():
    if "gitBlob" in item:
        assert subprocess.check_output(["git", "rev-parse", item["gitRevision"] + ":" + item["gitPath"]], cwd=ROOT).decode().strip() == item["gitBlob"]
        value = subprocess.check_output(["git", "cat-file", "blob", item["gitBlob"]], cwd=ROOT)
        if item["lineEndings"] == "lf": value = value.replace(b"\r\n", b"\n")
        if item["lineEndings"] == "crlf": value = value.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    else:
        value = (HERE / item["path"]).read_bytes()
    assert sha(value) == digest and len(value) == item["bytes"]
for digest in read(HERE / "source-files.json").values(): assert digest in index
logs = read(HERE / "log-index.json")
for item in logs.values(): assert sha((HERE / item["path"]).read_bytes()) == item["sha256"]
for name, item in read(HERE / "capture-archives.json").items():
    packed = (HERE / item["path"]).read_bytes(); value = gzip.decompress(packed)
    assert sha(packed) == item["archiveSha256"] and len(packed) == item["archiveBytes"]
    assert sha(value) == item["rawSha256"] and len(value) == item["rawBytes"] and value == raw(HERE / name)


def attempt(directory, label, report):
    run = read(HERE / directory / "run.json"); assert run["activeProcess"] is None and run["debugLayerRequested"]
    for source in run["sources"]:
        if source["path"].endswith(".dll"): assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
        else: assert source["sha256"] in index
    entry = next(e for e in run["entries"] if e["label"] == label)
    value = raw(HERE / report); assert sha(value) == entry["reportSha256"]
    log = (HERE / logs[entry["log"]]["path"]).read_text(encoding="utf-8-sig")
    assert entry["logSha256"] == logs[entry["log"]]["sha256"]
    assert entry["abortReason"] is None and entry["fresh"] and entry["sourceDrift"] == entry["debugErrors"] == 0
    return entry, json.loads(value.decode("utf-8-sig")), log


first, report, _ = attempt("rejected-before-prepare", "qualification", "rejected-before-prepare/report.json")
assert first["exitCode"] == 1 and not report["pass"] and "Pool format selector was not applied" in report["error"]
for label, exit_code in [("baseline", 1), ("rgba32", 0)]:
    entry, report, log = attempt("rejected-copy-uav", label, "rejected-copy-uav/" + label + ".json")
    assert entry["exitCode"] == exit_code and entry["shaderCsDiagnostics"] == 0  # Old counter missed the actual binding error.
    assert log.count("Attempting to bind texture as UAV but the texture wasn't created with the UAV usage flag set!") == 1
    assert all(m["total"]["positiveMean"] == 0 and m["total"]["samples"] == 1048576 for m in report["moments"])
    assert all(all(s["full"][c] == 0 for c in "xyz") for s in report["spatialAudit"]["stages"] if s["pipeline"] >= 0)

qualification, q, qlog = attempt("qualification-original", "qualification", "results/qualification.json")
assert qualification["exitCode"] == 0 and q["pass"] and qualification["marker"] and qualification["shaderCsDiagnostics"] == 0
assert q["poolPrecision"]["copyBitExact"] and q["poolPrecision"]["copiedComponents"] == 64 and q["poolPrecision"]["changedPools"] == 4
assert q["poolPrecision"]["poolBytes"] - q["poolPrecision"]["baselinePoolBytes"] == 131072
reports = []
for label, expected_exit in [("baseline", 1), ("rgba32", 0)]:
    entry, report, log = attempt("comparison-original", label, "results/" + label + ".json")
    assert entry["exitCode"] == expected_exit and entry["shaderCsDiagnostics"] == 0 and entry["marker"] and entry["precisionFunctional"] and entry["spatialComplete"]
    assert not re.search(r"Compute shader .*Property|Attempting to bind texture|UAV usage flag|Shader error|Shader warning|D3D12Fence::Wait", log, re.I)
    pool = report["poolPrecision"]
    assert pool["pass"] and pool["copyBitExact"] and pool["copiedComponents"] == 64 and pool["signalScale"] == 64
    assert pool["signalFormat"] == pool["outputFormat"] == "R16G16B16A16_SFloat"
    assert pool["changedPools"] == (4 if label == "rgba32" else 0)
    assert pool["poolBytes"] - pool["baselinePoolBytes"] == (2097152 if label == "rgba32" else 0)
    assert pool["baselinePoolBytes"] == 2818304
    for item in pool["pools"]:
        if item["nativeFormat"] == 27:
            assert item["format"] == ("R32G32B32A32_SFloat" if label == "rgba32" else "R16G16B16A16_SFloat")
            assert item["bytes"] == item["width"] * item["height"] * (16 if label == "rgba32" else 8)
    assert report["observedFrames"] == 256 and report["spatialAudit"]["complete"]
    assert report["spatialAudit"]["full"]["pixels"] == 65536 and report["spatialAudit"]["full"]["samples"] == 16777216
    assert not report["spatialAudit"]["full"]["energyGatePass"]
    assert [m["pipeline"] for m in report["moments"]] == [4, 6]
    for stage in report["moments"]:
        assert stage["signalScale"] == 64 and stage["total"]["samples"] == 1048576 and stage["total"]["positiveMean"] > 900000
        assert stage["total"]["momentSaturated"] == 0 and stage["total"]["secondMomentSum"] > 0
        assert sum(b["samples"] for b in stage["bins"]) == stage["total"]["samples"]
    assert any(b["pipeline"] == 4 and b["binding"] == "gOut_Diff" and b["resourceType"] == "TRANSIENT_POOL" and b["poolIndex"] == 0 for b in pool["bindings"])
    assert any(b["pipeline"] == 6 and b["binding"] == "gOut_Diff" and b["resourceType"] == "PERMANENT_POOL" and b["poolIndex"] == 0 for b in pool["bindings"])
    assert report["energyGatePass"] == (label == "rgba32") and report["pass"] == (label == "rgba32")
    reports.append(report)
assert [f["raw"] for f in reports[0]["frameMeans"]] == [f["raw"] for f in reports[1]["frameMeans"]]
assert all(m["total"]["zeroMomentWithPositiveMean"] / m["total"]["positiveMean"] > .98 for m in reports[0]["moments"])
assert all(m["total"]["zeroMomentWithPositiveMean"] == 256 for m in reports[1]["moments"])
assert read(HERE / "analysis.json") == summarize()
review = read(HERE / "review.json"); assert not review["productionFormatChanged"] and not review["qualityQualified"] and review["precisionCandidateAcceptedForFurtherValidation"]
assert sha(raw(HERE / "run.json")) == sha(raw(HERE / "comparison-original/run.json"))
core = (ROOT / "Runtime/RTDI/NRD/HoRTDINrdContext.cs").read_text(encoding="utf-8-sig")
assert "#if UNITY_EDITOR\n                if(ValidationPoolFormat!=null)" in core
assert (ROOT / "Runtime/RTGI/NRD/HoRTGINrdContext.cs").read_bytes() == subprocess.check_output(["git", "show", review["baselineGitRevision"] + ":Runtime/RTGI/NRD/HoRTGINrdContext.cs"], cwd=ROOT)
for source in read(HERE / "run.json")["sources"]:
    assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
print("PASS: six actual processes, rejected allocation/capture errors preserved, exact float32 copy, pool/moment gain and remaining full-domain failure verified; production unchanged.")
