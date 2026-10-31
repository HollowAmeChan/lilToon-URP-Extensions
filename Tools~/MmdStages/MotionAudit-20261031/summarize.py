"""Summarize fresh motion audit evidence without modifying Unity assets."""
import argparse
import datetime
import hashlib
import json
import math
import pathlib


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(r"D:\Unity_Project\BREAK_URP"))
    parser.add_argument("--tools", type=pathlib.Path, default=pathlib.Path(r"D:\Unity_Fork\HoUnityTools"))
    parser.add_argument("--output", type=pathlib.Path, default=pathlib.Path(__file__).with_name("summary.json"))
    args = parser.parse_args()
    work = args.project / "Library/HoMmdMotionAudit"
    before = load(work / "dense-before.json")
    after = load(work / "final.json")
    production = load(work / "production-regressions.json")
    pole = load(args.project / "Library/HoMmdStages/ik-pole-validation.json")
    result = {
        "created": datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
        "scope": "Motion/retarget/camera/audio evidence; runtime cloth and final beauty rendering are separate.",
        "loadedScenesPreserved": before["loadedScenesPreserved"] and after["loadedScenesPreserved"],
        "hingeBefore": before["solverProbes"],
        "hingeAfter": after["solverProbes"],
        "regressions": {
            "hingeCases": len(after["solverProbes"]),
            "hingeMaxResidualMetres": max(p["residual"] for p in after["solverProbes"]),
            "poleCases": len(pole["cases"]),
            "poleFailures": pole["failures"],
            "production": production["passed"],
        },
        "films": [],
        "nemonemo8to10seconds": {},
    }
    old = {f["scene"]: f for f in before["films"]}
    for film in after["films"]:
        if film["error"]:
            raise RuntimeError(film["scene"] + ": " + film["error"])
        original = old[film["scene"]]
        result["films"].append({
            "scene": film["scene"],
            "sampledFrames": film["sampledFrames"],
            "beforeSeekPositionMetres": original["maxSeekPositionError"],
            "afterSeekPositionMetres": film["maxSeekPositionError"],
            "beforeSeekAngleDegrees": original["maxSeekRotationError"],
            "afterSeekAngleDegrees": film["maxSeekRotationError"],
            "sourceReachResidualMetres": film["maxSourceReachResidual"],
            "sourceReachFrame": film["sourceReachFrame"],
            "maxKneeDifferenceDegrees": film["maxKneeDifference"],
            "kneeDifferenceFrame": film["kneeDifferenceFrame"],
            "footRestAxisDifferenceDegrees": film["maxFootAxisRestDifference"],
            "missingNonzeroMorphs": film["missingNonzeroMorphTracks"],
            "missingZeroMorphs": film["missingZeroMorphTracks"],
            "durationSeconds": film["duration"],
            "audioSeconds": film["audioDuration"],
        })
        if "NEMONEMO" in film["scene"]:
            for side in ["左足ＩＫ", "右足ＩＫ"]:
                rows = [r for r in film["feet"] if r["name"] == side and 240 <= r["frame"] <= 300]
                previous = [r for r in original["feet"] if r["name"] == side and 240 <= r["frame"] <= 300]
                a = max(previous, key=lambda r: r["sourceResidual"])
                b = max(rows, key=lambda r: r["sourceResidual"])
                result["nemonemo8to10seconds"][side] = {
                    "beforeWorstResidualMetres": a["sourceResidual"], "beforeWorstFrame": a["frame"],
                    "afterWorstResidualMetres": b["sourceResidual"], "afterWorstFrame": b["frame"],
                    "samplesPerSide": len(rows),
                }
    sour_unity = load(work / "unity-sour-nemonemo.json")
    sour_blender = load(work / "sour-nemonemo.json")
    errors = {}
    for unity, blender in zip(sour_unity["frames"], sour_blender["frames"], strict=True):
        if unity["vmdFrame"] != blender["vmdFrame"]:
            raise RuntimeError("Sour comparison frame mismatch")
        positions = {b["name"]: [b["position"][a] for a in "xyz"] for b in unity["bones"]}
        for name, position in blender["bones"].items():
            distance = math.dist(positions[name], position)
            if distance > errors.get(name, {}).get("maxDistanceMetres", -1):
                errors[name] = {"maxDistanceMetres": distance, "frame": unity["vmdFrame"]}
    result["sourComparison"] = {
        "blender": sour_blender["blender"], "mmdTools": sour_blender["mmdTools"],
        "frames": len(sour_unity["frames"]), "physics": False,
        "status": "Not identical; remaining importer/IK differences require a separate focused comparison.",
        "bones": errors,
    }
    artifacts = [work / n for n in [
        "dense-before.json", "hinge-fixed.json", "pose-reset.json", "final.json",
        "sour-nemonemo.json", "unity-sour-nemonemo.json", "production-regressions.json",
    ]]
    sources = [args.tools / p for p in [
        "Runtime/RigConstraints/HoIK/HoIkConstraint.cs",
        "Runtime/MmdMotion/HoVmdHumanoidRetargeter.cs",
        "Tests~/MmdMotion/VmdRetargetAudit.cs",
        "Tests~/MmdMotion/blender_sour_foot_audit.py",
    ]]
    result["sha256"] = {
        str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in artifacts + sources
    }
    if not result["loadedScenesPreserved"] or pole["failures"]:
        raise RuntimeError("Scene preservation or pole regression failed")
    if result["regressions"]["hingeMaxResidualMetres"] > .0002:
        raise RuntimeError("Single-axis hinge regression exceeded 0.2 mm")
    if any(f["afterSeekPositionMetres"] > .0001 or f["afterSeekAngleDegrees"] > .1 for f in result["films"]):
        raise RuntimeError("Reference sampling is still history dependent")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(args.output)


if __name__ == "__main__":
    main()
