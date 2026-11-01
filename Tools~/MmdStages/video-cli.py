"""Explicit MMD video production commands for the running Unity editor."""
import argparse
import json
import pathlib
import subprocess
import sys
import time
import uuid
sys.dont_write_bytecode = True
from render_presets import ASPECT_RATIOS, QUALITY_SHORT_EDGES, list_presets, resolve_output

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["status", "presets", "plan", "cancel", "create", "setup", "adjust", "record", "validate-rotation", "mux",
                                         "compose-stage", "inspect-stage", "place-stage", "focus-stage", "render-performance",
                                         "diagnose-render", "configure-camera-post", "enable-scene-post",
                                         "test-render-presets", "proof-post", "proof-glow", "proof-ho-only", "strip-native-post",
                                         "diagnose-rtshadow", "configure-stage-rtshadow"])
    parser.add_argument("--project", type=pathlib.Path, default=pathlib.Path(r"D:\Unity_Project\BREAK_URP"))
    parser.add_argument("--config", type=pathlib.Path, help="One film's 制作配置.json")
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--video", type=pathlib.Path, help="Silent Recorder MP4 for mux")
    parser.add_argument("--ffmpeg", type=pathlib.Path, default=pathlib.Path(r"D:\_SOFTWARE\ffmpeg-4.3.1-win64-static\bin\ffmpeg.exe"))
    parser.add_argument("--quality", choices=list(QUALITY_SHORT_EDGES), default="draft",
                        help="Output size: draft=360p, preview=720p, review=1080p, high=2160p. Effects stay unchanged.")
    parser.add_argument("--aspect", choices=list(ASPECT_RATIOS), default="portrait")
    parser.add_argument("--width", type=int, help="Advanced override; provide both dimensions")
    parser.add_argument("--height", type=int, help="Advanced override; provide both dimensions")
    parser.add_argument("--fps", type=int, choices=[30, 60], default=30)
    parser.add_argument("--seconds", type=float, default=0)
    parser.add_argument("--aa", choices=["inherit", "taa", "smaa", "fxaa", "none"],
                        help="Recording-camera AA override for this play session; defaults to the saved camera setting")
    parser.add_argument("--post-stack", choices=["authored", "image-off", "screen-off"], default="authored",
                        help="Diagnostic A/B only; off variants use a temporary runtime Volume profile")
    parser.add_argument("--position", type=float, nargs=3, metavar=("X", "Y", "Z"),
                        help="Place the whole performance (character, source rig and VMD camera) together")
    parser.add_argument("--yaw", type=float, help="Performance heading in degrees")
    parser.add_argument("--camera-name", help="Explicit camera; proofs also accept '$scene-view' for the actual Scene View camera")
    parser.add_argument("--full-body", action="store_true", help="Diagnostic full-body camera; does not change the saved scene")
    parser.add_argument("--enable-part", action="append", default=[],
                        help="Enable a character Parts child only during recording; repeat for multiple parts")
    parser.add_argument("--disable-part", action="append", default=[],
                        help="Disable a character Parts child only during recording; repeat for multiple parts")
    parser.add_argument("--disable-rtgi", action="store_true", help="Capture diagnostic without RTGI; scene profile edits are transient")
    parser.add_argument("--timeout", type=int, default=3600)
    args = parser.parse_args()
    if args.action == "presets":
        print(json.dumps(list_presets(), ensure_ascii=False, indent=2))
        return
    try:
        output_settings = resolve_output(args.quality, args.aspect, args.fps, args.width, args.height)
    except ValueError as error:
        parser.error(str(error))
    if args.action == "plan":
        print(json.dumps(output_settings, ensure_ascii=False, indent=2))
        return
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
        report_path = args.video.with_suffix(".render.json")
        report = json.loads(report_path.read_text(encoding="utf-8-sig")) if report_path.exists() else {}
        offset = float(report.get("soundtrackTimeOffset", config.get("soundtrackTimeOffset", 0)))
        # Inputs are argument-array elements; names never pass through a shell.
        command = [str(args.ffmpeg), "-v", "error", "-n", "-i", str(args.video), "-i", str(audio),
                   "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k"]
        if offset > 0:
            command += ["-af", f"adelay={offset * 1000:.6f}:all=1,apad"]
        elif offset < 0:
            command += ["-af", f"atrim=start={-offset:.9f},asetpts=PTS-STARTPTS,apad"]
        if offset:
            if report.get("frames") and report.get("fps"):
                duration = report["frames"] / report["fps"]
            else:
                probe = args.ffmpeg.with_name("ffprobe" + args.ffmpeg.suffix)
                info = json.loads(subprocess.check_output(
                    [str(probe), "-v", "error", "-show_entries", "format=duration",
                     "-of", "json", str(args.video)], encoding="utf-8"))
                duration = float(info["format"]["duration"])
            # Copying B-frame video with -shortest can stop audio at the last
            # DTS rather than the final displayed frame. Use the output timeline.
            command += ["-t", f"{duration:.9f}"]
        else:
            command += ["-shortest"]
        command += ["-movflags", "+faststart", "-metadata", "artist=tencha.3d",
                    "-metadata", "comment=Local WTD motion and Ho rendering test; see adjacent credits.",
                    str(args.output)]
        subprocess.run(command, check=True)
        print(args.output)
        return
    if (work / "request.json").exists():
        raise SystemExit("A video command is already queued; inspect status before replacing it.")
    status_path = args.project / "Library/HoMmdStages/status.json"
    if status_path.exists():
        state = json.loads(status_path.read_text(encoding="utf-8-sig"))
        if state.get("playing") or state.get("compiling"):
            raise SystemExit("Unity is playing/compiling. Wait for completion or cancel this recording explicitly.")
        dirty_scenes = [scene for scene in state.get("scenes", []) if scene.get("dirty")]
        in_place_focus = (args.action in ("focus-stage", "diagnose-render", "proof-post", "proof-glow", "enable-scene-post", "proof-ho-only", "strip-native-post", "diagnose-rtshadow", "configure-stage-rtshadow") and
                          all(scene.get("path") == config.get("scene") for scene in dirty_scenes))
        if dirty_scenes and not in_place_focus:
            raise SystemExit("A scene has unsaved edits; save it before a command that switches scenes.")
    request = dict(config)
    identifier = uuid.uuid4().hex
    request.update(id=identifier, action=args.action)
    if args.camera_name is not None:
        request["cameraName"] = args.camera_name
    if args.aa is not None:
        request["antialiasing"] = args.aa
    if args.position is not None:
        request["performancePosition"] = dict(zip(("x", "y", "z"), args.position))
    if args.yaw is not None:
        request["performanceYaw"] = args.yaw
    if args.action == "render-performance":
        if not args.output:
            parser.error("render-performance requires --output (PNG path); --seconds selects the pose")
        if args.output.exists():
            raise SystemExit("Image exists; select a new output path.")
        request.update(output_settings, output=str(args.output.resolve()), seconds=args.seconds)
    if args.action in ("proof-post", "proof-glow", "proof-ho-only", "diagnose-rtshadow"):
        request.update(output_settings)
        if args.output is not None:
            request["output"] = str(args.output.resolve())
    if args.action == "record":
        if not args.output:
            parser.error("record requires --output (MP4 stem, without extension)")
        if pathlib.Path(str(args.output) + ".mp4").exists():
            raise SystemExit("Recording exists; select a new output stem.")
        request.update(output_settings, output=str(args.output.resolve()),
                       seconds=args.seconds, fullBody=args.full_body, disableRtgi=args.disable_rtgi,
                       postStack=args.post_stack, enableParts=args.enable_part, disableParts=args.disable_part)
        if args.aa is not None:
            request["antialiasing"] = args.aa
        print("RENDER_PLAN " + json.dumps(output_settings, ensure_ascii=False), flush=True)
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
