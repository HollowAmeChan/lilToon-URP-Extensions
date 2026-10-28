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
}
ALLOWED = {
    ".pmx", ".pmd", ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".exr", ".hdr",
    ".fx", ".fxsub", ".emd", ".emm", ".x", ".vmd", ".vpd", ".txt", ".md", ".pdf",
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
    for name, directory in STAGES.items():
        source = source_root / directory
        destination = target_root / name / "Source"
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
                        default=pathlib.Path(r"D:\Unity_Project\BREAK_URP\Assets\mmd场景测试\H2Cu阿相"))
    parser.add_argument("--evidence", type=pathlib.Path,
                        default=pathlib.Path(__file__).resolve().parents[2] / "Temp~" / "MmdStages" / "source-copy.json")
    args = parser.parse_args()
    report = run(args.source_root, args.target_root, args.evidence.resolve())
    print(json.dumps({"stages": len(report["stages"]), "copied": report["copiedFiles"],
                      "reused": report["reusedFiles"], "bytes": sum(item["bytes"] for item in report["stages"]),
                      "evidence": str(args.evidence.resolve())}, ensure_ascii=False))


if __name__ == "__main__":
    main()
