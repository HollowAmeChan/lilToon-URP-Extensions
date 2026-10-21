"""Controlled history-policy comparison; preserves the original quality failures."""
import gzip
import hashlib
import json
import math
import statistics
from pathlib import Path

HERE = Path(__file__).parent
CHANNELS = "xyz"


def raw(path):
    return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(".json.gz").read_bytes())


def read(path):
    return json.loads(raw(path).decode("utf-8-sig"))


def sha(value):
    return hashlib.sha256(value).hexdigest().upper()


def estimate(values):
    return {"mean": statistics.mean(values), "seedMeanStandardError": statistics.stdev(values) / math.sqrt(len(values))}


def summarize(matrix, no_reuse, gpu):
    output = {"qualityQualified": False, "productionRetained": False,
              "scope": "Three TS policies, eight fixed paired seeds; empirical SE across segment means. Full no-reuse comparator is finite and shared-model; 15 selected reference points have post-selection bias. No full-image absolute truth, original 5% gate replacement, Player/dynamic/HD acceptance or global performance claim.",
              "variants": [], "gpu": []}
    baseline = matrix["variants"][0]["capture"]["segments"]
    mc = [s for s in no_reuse["segments"] if s["mode"] == "none"]
    for variant in matrix["variants"]:
        capture = variant["capture"]; segments = capture["segments"]
        result = {"policy": variant["policy"], "raw": [], "filtered": [], "rawMinusLegacy": [], "filteredMinusLegacy": [],
                  "selectedPoints": [], "rawTemporalVariance": statistics.mean(s["full"]["rawVariance"] for s in segments),
                  "filteredTemporalVariance": statistics.mean(s["full"]["filteredVariance"] for s in segments),
                  "originalFullGatePassingSegments": sum(s["full"]["energyGatePass"] for s in segments),
                  "originalPhaseZeroGatePassingSegments": sum(s["phaseZero"]["energyGatePass"] for s in segments)}
        for c in CHANNELS:
            for signal in ["raw", "filtered"]:
                item = estimate([s["full"][signal][c] for s in segments]); expected = statistics.mean(s["full"]["raw"][c] for s in mc)
                item["relativeDifferenceFromNoReusePercent"] = (item["mean"] / expected - 1) * 100
                result[signal].append(item)
                result[signal + "MinusLegacy"].append(estimate([s["full"][signal][c] - b["full"][signal][c] for s, b in zip(segments, baseline)]))
        raw_errors = []; filtered_errors = []
        for i, reference in enumerate(capture["referencePoints"]):
            point = {"x": reference["x"], "y": reference["y"], "referenceConverged": reference["converged"], "raw": [], "filtered": []}
            for c in CHANNELS:
                for signal, errors in [("raw", raw_errors), ("filtered", filtered_errors)]:
                    item = estimate([s["points"][i][signal][c] for s in segments]); expected = reference["referenceMean"][c]
                    item["referenceMean"] = expected; item["referenceSE"] = reference["batchMeanStandardError"][c]
                    item["relativeDifferencePercent"] = (item["mean"] / expected - 1) * 100 if expected > 1e-6 else None
                    if reference["converged"] and expected > 1e-6:
                        errors.append(abs(item["mean"] / expected - 1))
                    point[signal].append(item)
            result["selectedPoints"].append(point)
        result["selectedConvergedComponentCount"] = len(raw_errors)
        result["selectedRawMeanAbsoluteRelativeError"] = statistics.mean(raw_errors)
        result["selectedFilteredMeanAbsoluteRelativeError"] = statistics.mean(filtered_errors)
        output["variants"].append(result)
    for variant in gpu["gpuVariants"]:
        output["gpu"].append({"policy": variant["policy"], "summary": variant["summary"]})
    return output


if __name__ == "__main__":
    matrix_path = HERE / "results/matrix.json"; no_reuse_path = HERE.parent / "Seeds/results/matrix.json"; gpu_path = HERE / "results/gpu-cost.json"
    matrix = read(matrix_path)["feedbackAudit"]; no_reuse = read(no_reuse_path)["seedAudit"]; gpu = read(gpu_path)["feedbackAudit"]
    result = summarize(matrix, no_reuse, gpu)
    result["inputs"] = {"matrixSha256": sha(raw(matrix_path)), "noReuseSourceSha256": sha(raw(no_reuse_path)), "gpuReportSha256": sha(raw(gpu_path))}
    (HERE / "analysis.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for item in result["variants"]:
        print(item["policy"], "filtered/no-reuse RGB %", [round(v["relativeDifferenceFromNoReusePercent"], 3) for v in item["filtered"]],
              "selected raw/filtered relative error", round(item["selectedRawMeanAbsoluteRelativeError"], 5), round(item["selectedFilteredMeanAbsoluteRelativeError"], 5),
              "old full 5%", item["originalFullGatePassingSegments"], "/8")
