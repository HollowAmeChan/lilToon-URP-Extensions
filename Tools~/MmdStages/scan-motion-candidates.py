"""Read-only VMD/media inventory; never imports assets or changes a Unity scene."""

import argparse
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import unicodedata


def read_text(path):
    data = path.read_bytes()
    encodings = ["utf-8-sig", "utf-16"] if data[:2] in (b"\xff\xfe", b"\xfe\xff") else ["utf-8-sig"]
    encodings += ["gb18030", "cp932"] if "_cn" in path.name else ["cp932", "gb18030"]
    for encoding in encodings:
        try:
            return data.decode(encoding)
        except UnicodeError:
            pass
    return data.decode("utf-8", errors="replace")


def vmd_info(path):
    data = path.read_bytes()
    result = {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
    offset = 0

    def take(size):
        nonlocal offset
        if offset + size > len(data):
            raise ValueError(f"truncated VMD at byte {offset}, expected {size} bytes")
        value = data[offset:offset + size]
        offset += size
        return value

    def integer():
        return struct.unpack("<I", take(4))[0]

    def name(raw):
        return raw.split(b"\0", 1)[0].decode("cp932", errors="replace")

    def span(frames):
        return {
            "firstFrame": min(frames) if frames else None,
            "lastFrame": max(frames) if frames else None,
            "endSeconds": round(max(frames) / 30, 3) if frames else None,
        }

    try:
        signature = take(30).split(b"\0", 1)[0]
        if signature.startswith(b"Vocaloid Motion Data 0002"):
            result["model"] = name(take(20))
        elif signature.startswith(b"Vocaloid Motion Data file"):
            result["model"] = name(take(10))
        else:
            raise ValueError("invalid VMD signature")
        count = integer()
        bones = defaultdict(list)
        bone_stats = {}
        first_pose, dynamic_bones = {}, set()
        for _ in range(count):
            record = take(111)
            track = name(record[:15])
            frame = struct.unpack_from("<I", record, 15)[0]
            bones[track].append(frame)
            pose = struct.unpack_from("<7f", record, 19)
            first_pose.setdefault(track, pose)
            base = first_pose[track]
            position_changed = max(abs(a - b) for a, b in zip(base[:3], pose[:3])) > 1e-5
            rotation_changed = min(
                max(abs(a - b) for a, b in zip(base[3:], pose[3:])),
                max(abs(a + b) for a, b in zip(base[3:], pose[3:])),
            ) > 1e-5
            if position_changed or rotation_changed:
                dynamic_bones.add(track)
            if track in ("センター", "グルーブ", "左足ＩＫ", "右足ＩＫ", "左足IK", "右足IK"):
                xyz = struct.unpack_from("<3f", record, 19)
                stat = bone_stats.setdefault(track, {"min": list(xyz), "max": list(xyz)})
                stat["min"] = [min(a, b) for a, b in zip(stat["min"], xyz)]
                stat["max"] = [max(a, b) for a, b in zip(stat["max"], xyz)]
        result["bone"] = {
            "keys": count, "tracks": {k: len(v) for k, v in bones.items()},
            "dynamicTracks": sorted(dynamic_bones),
            "positionBoundsMmdUnits": bone_stats,
            **span([frame for values in bones.values() for frame in values]),
        }
        count = integer()
        morphs = defaultdict(lambda: {"keys": 0, "maxAbsWeight": 0})
        morph_frames = []
        for _ in range(count):
            record = take(23)
            track = name(record[:15])
            frame, weight = struct.unpack_from("<If", record, 15)
            morphs[track]["keys"] += 1
            morphs[track]["maxAbsWeight"] = max(morphs[track]["maxAbsWeight"], abs(weight))
            if abs(weight) > 1e-5:
                morph_frames.append(frame)
        result["morph"] = {"keys": count, "tracks": dict(morphs), "activeTracks": [
            k for k, v in morphs.items() if v["maxAbsWeight"] > 1e-5
        ], **span(morph_frames)}
        result["camera"] = {"keys": 0}
        result["light"] = {"keys": 0}
        result["selfShadow"] = {"keys": 0}
        result["showIK"] = {"keys": 0, "tracks": {}}
        if offset < len(data):
            count = integer()
            frames, distances, fovs, ortho = [], [], [], 0
            for _ in range(count):
                record = take(61)
                frames.append(struct.unpack_from("<I", record)[0])
                distances.append(struct.unpack_from("<f", record, 4)[0])
                fovs.append(struct.unpack_from("<I", record, 56)[0])
                ortho += record[60] != 0
            result["camera"] = {"keys": count, **span(frames), "orthographicKeys": ortho,
                                "distanceRange": [min(distances), max(distances)] if distances else [],
                                "fovRange": [min(fovs), max(fovs)] if fovs else []}
        for label, size in (("light", 28), ("selfShadow", 9)):
            if offset < len(data):
                count = integer()
                frames = [struct.unpack_from("<I", take(size))[0] for _ in range(count)]
                result[label] = {"keys": count, **span(frames)}
        if offset < len(data):
            count = integer()
            tracks = defaultdict(lambda: {"onKeys": 0, "offKeys": 0})
            for _ in range(count):
                take(5)
                for _ in range(integer()):
                    record = take(21)
                    tracks[name(record[:20])]["onKeys" if record[20] else "offKeys"] += 1
            result["showIK"] = {"keys": count, "tracks": dict(tracks)}
        result["tailBytes"] = len(data) - offset
        result["roles"] = [k for k in ("bone", "morph", "camera", "light", "selfShadow", "showIK")
                           if result[k]["keys"] > 0]
        normalized = {unicodedata.normalize("NFKC", k) for k in bones}
        body_markers = {"センター", "グルーブ", "上半身", "下半身", "左腕", "右腕", "左足", "右足"}
        result["boneKind"] = ("body" if normalized & body_markers else
                              "faceOrAuxiliary" if bones else "none")
    except (ValueError, struct.error) as exc:
        result["error"] = str(exc)
    return result


def media_info(item):
    path, ffprobe = item
    result = {"path": str(path), "bytes": path.stat().st_size}
    try:
        proc = subprocess.run(
            [str(ffprobe), "-v", "error", "-show_entries",
             "format=duration:stream=codec_type,codec_name,width,height,sample_rate,duration",
             "-of", "json", str(path)], capture_output=True, timeout=30,
            encoding="utf-8", errors="replace",
        )
        if proc.returncode:
            result["error"] = proc.stderr[:300]
        else:
            result.update(json.loads(proc.stdout))
            result["hasAudio"] = any(s["codec_type"] == "audio" for s in result.get("streams", []))
    except (OSError, subprocess.TimeoutExpired, ValueError) as exc:
        result["error"] = str(exc)
    return result


def work_roots(root):
    for folder in sorted(root.iterdir(), key=lambda p: p.name.casefold()):
        if not folder.is_dir() or folder.name.startswith("_"):
            continue
        if folder.name.startswith("A甜茶旧合集"):
            collection = folder / "甜茶合集动作"
            for child in sorted(collection.iterdir(), key=lambda p: p.name.casefold()):
                if child.is_dir():
                    yield child, True
        else:
            yield folder, False


def scan(root, ffprobe):
    works = []
    for folder, old_collection in work_roots(root):
        files = sorted((p for p in folder.rglob("*") if p.is_file() and p.suffix.lower() != ".meta"),
                       key=lambda p: str(p).casefold())
        vmds = []
        for path in files:
            if path.suffix.lower() == ".vmd":
                item = vmd_info(path)
                item["referenceOnly"] = any(
                    part.casefold() in ("quickmagic", "修正前") for part in path.relative_to(folder).parts[:-1])
                vmds.append(item)
        media_paths = [p for p in files if p.suffix.lower() in
                       (".wav", ".mp3", ".ogg", ".flac", ".m4a", ".mp4", ".mov", ".avi", ".webm")]
        with ThreadPoolExecutor(max_workers=4) as pool:
            media = list(pool.map(media_info, [(p, ffprobe) for p in media_paths]))
        readmes = [{"path": str(p), "text": read_text(p)} for p in files
                   if p.suffix.lower() in (".txt", ".md") and p.name != "_source.txt"]
        works.append({
            "name": folder.name, "folder": str(folder), "oldCollection": old_collection,
            "files": [str(p) for p in files], "vmds": vmds, "media": media, "readmes": readmes,
            "sizingPmx": [str(p) for p in files if p.suffix.lower() == ".pmx"],
        })
    hash_groups = defaultdict(list)
    for work in works:
        for item in work["vmds"]:
            hash_groups[item["sha256"]].append({"work": work["name"], "path": item["path"]})
    duplicates = [{"sha256": sha, "files": items} for sha, items in hash_groups.items() if len(items) > 1]
    return {"generatedAt": datetime.now().astimezone().isoformat(), "root": str(root),
            "vmdFps": 30, "works": works, "duplicateVmds": duplicates,
            "summary": {"works": len(works), "vmdFiles": sum(len(w["vmds"]) for w in works),
                        "vmdErrors": sum("error" in v for w in works for v in w["vmds"]),
                        "uniqueVmds": len(hash_groups), "duplicateHashGroups": len(duplicates),
                        "mediaProbeErrors": sum("error" in m for w in works for m in w["media"])}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--summary-only", action="store_true")
    parser.add_argument("--ffprobe", type=Path, default=Path(r"D:/_SOFTWARE/ffmpeg-4.3.1-win64-static/bin/ffprobe.exe"))
    args = parser.parse_args()
    result = scan(args.root.resolve(), args.ffprobe)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["summary"], ensure_ascii=False))
    if args.summary_only:
        return
    for work in result["works"]:
        valid = [v for v in work["vmds"] if not v.get("error") and not v["referenceOnly"]]
        body = [v for v in valid if v["boneKind"] == "body"]
        cams = [v for v in valid if v["camera"]["keys"]]
        sounds = [m for m in work["media"] if m.get("hasAudio")]
        lengths = sorted(set(v["bone"]["endSeconds"] for v in body))
        print(json.dumps({"name": work["name"], "old": work["oldCollection"],
                          "body": [str(Path(v["path"]).relative_to(work["folder"])) for v in body],
                          "seconds": lengths, "cameras": len(cams),
                          "audioOrPv": [Path(m["path"]).name for m in sounds],
                          "pmx": len(work["sizingPmx"])}, ensure_ascii=False))


if __name__ == "__main__":
    main()
