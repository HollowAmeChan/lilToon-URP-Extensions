"""Report production gains and unresolved gates without relabeling failures."""
import gzip
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).parent


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def sha(value):
    return hashlib.sha256(value).hexdigest().upper()


def summarize():
    quality_path = HERE / "results/production-quality.json"
    quality = read(quality_path)
    baseline_path = HERE.parent / "Seeds/analysis.json"
    baseline = read(baseline_path)["modes"][0]
    full = quality["spatialAudit"]["full"]
    output = {"productionHistoryRetained": True, "qualityQualified": False, "coreDebugQualified": False,
              "scope": "Actual production shader, RG/compat contracts and two 315-request Release Players. Old filtered/raw 5% failure and baseline-reproduced native AS growth diagnostics stay open. Finite no-reuse comparator is not independent full-image truth.",
              "inputs": {"productionQualitySha256": sha(raw(quality_path)), "noReuseAnalysisSha256": sha(raw(baseline_path))},
              "productionFull": full,
              "filteredRelativeToNoReusePercent": [(full["filtered"][c] / baseline["fullRaw"][i]["mean"] - 1) * 100 for i, c in enumerate("xyz")],
              "rawRelativeToNoReusePercent": [(full["raw"][c] / baseline["fullRaw"][i]["mean"] - 1) * 100 for i, c in enumerate("xyz")],
              "players": []}
    for mode in ["rg", "compat"]:
        path = HERE / "player-results" / (mode + ".json"); data = read(path)
        output["players"].append({"mode": mode, "reportSha256": sha(raw(path)), "requests": data["renderCallbacks"],
                                  "checks": len(data["checks"]), "historyChecks": len(data["historyChecks"]),
                                  "historyPixels": sum(c["pixels"] for c in data["historyChecks"]),
                                  "historyMismatches": sum(c["packetMismatches"] for c in data["historyChecks"]),
                                  "stationarySpatialL1": data["spatialRelativeL1"], "filteredToRawVariance": data["filteredVariance"] / data["rawVariance"],
                                  "reprojectedCameraFrames": data["reprojectedCameraFrames"], "changedGeometryResetFrames": data["movingGeometryResetFrames"]})
    return output


if __name__ == "__main__":
    data = summarize()
    (HERE / "analysis.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Production filtered/no-reuse RGB %:", [round(v, 3) for v in data["filteredRelativeToNoReusePercent"]])
    print("Original full filtered/raw RGB %:", data["productionFull"]["relativeErrorPercent"])
    print("Players:", [(v["mode"], v["requests"], v["historyChecks"], v["historyMismatches"]) for v in data["players"]])
    print("Quality/core debug qualified:", data["qualityQualified"], data["coreDebugQualified"])
