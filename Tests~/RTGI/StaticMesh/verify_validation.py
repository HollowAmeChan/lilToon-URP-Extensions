"""Verify a rejected static-mesh candidate and bounded native AS reproduction."""
import hashlib
import json
import re
import subprocess
from pathlib import Path

HERE = Path(__file__).parent
ROOT = HERE.parents[2]


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


sources = read(HERE / "source-index.json")
for digest, item in sources.items():
    path = HERE / item["path"]
    assert sha(path) == digest and path.stat().st_size == item["bytes"]
for digest in read(HERE / "source-files.json").values():
    assert digest in sources
logs = read(HERE / "log-index.json")
for item in logs.values():
    assert sha(HERE / item["path"]) == item["sha256"]
reports = {sha(p): p for p in HERE.rglob("*.json")}
attempts = {}
for path in HERE.rglob("run.json"):
    run = read(path)
    assert run["activeProcess"] is None and run["debugLayerRequested"]
    for source in run["sources"]:
        if source["path"].endswith(".dll"):
            assert sha(ROOT / source["path"]) == source["sha256"]
        else:
            assert source["sha256"] in sources
    for entry in run["entries"]:
        assert entry["abortReason"] is None and entry["exitCode"] == 0
        assert entry["fresh"] and entry["marker"] and entry["shaderCsDiagnostics"] == 0
        assert entry["logSha256"] == logs[entry["log"]]["sha256"]
        log = (HERE / logs[entry["log"]]["path"]).read_text(encoding="utf-8-sig")
        diagnostics = re.findall(r"(?im)^d3d12(?: validation)? (?:error|corruption).*", log)
        assert len(diagnostics) == entry["debugErrors"]
        for message in diagnostics:
            assert "ScratchAccelerationStructureData" in message
            address = re.search(r"range \[(0x[0-9a-f]+), (0x[0-9a-f]+)\]", message, re.I)
            expected_bytes = 2432 if path.parent.name == "rejected-fastbuild" else 2560
            assert address and int(address[2], 16) - int(address[1], 16) + 1 == expected_bytes
        report = read(reports[entry["reportSha256"]])
        assert report["pass"] is True  # API/functional completion, not debug qualification.
        attempts[(path.parent.name, entry["label"])] = (entry, report)

initial, initial_report = attempts[(HERE.name, "native-initial-five")]
growth, growth_report = attempts[(HERE.name, "native-growth")]
assert initial["debugErrors"] == 0 and growth["debugErrors"] == 1
assert initial_report["initialInstances"] == initial_report["finalInstances"] == 5
assert initial_report["builds"] == 1 and not initial_report["growth"]
assert growth_report["initialInstances"] == 3 and growth_report["finalInstances"] == 5
assert growth_report["builds"] == 2 and growth_report["growth"]
assert growth_report["unity"] == "6000.3.15f1"
assert "4060" in growth_report["device"]
for folder in ["rejected-scratch", "rejected-shared-scratch", "rejected-immutable-mode",
               "growth-controls-original", "rejected-as-recreate", "deferred-growth-original",
               "rejected-fastbuild", "rejected-minmemory", "rejected-initial-update"]:
    entries = [e for (f, _), (e, _) in attempts.items() if f == folder]
    assert entries and all(e["debugErrors"] == 1 for e in entries)
for folder in ["control-original", "initial-five-original"]:
    assert all(e["debugErrors"] == 0 for (f, _), (e, _) in attempts.items() if f == folder)

review = read(HERE / "review.json")
assert not review["staticCanonicalProductionRetained"] and not review["staticBaseVertexQualified"]
assert review["nativeUnityApiReproducesScratch"] and not review["scratchRootCauseProven"]
assert review["scratchRangeBytes"] == 2560 and review["goalStatus"] == "active"
for name, digest in review["restoredSourceHashes"].items():
    assert sha(ROOT / name) == digest
    baseline = subprocess.check_output(["git", "show", review["restoredToGitRevision"] + ":" + name], cwd=ROOT)
    assert (ROOT / name).read_bytes().replace(b"\r\n", b"\n") == baseline.replace(b"\r\n", b"\n")
assert sha(HERE / "rejected-candidate.patch") == review["candidatePatchSha256"]
for name in ["Runtime/RayTracing/HoRTStaticMesh.cs", "Editor/RayTracing/HoRTStaticSceneValidation.cs",
             "Editor/RayTracing/HoRTStaticMeshCopyProbe.cs"]:
    assert not (ROOT / name).exists()
probe = (ROOT / "Editor/RayTracing/HoRTAccelerationGrowthProbe.cs").read_text(encoding="utf-8")
assert "HoRTScene" not in probe[probe.index("public static class"):]
runner = (ROOT / "Tests~/RTGI/StaticMesh/run_validation.ps1").read_text(encoding="utf-8")
assert "$record.debugErrors -ne 0" in runner and "if($rejected){throw" in runner
print(f"PASS: {len(attempts)} actual processes, native 0/1 scratch contrast; candidate rejected, production restored.")
