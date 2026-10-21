"""Summarize the fixed seed matrix without turning it into a quality acceptance gate."""
import hashlib
import gzip
import json
import math
import statistics
from pathlib import Path

HERE = Path(__file__).parent
CHANNELS = "xyz"
MODES = ["none", "temporal", "spatial", "temporal-spatial"]


def vector(value):
    return [value[c] for c in CHANNELS]


def estimate(values):
    return {"mean": statistics.mean(values), "seedMeanStandardError": statistics.stdev(values) / math.sqrt(len(values)),
            "minSeedMean": min(values), "maxSeedMean": max(values)}


def summarize(data, report_hash):
    assert data["terminal"] and data["functionalPass"] and data["seedCount"] == 8
    assert len(data["segments"]) == 32 and data["warmup"] == 64 and data["measured"] == 256
    references = data["referencePoints"]
    segments = data["segments"]
    output = {"reportSha256": report_hash, "qualityQualified": False,
              "scope": "Eight predeclared frame-seed blocks per mode. SE is estimated across block means, not across correlated frames or pixels. Same-model selected-point reference has selection bias and one unresolved control. No formal confidence/independence guarantee, no full-image absolute truth or original 5% gate replacement.",
              "modes": [], "pairedFullImageComparisons": []}
    baseline = sorted((s for s in segments if s["mode"] == "none"), key=lambda s: s["seed"])
    for mode in MODES:
        selected = sorted((s for s in segments if s["mode"] == mode), key=lambda s: s["seed"])
        summary = {"mode": mode, "fullRaw": [], "fullFiltered": [], "filteredMinusRaw": [],
                   "phaseZeroRaw": [], "phaseZeroFiltered": [], "points": [],
                   "meanRawTemporalVariance": statistics.mean(s["full"]["rawVariance"] for s in selected),
                   "meanFilteredTemporalVariance": statistics.mean(s["full"]["filteredVariance"] for s in selected),
                   "fullEnergyGatePassingSegments": sum(s["full"]["energyGatePass"] for s in selected),
                   "phaseZeroEnergyGatePassingSegments": sum(s["phaseZero"]["energyGatePass"] for s in selected)}
        paired = {"mode": mode, "rawMinusNoReuse": [], "filteredMinusNoReuse": []}
        for c in CHANNELS:
            a = [s["full"]["raw"][c] for s in selected]
            b = [s["full"]["filtered"][c] for s in selected]
            summary["fullRaw"].append(estimate(a)); summary["fullFiltered"].append(estimate(b))
            summary["filteredMinusRaw"].append(estimate([y - x for x, y in zip(a, b)]))
            summary["phaseZeroRaw"].append(estimate([s["phaseZero"]["raw"][c] for s in selected]))
            summary["phaseZeroFiltered"].append(estimate([s["phaseZero"]["filtered"][c] for s in selected]))
            assert [s["seed"] for s in selected] == [s["seed"] for s in baseline]
            paired["rawMinusNoReuse"].append(estimate([s["full"]["raw"][c] - ref["full"]["raw"][c] for s, ref in zip(selected, baseline)]))
            paired["filteredMinusNoReuse"].append(estimate([s["full"]["filtered"][c] - ref["full"]["raw"][c] for s, ref in zip(selected, baseline)]))
        for i, reference in enumerate(references):
            point = {"x": reference["x"], "y": reference["y"], "referenceConverged": reference["converged"],
                     "referenceMean": vector(reference["referenceMean"]), "referenceSE": vector(reference["batchMeanStandardError"]),
                     "raw": [], "filtered": []}
            for signal in ["raw", "filtered"]:
                for c in CHANNELS:
                    values = [s["points"][i][signal][c] for s in selected]
                    item = estimate(values); expected = reference["referenceMean"][c]
                    item["relativeDifferencePercent"] = (item["mean"] / expected - 1) * 100 if abs(expected) > 1e-8 else None
                    item["absoluteDifference"] = item["mean"] - expected
                    item["quadratureSE"] = math.hypot(item["seedMeanStandardError"], reference["batchMeanStandardError"][c])
                    point[signal].append(item)
            summary["points"].append(point)
        output["modes"].append(summary); output["pairedFullImageComparisons"].append(paired)
    return output


if __name__ == "__main__":
    path = HERE / "results/matrix.json"
    raw = path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())
    data = json.loads(raw.decode("utf-8-sig"))["seedAudit"]
    analysis = summarize(data, hashlib.sha256(raw).hexdigest().upper())
    (HERE / "analysis.json").write_text(json.dumps(analysis, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    for mode in analysis["modes"]:
        print(mode["mode"], "full RGB raw", [round(x["mean"], 9) for x in mode["fullRaw"]],
              "filtered", [round(x["mean"], 9) for x in mode["fullFiltered"]],
              "full 5% segments", mode["fullEnergyGatePassingSegments"], "/8")
