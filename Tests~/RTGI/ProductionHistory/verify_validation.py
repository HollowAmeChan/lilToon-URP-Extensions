"""Verify the production change, real regressions and deliberately unresolved gates."""
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


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def sha(value):
    return hashlib.sha256(value).hexdigest().upper()


index = read(HERE / "source-index.json")
objects = sorted({v["gitBlob"] for v in index.values() if "gitBlob" in v})
data = subprocess.check_output(["git", "cat-file", "--batch"], input=("\n".join(objects) + "\n").encode(), cwd=ROOT)
at = 0; git_objects = {}
for oid in objects:
    end = data.index(b"\n", at); size = int(data[at:end].split()[2]); at = end + 1
    git_objects[oid] = data[at:at + size]; at += size + 1
trees = {}
for revision in {v["gitRevision"] for v in index.values() if "gitRevision" in v}:
    tree = {}
    for entry in subprocess.check_output(["git", "ls-tree", "-r", "-z", revision], cwd=ROOT).split(b"\0"):
        if entry:
            meta, name = entry.split(b"\t", 1); tree[name.decode()] = meta.split()[2].decode()
    trees[revision] = tree
for digest, item in index.items():
    if "gitBlob" in item:
        assert trees[item["gitRevision"]][item["gitPath"]] == item["gitBlob"]
        value = git_objects[item["gitBlob"]]
        if item["lineEndings"] == "lf": value = value.replace(b"\r\n", b"\n")
        if item["lineEndings"] == "crlf": value = value.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    else:
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


def run(name):
    value = read(HERE / name)
    assert value["activeProcess"] is None and value["debugLayerRequested"]
    for source in value["sources"]:
        if source["path"].endswith(".dll"):
            assert sha((ROOT / source["path"]).read_bytes()) == source["sha256"]
        else:
            assert source["sha256"] in index
    return value


def log(entry, key="log"):
    assert logs[entry[key]]["sha256"] == entry[key + "Sha256"]
    return (HERE / logs[entry[key]]["path"]).read_text(encoding="utf-8-sig")


def scratch_messages(text):
    return re.findall(r"(?im)^d3d12 validation error.*ScratchAccelerationStructureData.*$", text)


editor = run("run.json")
for entry in editor["entries"]:
    assert entry["exitCode"] == 0 and entry["pass"] and entry["productionFunctional"] and entry["fresh"] and entry["marker"]
    assert entry["debugErrors"] == entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0 and entry["abortReason"] is None
    value = raw(HERE / "results" / (entry["label"] + ".json")); assert sha(value) == entry["reportSha256"]
    report = json.loads(value.decode("utf-8-sig")); assert report["frames"] == 226 and report["instances"] == 15 and report["cutouts"] == 12
    history = report["productionHistory"]; assert history["pass"] and history["productionShaderBound"] and history["restoredSettings"]
    assert len(history["checks"]) == 8
    for check in history["checks"]:
        h = check["history"]; assert h["pixels"] == 65536 and h["packetMismatches"] == 0
        if "spatial" in check["mode"]:
            assert h["sourceBuffer"] == 2 and check["spatialTemporalDifferentPackets"] > 0
        else:
            assert h["sourceBuffer"] == 1
    assert not scratch_messages(log(entry))

rejected = run("rejected-old-spatial-reader/run.json")
assert len(rejected["entries"]) == 7
for entry in rejected["entries"]:
    assert entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0
    if entry["label"].startswith("core-"):
        assert entry["exitCode"] == 1 and not entry["pass"] and entry["debugErrors"] == 0
        assert "SDK spatial-only stage did not combine neighbors" in log(entry)
    else:
        assert entry["exitCode"] == 0 and entry["pass"] and entry["debugErrors"] == 0 and entry["fresh"] and entry["marker"]
        report = HERE / "results" / (entry["label"] + ".json"); assert sha(raw(report)) == entry["reportSha256"] and read(report)["pass"]
corrected = run("core-corrected-run.json"); control = run("legacy-control-run.json")
for entry in corrected["entries"] + control["entries"]:
    assert entry["exitCode"] == 0 and entry["pass"] and entry["marker"] and entry["debugErrors"] == 2
    assert not entry["reportExpected"] and entry["fresh"] is None and entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0
    messages = scratch_messages(log(entry)); assert len(messages) == 2
    for message in messages:
        address = re.search(r"range \[(0x[0-9a-f]+), (0x[0-9a-f]+)\]", message, re.I)
        assert address and int(address[2], 16) - int(address[1], 16) + 1 == 2304
assert "[HoGIHistoryLegacy] BOUND frozen legacy Final" in log(control["entries"][0])

quality_run = run("quality-run.json"); entry = quality_run["entries"][0]
assert entry["exitCode"] == 1 and not entry["pass"] and not entry["energyGatePass"] and entry["originalGateFailure"] and entry["productionFunctional"]
assert entry["fresh"] and entry["marker"] and entry["debugErrors"] == entry["shaderCsDiagnostics"] == entry["sourceDrift"] == 0
quality = read(HERE / "results/production-quality.json"); assert sha(raw(HERE / "results/production-quality.json")) == entry["reportSha256"]
assert quality["observedFrames"] == 256 and quality["spatialAudit"]["complete"] and not quality["spatialAudit"]["full"]["energyGatePass"]
assert "Castle filtered RGB time-mean failed original 5% energy gate" in quality["error"]
assert all(not phase["energyGatePass"] for phase in quality["spatialAudit"]["phases"])

warnings = read(HERE / "build-warning-review.json")
assert len(warnings["knownWarningLines"]) == 5 and not any("RTGI" in v for v in warnings["knownWarningLines"])
for item in warnings["oldBuildLogs"]:
    assert logs[item["path"]]["sha256"] == item["sha256"]
    text = (HERE / logs[item["path"]]["path"]).read_text(encoding="utf-8-sig")
    assert sorted(s for s in text.splitlines() if s.startswith("Shader warning")) == sorted(warnings["knownWarningLines"])
build_rejected = run("rejected-build-warnings/run.json"); old_build = build_rejected["entries"][0]
assert old_build["buildExit"] == 0 and old_build["buildMarker"] and old_build["buildDebugErrors"] == 0 and old_build["buildDiagnostics"] == 5
players = run("player-run.json")
expected_samples = [4, 52, 179, 180, 239, 240, 244, 267, 269, 270, 271] + list(range(303, 315))
for entry in players["entries"]:
    assert entry["buildExit"] == entry["playerExit"] == 0 and entry["pass"] and entry["historyProbe"] and entry["baseVertexProbe"]
    assert entry["buildAbort"] is None and entry["playerAbort"] is None and entry["probeDllAbsent"]
    assert entry["buildDebugErrors"] == entry["debugErrors"] == entry["diagnostics"] == entry["sourceDrift"] == 0
    assert entry["fresh"] and entry["marker"] and entry["buildMarker"] and not entry["buildUnhandledDiagnostics"]
    assert sorted(entry["buildKnownWarnings"]) == sorted(warnings["knownWarningLines"])
    assert "[HoGIPlayerBuild] PASS" in log(entry, "buildLog") and "[HoGIRuntime] END pass=True" in log(entry)
    if entry["mode"] == "rg":
        assert entry["reusedBuild"] and entry["buildOriginalRunSha256"] == sha(raw(HERE / entry["buildOriginalRun"]))
        assert entry["buildLogSha256"] == old_build["buildLogSha256"]
    report_path = HERE / "player-results" / (entry["mode"] + ".json"); assert sha(raw(report_path)) == entry["reportSha256"]
    report = read(report_path)
    assert report["pass"] and report["historyProbe"] and report["baseVertexProbe"] and report["skinnedProbe"] and report["cutoutProbe"]
    assert report["api"] == "Direct3D12" and report["colorSpace"] == "Linear" and report["compatibility"] == (entry["mode"] == "compat")
    assert report["renderCallbacks"] == 315 and len(report["checks"]) == 38 and len(report["frames"]) == 310
    assert [f["sample"] for f in report["frames"]] == [v for v in range(4, 315) if v != 268]
    assert all(b["applicationFrame"] > a["applicationFrame"] and b["time"] > a["time"] for a, b in zip(report["frames"], report["frames"][1:]))
    assert report["spatialRelativeL1"] < .15 and report["filteredVariance"] < report["rawVariance"] * .4
    assert report["reprojectedCameraFrames"] >= 55 and report["movingGeometryResetFrames"] == 24
    assert [h["sample"] for h in report["historyChecks"]] == expected_samples
    assert sum(h["pixels"] for h in report["historyChecks"]) == 94208
    for check in report["historyChecks"]:
        assert check["historySource"] == 2 and check["packetMismatches"] == 0 and check["computeShader"] == "HoRTGI"
        if check["sample"] == 52: assert check["temporalUsed"] and check["spatialTemporalDifferentPackets"] > 0
    for check in report["historyChecks"]:
        if check["sample"] in [240, 244, 267, 269, 271, 304, 305, 307, 309, 310, 311, 312, 313, 314]: assert check["nrdReset"]
    payload = read(HERE / "player-results" / (entry["mode"] + "-payload.json")); output = Path(entry["exe"]).parent
    assert not any(Path(p["path"]).name.lower() == "hogigpuprobe.dll" for p in payload)
    assert any(p["path"].endswith("Assembly-CSharp.dll") for p in payload)
    for item in payload:
        value = (output / item["path"]).read_bytes(); assert sha(value) == item["sha256"] and len(value) == item["bytes"]

review = read(HERE / "review.json")
assert review["productionHistoryRetained"] and not review["qualityQualified"] and not review["coreDebugQualified"] and not review["newHistoryRegressionsObserved"]
shader = (ROOT / review["productionShaderSource"]).read_bytes(); assert sha(shader) == review["productionShaderSha256"]
baseline = subprocess.check_output(["git", "show", review["baselineGitRevision"] + ":" + review["productionShaderSource"]], cwd=ROOT).decode()
old = "    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,0);"
replacement = """    // Keep temporal history independent of the spatially resampled pool.
    // Final executes after both stages, so no temporal reader races this write.
    if(_HoRTGIFlags.y>.5)
        _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0)]=
            _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,2)];
    else
        RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,0);"""
assert shader.decode().replace("\r\n", "\n") == baseline.replace("\r\n", "\n").replace(old, replacement)
changed = subprocess.check_output(["git", "diff", "--name-only", review["baselineGitRevision"], "--", "Runtime"], cwd=ROOT).decode().splitlines()
assert changed == [review["productionShaderSource"]]
fixture = (ROOT / "Editor/RTGI/HoRTGIValidation.cs").read_text(encoding="utf-8-sig")
assert 'spatialOnly[feature.LastContext.arrayPitch+spatialAddress].misc&255)>1' in fixture
for name, item in read(HERE / "image-artifacts.json").items():
    value = (HERE / name).read_bytes(); assert sha(value) == item["sha256"] and len(value) == item["bytes"]
assert read(HERE / "analysis.json") == summarize()
print("PASS: production history RG/compat, existing regressions and two Release Players; old reader failures, exact known warnings, baseline AS debug errors and original quality failure retained.")
