"""Audit the bounded WTD Play regression; no Unity state is modified."""
import hashlib
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def read(name):
    return json.loads((HERE / name).read_text(encoding="utf-8-sig"))


review = read("review.json")
for path, key in (
    ("Runtime/RT/HoRTSkinnedMesh.cs", "runtimeSha256"),
    ("Editor/RT/HoRTSkinnedSceneValidation.cs", "validationSha256"),
):
    assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == review[key], path

before = read(review["before"])
assert before["playing"] and before["instances"] == 25
assert any("衣服2围裙" in reason for reason in before["rejections"])
assert any("衣服2袖子" in reason for reason in before["rejections"])

frames = []
for filename in review["after"]:
    result = read(filename)
    assert result["playing"] and result["viewValid"] and not result["error"], filename
    assert result["instances"] == result["skins"] == 27, filename
    assert not result["rejections"], filename
    assert len(result["poses"]) == 27, filename
    assert result["width"] == 960 and result["height"] == 540, filename
    for pose in result["poses"]:
        assert pose["vertices"] > 0 and pose["different"] == 0, pose
        assert pose["maxWorldDifference"] == 0, pose
    for name in ("衣服2围裙", "衣服2袖子"):
        pose = next(p for p in result["poses"] if p["renderer"].endswith(name))
        assert pose["mesh"].endswith("(Clone)"), pose
        skin = next(r for r in result["renderers"] if f"/{name} " in r)
        assert "weightedNull=0 " in skin and "bakedNonFinite=0 " in skin, skin
    frames.append(result["frame"])
assert frames[1] - frames[0] >= 120 and frames[0] >= 120, frames

cpu = read(review["cpu"])
assert cpu["pass"] and not cpu["error"]
assert len(cpu["checks"]) == review["observed"]["cpuChecks"] == 14
compilation = read(review["compile"])
assert {row["assembly"]: row["exitCode"] for row in compilation} == {"Runtime": 0, "Editor": 0}
assert review["temporaryDiagnosticRemoved"]
assert not (ROOT / "Editor/RTShadow/HoRTShadowPlayDiagnostic.cs").exists()
print(f"PASS: 14 CPU checks; WTD frames {frames}; 25 -> 27 casters; 27/27 poses match fresh Bake.")
