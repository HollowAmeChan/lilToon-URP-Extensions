"""Copy a scene dependency closure into an existing isolated Unity test project.

Reads the source Assets tree; does not run Unity, copy scripts/plugins, or alter packages.
Default is a dry run. Local glTF buffer/image URIs and importer material remaps are included.
"""
import argparse
from collections import Counter, deque
import hashlib
import json
from pathlib import Path
import re
import shutil
from urllib.parse import unquote, urlparse


GUID = re.compile(r"guid: ([0-9a-f]{32})")
TEXT = {".unity", ".prefab", ".mat", ".asset", ".controller", ".anim", ".shader", ".hlsl", ".cginc", ".gltf", ".json"}
CODE = {".cs", ".dll", ".exe", ".asmdef", ".asmref", ".so", ".dylib"}


def confined(path, root):
    path = path.resolve()
    if not path.is_relative_to(root):
        raise ValueError(f"Path outside intended tree: {path}")
    return path


def snapshot(source, scene, destination, copy):
    source = source.resolve()
    assets = (source / "Assets").resolve()
    destination = destination.resolve()
    if ".codex-research" not in destination.parts or source == destination:
        raise ValueError("Destination must be a different isolated .codex-research project")
    if (destination / "Assets").resolve().is_relative_to(assets):
        raise ValueError("Destination Assets must not be inside source Assets")
    index = {}
    for meta in assets.rglob("*.meta"):
        match = re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="replace"), re.M)
        if match:
            index[match[1]] = Path(str(meta)[:-5])
    queue = deque([confined(source / scene, assets)])
    found, external, excluded = set(), set(), set()
    while queue:
        path = confined(queue.popleft(), assets)
        if path in found:
            continue
        if not path.is_file():
            raise FileNotFoundError(path)
        if path.suffix.lower() in CODE:
            excluded.add(str(path.relative_to(source)))
            continue
        found.add(path)
        content = path.read_text(encoding="utf-8", errors="replace") if path.suffix.lower() in TEXT else ""
        meta = Path(str(path) + ".meta")
        if meta.is_file():
            content += "\n" + meta.read_text(encoding="utf-8", errors="replace")
        for guid in set(GUID.findall(content)):
            target = index.get(guid)
            if target is not None and target.is_file():
                queue.append(target)
            elif target is None and not guid.startswith("0000000000000000"):
                external.add(guid)  # Normally a package GUID; the test manifest provides it.
        if path.suffix.lower() == ".gltf":
            gltf = json.loads(path.read_text(encoding="utf-8-sig"))
            for kind in ("buffers", "images"):
                for entry in gltf.get(kind, []):
                    uri = entry.get("uri", "")
                    if not uri or uri.startswith("data:"):
                        continue
                    parsed = urlparse(uri)
                    if parsed.scheme or parsed.netloc:
                        raise ValueError(f"External glTF URI needs explicit preparation: {uri}")
                    queue.append(confined(path.parent / unquote(parsed.path), assets))
    report = {"source": str(source), "scene": str(scene), "assetCount": len(found),
              "bytes": sum(p.stat().st_size for p in found), "types": dict(Counter(p.suffix for p in found)),
              "externalGuids": sorted(external), "excludedCode": sorted(excluded), "files": []}
    if copy:
        if not (destination / "Packages/manifest.json").is_file():
            raise ValueError("Prepare the isolated Unity project's manifest before copying")
        for asset in sorted(found):
            for path in (asset, Path(str(asset) + ".meta")):
                if not path.is_file():
                    continue
                relative = path.relative_to(source)
                target = confined(destination / relative, destination)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                report["files"].append({"path": relative.as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        confined(destination / "rtdi-scene-snapshot.json", destination).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "files"}, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--scene", type=Path, required=True, help="Assets/.../*.unity path relative to source project")
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--copy", action="store_true")
    args = parser.parse_args()
    snapshot(args.source, args.scene, args.destination, args.copy)
