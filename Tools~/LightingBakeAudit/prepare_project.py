"""Copy authored inputs into an independent Unity lighting experiment. No hard links."""
import hashlib
import json
import pathlib
import shutil
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")
REPO = pathlib.Path(__file__).resolve().parents[2]
SOURCE = pathlib.Path(r"D:\Unity_Project\BREAK_URP")
ROOT = REPO / ".codex-research/LightingBakeAudit-20261102"
OUT = REPO / "Temp~/LightingBakeAudit-20261102"


def ignored(directory, names):
    return [name for name in names if name.startswith(".") or name.endswith("~")
            or name.lower() in {"temp", "library", "obj", "logs", "node_modules", "research", "docs"}]


def copy_file(relative):
    source = SOURCE / relative
    if not source.is_file():
        return
    target = ROOT / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    meta = pathlib.Path(str(source) + ".meta")
    if meta.is_file():
        shutil.copy2(meta, pathlib.Path(str(target) + ".meta"))
    # Preserve parent folder GUIDs where present.
    parent = pathlib.Path(relative).parent
    while str(parent) not in (".", "Assets"):
        meta = SOURCE / (str(parent) + ".meta")
        if meta.is_file():
            shutil.copy2(meta, ROOT / (str(parent) + ".meta"))
        parent = parent.parent


def main():
    if (ROOT / "Library/SourceAssetDB").exists():
        raise SystemExit("Research project already imported; do not overwrite its baked assets.")
    manifest = json.loads((OUT / "source-dependencies.json").read_text(encoding="utf-8-sig"))
    ROOT.mkdir(parents=True, exist_ok=True)
    paths = set(p for p in manifest["assets"] if p.startswith("Assets/"))
    stages = ["花吐电気", "圣诞万花镜", "孔雀月令"]
    for stage in stages:
        folder = SOURCE / f"Assets/00动画/MMD场景/H2Cu阿相/{stage}/Source"
        for source in folder.rglob("*"):
            if source.is_file() and source.suffix != ".meta":
                paths.add(source.relative_to(SOURCE).as_posix())
    total = sum((SOURCE / p).stat().st_size for p in paths if (SOURCE / p).is_file())
    print(f"Copy {len(paths)} authored assets, {total / 1024**2:.1f} MiB", flush=True)
    for path in sorted(paths):
        copy_file(path)
    shutil.copytree(SOURCE / "ProjectSettings", ROOT / "ProjectSettings", dirs_exist_ok=True)
    (ROOT / "Packages").mkdir(exist_ok=True)
    packages = json.loads((SOURCE / "Packages/manifest.json").read_text(encoding="utf-8-sig"))
    snapshots = []
    for name, value in list(packages["dependencies"].items()):
        if not value.startswith("file:"):
            continue
        source = pathlib.Path(value.removeprefix("file:"))
        if not source.is_absolute():
            source = (SOURCE / "Packages" / source).resolve()
        target = ROOT / "LocalPackages" / name
        print("Snapshot", name, flush=True)
        shutil.copytree(source, target, ignore=ignored, dirs_exist_ok=True)
        packages["dependencies"][name] = "file:" + target.as_posix()
        snapshots.append({"name": name, "source": str(source), "snapshot": str(target)})
    # Embedded VRM packages are required by the authored character.
    for source in (SOURCE / "Packages").iterdir():
        if source.is_dir() and (source / "package.json").is_file():
            shutil.copytree(source, ROOT / "Packages" / source.name, ignore=ignored, dirs_exist_ok=True)
    (ROOT / "Packages/manifest.json").write_text(json.dumps(packages, ensure_ascii=False, indent=2), encoding="utf-8")
    shutil.copy2(SOURCE / "Packages/packages-lock.json", ROOT / "Packages/packages-lock.json")
    controls = [x["path"] for x in manifest["scenes"]] + [
        "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset",
        "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset"]
    hashes = {p: hashlib.sha256((SOURCE / p).read_bytes()).hexdigest() for p in controls}
    report = {"source": str(SOURCE), "research": str(ROOT), "assets": len(paths),
              "sourceBytes": total, "snapshots": snapshots, "originalControls": hashes}
    (OUT / "prepared-project.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Research project ready:", ROOT, flush=True)


if __name__ == "__main__":
    main()
