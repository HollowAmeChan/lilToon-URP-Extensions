"""Copy the selected local H2Cu stage packages into independent Unity Source folders.

Source packages remain read-only. Existing destinations are reused only when their
SHA256 matches; conflicts fail instead of overwriting artist edits. Archives,
intermediate Blender files and foreign Unity metadata are never copied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import shutil
from datetime import datetime, timezone


STAGES = {
    "花吐电気": "花吐电気·H2CU式stage06_by_H2Cu阿相_699357166830bf6a4741aaace9e6819e",
    "乐园·深命": "乐园·深命·H2CU式stage05-1_2b76b0a6146d395903183661ce4c6263",
    "圣诞万花镜": "圣诞万花镜·H2CU式stage08-2_by_H2Cu阿相_fa2f43c5748e4a3cb2fc6f937a664d7a",
    "茉莉庭院": "茉莉庭院·H2CU式stage19_by_H2Cu阿相_bb5b5485e16dc9b93dfaafa507ed9c3f",
    "六角花台绣球": "六角花台绣球_9e95a9483648a39257868f4eaebcc532",
    "琉璃万花镜": "琉璃万花镜·H2CU式stage08-1_by_H2Cu阿相_950ad78cd2370fe87921c741211bb74a",
    "古典机械剧场": "古典机械剧场·H2CU式stage15_by_H2Cu阿相_5efec0784e395a9a8548f7f2e2f5f3d6",
    "拟花十字之棺": "拟花十字之棺·H2CU式stage11_by_H2Cu阿相_eeb3c020e3ffecf270f093295624a1c4",
    "百鸟溯归": "百鸟溯归·H2CU式stage04 (3)_by_H2Cu阿相_9f266086ee892d4fed947b7e89a844d9",
    "孔雀月令红墨": "孔雀月令红墨·H2CU式stage17-3_by_H2Cu阿相_4b4e7a1073a0e3e3567084a0f524ef16",
    "乐园·墟实": "乐园·墟实·H2CU式stage05-3_9c2b4a708b000488e529b8160d078c2e",
    "秘月之泉": "秘月之泉·H2CU式stage14_by_H2Cu阿相_3a0c097d121339b5e418630a658642e1",
    "万象天文": "万象天文·H2CU式stage02_7a233a3ad3a4660d33ef10df346599fb",
    "炫光夜厅": "炫光夜厅·H2CU式stage16_by_H2Cu阿相_e47df0fcf57f65e49769e87df6c657a5",
    "夜月蓝": "夜月蓝·H2CU式stage13_by_H2Cu阿相_44b17a1cd084b24d5bacf9703f64198d (1)",
    "芋泥咩咩": "芋泥咩咩·H2CU式stage18_by_H2Cu阿相_5c3d09622be14b59490b7d97c0468255",
    "月昔梅": "月昔梅·H2CU式stage20_by_H2Cu阿相_5acc23fda20c8d79429730c53bbf33ee",
    "古典机械剧场黑蓝白": "古典机械剧场改色版黑蓝白·H2CU式stage15_by_H2Cu阿相_08c525632fad769cb39160305abfa9c5",
    "冷色古堂": "H2CU式stage09改色-冷色古堂_by_H2Cu阿相_65b030403a0f1099aaa683cda626ddd4",
    "孔雀月令": "孔雀月令·H2CU式stage17_by_H2Cu阿相_942d46105519b49b4312a5598fc50a1e",
    "孔雀月令青瓷": "孔雀月令青瓷·H2CU式stage17-2_by_H2Cu阿相_42a50a48445db9f753e7f938e92df32a",
    "丝苇楼梯": "丝苇楼梯 H2CU式stage21_by_H2Cu阿相_a74d1e8b306ad491384ae2d5fe000117",
    "月极蝶夜礼": "月极蝶夜礼1-H2CU式stage07-2 2_by_H2Cu阿相_a9861e02af0ed58453b713b58426bb76",
    "虫无阶梯": "虫无阶梯·H2CU式stage10-1_by_H2Cu阿相_ae71b3207466144f6a676c53c7949c10",
    "虫染阶梯": "虫染阶梯·H2CU式stage10-3_by_H2Cu阿相_0fcf040439bde557efa3b0cf6d50280c",
    "乐园·葡萄柚": "H2CU式stage05-2改色 -葡萄柚_by_H2Cu阿相_b17d55a87b0fedca74962481299eaf40",
    "乐园·西瓜霜": "H2CU式stage05-2改色 -西瓜霜_by_H2Cu阿相_d6d1731e2cfe91d3797aad9471313daf",
}
ALLOWED = {
    ".pmx", ".pmd", ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".exr", ".hdr",
    ".fx", ".fxsub", ".emd", ".emm", ".x", ".vmd", ".vpd", ".txt", ".md", ".pdf",
}
SUPPLEMENTAL = {
    "月昔梅（完整场景）": (
        "月昔梅·H2CU式stage20_by_H2Cu阿相_32903cd061fc7937dd3f529c539d094c",
        "月昔梅/Source/完整场景",
    ),
}


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def run(source_root: pathlib.Path, target_root: pathlib.Path, evidence: pathlib.Path) -> dict:
    source_root = source_root.resolve()
    target_root = target_root.resolve()
    if source_root == target_root or source_root in target_root.parents or target_root in source_root.parents:
        raise ValueError("Source and destination must be separate directory trees")
    report = {"timestampUtc": datetime.now(timezone.utc).isoformat(), "sourceRoot": str(source_root),
              "targetRoot": str(target_root), "stages": [], "copiedFiles": 0, "reusedFiles": 0}
    packages = [(name, directory, target_root / name / "Source") for name, directory in STAGES.items()]
    packages += [(name, value[0], target_root / value[1]) for name, value in SUPPLEMENTAL.items()]
    for name, directory, destination in packages:
        source = source_root / directory
        files = sorted(path for path in source.rglob("*") if path.is_file())
        if not files:
            raise ValueError(f"Empty or missing source package: {source}")
        if any(path.is_symlink() for path in source.rglob("*")):
            raise ValueError(f"Source package contains links: {source}")
        entry = {"name": name, "source": str(source), "destination": str(destination), "files": [], "skipped": []}
        for path in files:
            relative = path.relative_to(source)
            if path.suffix.lower() not in ALLOWED:
                entry["skipped"].append(str(relative))
                continue
            target = destination / relative
            before = path.stat()
            digest = sha256(path)
            if target.exists():
                if not target.is_file() or sha256(target) != digest:
                    raise ValueError(f"Destination conflict; preserve existing file: {target}")
                report["reusedFiles"] += 1
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                report["copiedFiles"] += 1
            if sha256(target) != digest:
                raise ValueError(f"Copy mismatch: {target}")
            after = path.stat()
            unchanged = (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns)
            if not unchanged:
                raise ValueError(f"Source changed during read: {path}")
            entry["files"].append({"relativePath": str(relative), "bytes": before.st_size,
                                   "sha256": digest, "sourceStatUnchanged": unchanged})
        entry["bytes"] = sum(item["bytes"] for item in entry["files"])
        report["stages"].append(entry)
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=pathlib.Path,
                        default=pathlib.Path(r"E:\BaiduSyncdisk\大师\_完整场景\H2Cu阿相"))
    parser.add_argument("--target-root", type=pathlib.Path,
                        default=pathlib.Path(r"D:\Unity_Project\BREAK_URP\Assets\00动画\MMD场景\H2Cu阿相"))
    parser.add_argument("--evidence", type=pathlib.Path,
                        default=pathlib.Path(__file__).resolve().parents[2] / "Temp~" / "MmdStages" / "source-copy.json")
    args = parser.parse_args()
    report = run(args.source_root, args.target_root, args.evidence.resolve())
    print(json.dumps({"stages": len(report["stages"]), "copied": report["copiedFiles"],
                      "reused": report["reusedFiles"], "bytes": sum(item["bytes"] for item in report["stages"]),
                      "evidence": str(args.evidence.resolve())}, ensure_ascii=False))


if __name__ == "__main__":
    main()
