"""Pool precision comparison with unchanged units and unresolved full-domain gate."""
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
    reports = [read(HERE / "results" / (name + ".json")) for name in ["baseline", "rgba32"]]
    result = {"qualityQualified": False, "productionFormatChanged": False,
              "scope": "Only this GI instance's four RGBA16F pool textures promoted to RGBA32F. Scale64, original NRD/settings/epsilon and external formats retained. Full green still fails 5%; stride0 success alone is not full acceptance. Payload is not physical VRAM or performance.", "variants": []}
    for name, report in zip(["baseline", "rgba32"], reports):
        pool = report["poolPrecision"]; full = report["spatialAudit"]["full"]
        item = {"name": name, "reportSha256": sha(raw(HERE / "results" / (name + ".json"))),
                "originalStrideZeroGate": report["energyGatePass"], "fullGate": full["energyGatePass"],
                "full": full, "failingPhases": sum(not p["energyGatePass"] for p in report["spatialAudit"]["phases"]),
                "additionalPoolBytes": pool["poolBytes"] - pool["baselinePoolBytes"], "moments": []}
        for stage in report["moments"]:
            total = stage["total"]
            item["moments"].append({"pipeline": stage["pipeline"], "samples": total["samples"], "positiveMean": total["positiveMean"],
                                    "zeroMomentWithPositiveMean": total["zeroMomentWithPositiveMean"],
                                    "zeroPercent": total["zeroMomentWithPositiveMean"] * 100 / total["positiveMean"],
                                    "momentSaturated": total["momentSaturated"], "secondMomentSum": total["secondMomentSum"],
                                    "squaredMeanSum": total["squaredMeanSum"]})
        result["variants"].append(item)
    result["filteredVarianceRelativeChangePercent"] = (reports[1]["spatialAudit"]["full"]["filteredVariance"] / reports[0]["spatialAudit"]["full"]["filteredVariance"] - 1) * 100
    result["rawFrameMeansIdentical"] = all(a["raw"] == b["raw"] for a, b in zip(reports[0]["frameMeans"], reports[1]["frameMeans"]))
    result["additionalPoolPayloadProjection1080Bytes"] = 4 * 1920 * 1080 * 8
    return result


if __name__ == "__main__":
    data = summarize()
    (HERE / "analysis.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for item in data["variants"]:
        print(item["name"], "full RGB %", item["full"]["relativeErrorPercent"], "stride0/full", item["originalStrideZeroGate"], item["fullGate"],
              "moment zero %", [round(v["zeroPercent"], 6) for v in item["moments"]], "extra bytes", item["additionalPoolBytes"])
    print("Filtered variance change %", data["filteredVarianceRelativeChangePercent"], "raw frame means identical", data["rawFrameMeansIdentical"])
