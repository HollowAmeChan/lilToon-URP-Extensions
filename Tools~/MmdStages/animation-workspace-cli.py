"""Run an explicit asset-layout migration in the existing Unity Editor."""
import argparse
import json
from pathlib import Path
import sys
import time
import uuid

sys.dont_write_bytecode = True
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("action", choices=("inventory", "state", "relocate", "consolidate", "cleanup", "verify"))
parser.add_argument("--plan", type=Path, required=True)
parser.add_argument("--project", type=Path, default=Path(r"D:\Unity_Project\BREAK_URP"))
parser.add_argument("--timeout", type=int, default=600)
args = parser.parse_args()
work = args.project / "Library/HoAnimationWorkspace"
work.mkdir(parents=True, exist_ok=True)
if (work / "request.json").exists():
    raise SystemExit("A migration is already queued; inspect its result before resubmitting.")
identifier = uuid.uuid4().hex
request = dict(id=identifier, action=args.action, plan=str(args.plan.resolve()))
temp = work / (identifier + ".tmp")
temp.write_text(json.dumps(request, ensure_ascii=False), encoding="utf-8")
temp.replace(work / "request.json")
deadline = time.monotonic() + args.timeout
while time.monotonic() < deadline:
    try:
        result = json.loads((work / "result.json").read_text(encoding="utf-8-sig"))
        if result.get("id") == identifier:
            history = work / "History"
            history.mkdir(exist_ok=True)
            (history / (identifier + ".json")).write_text(
                json.dumps(dict(request=request, response=result), ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
            print(json.dumps(result, ensure_ascii=False))
            raise SystemExit(0 if result["status"] == "ok" else 1)
    except (OSError, ValueError):
        pass
    time.sleep(.5)
raise SystemExit("Observation timeout; the Unity operation may still be running. Inspect it before retrying.")
