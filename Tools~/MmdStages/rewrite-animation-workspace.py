"""Rewrite non-GUID paths after a completed GUID-preserving animation migration."""
import argparse
import json
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--project", type=Path, default=Path(r"D:\Unity_Project\BREAK_URP"))
parser.add_argument("--plan", type=Path, required=True)
parser.add_argument("--map", type=Path, required=True)
parser.add_argument("--source-tools", type=Path, default=Path(__file__).parent)
args = parser.parse_args()
plan = json.loads(args.plan.read_text(encoding="utf-8-sig"))
mapping = json.loads(args.map.read_text(encoding="utf-8-sig"))
for section in ("folders", "files", "archives"):
    for move in plan[section]:
        if not (args.project / move["to"]).exists():
            raise SystemExit("Finish native relocation before rewriting paths: " + move["to"])

variants = {}
for old, new in mapping.items():
    variants[old] = new
    variants[old.replace("/", "\\")] = new.replace("/", "\\")
    variants[old.replace("/", "\\\\")] = new.replace("/", "\\\\")
    escaped_old = old.encode("unicode_escape").decode("ascii")
    escaped_new = new.encode("unicode_escape").decode("ascii")
    variants.setdefault(escaped_old, escaped_new)
    variants.setdefault(re.sub(r"\\u([0-9a-f]{4})", lambda m: "\\u" + m[1].upper(), escaped_old), re.sub(
        r"\\u([0-9a-f]{4})", lambda m: "\\u" + m[1].upper(), escaped_new
    ))
patterns = sorted(variants, key=len, reverse=True)
pattern = re.compile("|".join(re.escape(old) for old in patterns))

def rewrite(text):
    # One substitution pass prevents a destination that reuses a source path
    # from being rewritten a second time.
    return pattern.sub(lambda match: variants[match[0]], text)

extensions = {".cs", ".py", ".ps1", ".json", ".md", ".meta", ".asset", ".prefab", ".unity", ".mat", ".controller", ".overrideController", ".anim"}
roots = [
    args.project / "Assets/00动画",
    args.project / "Assets/Editor/MMD制作验证",
    args.source_tools,
]
changed = []
for root in roots:
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in extensions:
            continue
        raw = path.read_bytes()
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            continue
        updated = rewrite(text)
        if updated != text:
            path.write_bytes(updated.encode("utf-8"))
            changed.append(str(path))

for film in plan["films"]:
    path = args.project / film["config"]
    config = json.loads(path.read_text(encoding="utf-8-sig"))
    config.update(
        scene=film["scene"], folder=plan["commonFolder"],
        sharedCharacter=plan["rolePrefab"], stagePrefab=plan["stagePrefab"],
        soundtrackTimeOffset=film["audioOffset"],
    )
    # These are completed performance scenes. The old studio is not a runtime
    # dependency; create a new action by copying a shared-stage motion scene.
    config.pop("animationScene", None)
    config.pop("sourceScene", None)
    path.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("PATH_REWRITE_COMPLETE", len(changed), "text assets/tools; five scene configs updated")
