"""Send a scene-authoring command to the already running BREAK_URP editor."""
import argparse, json, pathlib, time, uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("action", choices=["status", "refresh", "catalog", "build", "render", "audit", "adjust"])
parser.add_argument("stage", nargs="?", default="花吐电気")
parser.add_argument("--width", type=int, default=1280)
parser.add_argument("--height", type=int, default=720)
parser.add_argument("--view", type=int, default=0)
parser.add_argument("--timeout", type=int, default=300)
args = parser.parse_args()
folder = pathlib.Path(r"D:\Unity_Project\BREAK_URP\Library\HoMmdStages")
if (folder / "request.json").exists() or (folder / "running.json").exists():
    raise SystemExit("An editor command is already queued/running; inspect it first.")
identifier = uuid.uuid4().hex
request = dict(id=identifier, action=args.action, stage=args.stage, width=args.width, height=args.height, view=args.view)
(folder / "request.json").write_text(json.dumps(request, ensure_ascii=False), encoding="utf-8")
deadline = time.monotonic() + args.timeout
while time.monotonic() < deadline:
    try:
        result = json.loads((folder / "result.json").read_text(encoding="utf-8-sig"))
        if result.get("id") == identifier:
            history = folder / "History"
            history.mkdir(exist_ok=True)
            (history / (identifier + ".json")).write_text(json.dumps(dict(request=request,response=result), ensure_ascii=False,indent=2),encoding="utf-8")
            print(json.dumps(result, ensure_ascii=False))
            raise SystemExit(0 if result.get("status") == "ok" else 1)
    except (OSError, json.JSONDecodeError):
        pass
    time.sleep(.5)
raise SystemExit("Observation timeout; command may still be running. Inspect running.json/status and the live editor before retrying.")
