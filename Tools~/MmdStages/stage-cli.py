"""Send a scene-authoring command to the already running BREAK_URP editor."""
import argparse, json, pathlib, time, uuid, sys
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("action", choices=["status", "refresh", "catalog", "create", "logo", "review", "build", "render", "audit", "adjust"])
parser.add_argument("stage", nargs="?", default="花吐电気")
parser.add_argument("--width", type=int, default=1280)
parser.add_argument("--height", type=int, default=720)
parser.add_argument("--view", type=int, default=0)
parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(r"D:\Unity_Project\BREAK_URP"))
parser.add_argument("--folder", help="Stage Assets/... folder for create; asset scope for logo")
parser.add_argument("--scene", help="Saved Assets/...unity scene for review; no recipe required")
parser.add_argument("--pmx", action="append", default=[], help="PMX Assets/... path under the new stage's Source/; repeat for multiple parts")
parser.add_argument("--logo", help="Optional existing logo texture to copy into a new stage")
parser.add_argument("--output", help="Review directory; default is a new timestamped Library folder")
parser.add_argument("--warmup", type=int, default=16)
parser.add_argument("--logo-y", type=float, default=.1)
parser.add_argument("--logo-size", type=float, default=.3)
parser.add_argument("--timeout", type=int, default=300)
args = parser.parse_args()
if args.action == "create" and (not args.folder or not args.pmx):
    parser.error("create requires --folder and at least one --pmx; copy the complete package to Source/ first")
folder = args.project / "Library/HoMmdStages"
folder.mkdir(parents=True, exist_ok=True)
if (folder / "request.json").exists() or (folder / "running.json").exists():
    raise SystemExit("An editor command is already queued/running; inspect it first.")
identifier = uuid.uuid4().hex
request = dict(id=identifier, action=args.action, stage=args.stage, width=args.width, height=args.height, view=args.view)
request.update(scenePath=args.scene, stageFolder=args.folder, pmxPaths=args.pmx, logoPath=args.logo,
               outputPath=args.output, warmup=args.warmup, logoY=args.logo_y, logoSize=args.logo_size)
temporary = folder / (identifier + ".tmp")
temporary.write_text(json.dumps(request, ensure_ascii=False), encoding="utf-8")
temporary.replace(folder / "request.json")
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
