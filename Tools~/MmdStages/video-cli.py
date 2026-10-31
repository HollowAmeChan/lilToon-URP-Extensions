"""Explicit MMD video production commands for the running Unity editor."""
import argparse
import json
import pathlib
import subprocess
import sys
import time
import uuid

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["status", "cancel", "create", "setup", "adjust", "record", "validate-rotation", "mux"])
    parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(r"D:\Unity_Project\BREAK_URP"))
    parser.add_argument("--config", type=pathlib.Path, help="One film's 制作配置.json")
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--video", type=pathlib.Path, help="Silent Recorder MP4 for mux")
    parser.add_argument("--ffmpeg", type=pathlib.Path, default=pathlib.Path(r"D:\_SOFTWARE\ffmpeg-4.3.1-win64-static\bin\ffmpeg.exe"))
    parser.add_argument("--width", type=int, default=1080)
    parser.add_argument("--height", type=int, default=1920)
    parser.add_argument("--fps", type=int, default=60)
    parser.add_argument("--seconds", type=float, default=0)
    parser.add_argument("--full-body", action="store_true", help="Diagnostic full-body camera; does not change the saved scene")
    parser.add_argument("--disable-rtgi", action="store_true", help="Capture diagnostic without RTGI; scene profile edits are transient")
    parser.add_argument("--timeout", type=int, default=3600)
    args = parser.parse_args()
    work = args.project / "Library/HoMmdVideo"
    work.mkdir(parents=True, exist_ok=True)
    if args.action == "status":
        for name in ["result.json", "progress.json"]:
            if (work / name).exists():
                print(name, (work / name).read_text(encoding="utf-8-sig"))
        return
    if args.action == "cancel":
        (work / "cancel.txt").write_text("cancel", encoding="utf-8")
        return
    if not args.config:
        parser.error("--config is required")
    config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    if args.action == "mux":
        if not args.video or not args.output:
            parser.error("mux requires --video and --output")
        audio = args.project / config["audio"]
        args.output.parent.mkdir(parents=True, exist_ok=True)
        # Inputs are argument-array elements; names never pass through a shell.
        subprocess.run([str(args.ffmpeg), "-v", "error", "-n", "-i", str(args.video), "-i", str(audio),
                        "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                        "-shortest", "-movflags", "+faststart", "-metadata", "artist=tencha.3d",
                        "-metadata", "comment=Local WTD motion and Ho rendering test; see adjacent credits.",
                        str(args.output)], check=True)
        print(args.output)
        return
    if (work / "request.json").exists():
        raise SystemExit("A video command is already queued; inspect status before replacing it.")
    status_path = args.project / "Library/HoMmdStages/status.json"
    if status_path.exists():
        state = json.loads(status_path.read_text(encoding="utf-8-sig"))
        if state.get("playing") or state.get("compiling"):
            raise SystemExit("Unity is playing/compiling. Wait for completion or cancel this recording explicitly.")
        if any(scene.get("dirty") for scene in state.get("scenes", [])):
            raise SystemExit("A scene has unsaved edits; save it before a command that switches scenes.")
    request = dict(config)
    identifier = uuid.uuid4().hex
    request.update(id=identifier, action=args.action)
    if args.action == "record":
        if not args.output:
            parser.error("record requires --output (MP4 stem, without extension)")
        if pathlib.Path(str(args.output) + ".mp4").exists():
            raise SystemExit("Recording exists; select a new output stem.")
        request.update(output=str(args.output.resolve()), width=args.width, height=args.height,
                       fps=args.fps, seconds=args.seconds, fullBody=args.full_body, disableRtgi=args.disable_rtgi)
    temporary = work / (identifier + ".tmp")
    temporary.write_text(json.dumps(request, ensure_ascii=False), encoding="utf-8")
    temporary.replace(work / "request.json")
    deadline = time.monotonic() + args.timeout
    previous_progress = ""
    while time.monotonic() < deadline:
        try:
            result = json.loads((work / "result.json").read_text(encoding="utf-8-sig"))
            if result.get("id") == identifier:
                history = work / "History"
                history.mkdir(exist_ok=True)
                (history / (identifier + ".json")).write_text(
                    json.dumps({"request": request, "response": result}, ensure_ascii=False, indent=2), encoding="utf-8")
                print(json.dumps(result, ensure_ascii=False))
                if result.get("status") != "ok":
                    raise SystemExit(1)
                if args.action == "record" and status_path.exists():
                    # Recorder flushes the file before Unity completes its exit
                    # from Play. Do not let a batch start against stale playing=true.
                    idle_deadline = time.monotonic() + 90
                    result_stamp = (work / "result.json").stat().st_mtime
                    while time.monotonic() < idle_deadline:
                        try:
                            state = json.loads(status_path.read_text(encoding="utf-8-sig"))
                            if (not state.get("playing") and not state.get("compiling") and
                                    status_path.stat().st_mtime >= result_stamp):
                                break
                        except (OSError, json.JSONDecodeError):
                            pass
                        time.sleep(.5)
                    else:
                        raise SystemExit("Video is written, but Unity has not returned to edit mode; inspect before the next recording.")
                return
            if args.action == "record" and (work / "progress.json").exists():
                progress = json.loads((work / "progress.json").read_text(encoding="utf-8-sig"))
                if progress.get("output") == request["output"].replace("\\", "/"):
                    value = f'{progress["frame"]}/{progress["total"]}'
                    if value != previous_progress:
                        print(value, flush=True)
                        previous_progress = value
        except (OSError, json.JSONDecodeError):
            pass
        time.sleep(.5)
    raise SystemExit("Observation timeout; the Unity operation may still be running. Inspect status before retrying.")


if __name__ == "__main__":
    main()
