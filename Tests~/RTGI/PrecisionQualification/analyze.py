"""Summarize final GI precision qualification and keep unresolved gates explicit."""
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
    quality = read(HERE / "results/final-quality.json")
    output = {"productionPrecisionRetained": True, "fullQualityQualified": False, "stableBudgetQualified": False,
              "scope": "GI-only four RGBA32F pools plus RGBA32F shared PrePass/final output; signal/normal/filtered remain original. Units64 and original thresholds retained. Full green and seven phases still fail; cost snapshots are not target-resolution or stable-budget proof.",
              "qualityReportSha256": sha(raw(HERE / "results/final-quality.json")),
              "full": quality["spatialAudit"]["full"], "failingPhases": sum(not p["energyGatePass"] for p in quality["spatialAudit"]["phases"]),
              "additionalPayloadBytes256": 5 * 256 * 256 * 8,
              "additionalPayloadProjection1080Bytes": 5 * 1920 * 1080 * 8,
              "rangeReports": [], "players": [], "gpu": []}
    for name in ["output32-rg", "final-range-compat"]:
        path = HERE / "results" / (name + ".json"); data = read(path)
        output["rangeReports"].append({"name": name, "sha256": sha(raw(path)), "frames": data["frames"],
                                      "ranges": data["ranges"], "checks": len(data["checks"]), "poolStates": data["poolStates"]})
    for mode in ["rg", "compat"]:
        path = HERE / "final-player-results" / mode / "report.json"; data = read(path)
        output["players"].append({"mode": mode, "sha256": sha(raw(path)), "requests": data["renderCallbacks"], "checks": len(data["checks"]),
                                  "historyChecks": len(data["historyChecks"]), "poolChecks": len(data["poolChecks"]),
                                  "spatialL1": data["spatialRelativeL1"], "varianceRatio": data["filteredVariance"] / data["rawVariance"]})
    for name in ["gpu-baseline", "gpu-rgba32", "final-gpu"]:
        data = read(HERE / "results" / (name + ".json"))
        output["gpu"].append({"name": name, "summary": [s for s in data["gpuSummary"] if s["name"] in ["GI.Total", "GI.NRD"]]})
    return output


if __name__ == "__main__":
    data = summarize()
    (HERE / "analysis.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Final full RGB error %", data["full"]["relativeErrorPercent"], "failed phases", data["failingPhases"])
    print("Final ranges", [(v["name"], [r["energyGatePass"] for r in v["ranges"]]) for v in data["rangeReports"]])
    print("Final Players", [(v["mode"], v["requests"], v["poolChecks"]) for v in data["players"]])
    print("Extra payload MiB 256 / projected1080", data["additionalPayloadBytes256"] / 1048576, data["additionalPayloadProjection1080Bytes"] / 1048576)
