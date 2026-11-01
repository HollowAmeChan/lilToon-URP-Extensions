"""Resume a serial, explicitly configured MMD video batch in the running Editor."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.dont_write_bytecode = True
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--project", type=Path, default=Path(r"D:\Unity_Project\BREAK_URP"))
    parser.add_argument("--ffmpeg", type=Path, default=Path(r"D:\_SOFTWARE\ffmpeg-4.3.1-win64-static\bin\ffmpeg.exe"))
    parser.add_argument("--ffprobe", type=Path, default=Path(r"D:\_SOFTWARE\ffmpeg-4.3.1-win64-static\bin\ffprobe.exe"))
    parser.add_argument("--only", action="append", help="Render only the named performance; repeat as needed")
    parser.add_argument("--timeout", type=int, default=3600)
    args = parser.parse_args()
    data = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
    cli = Path(__file__).with_name("video-cli.py")
    project = args.project.resolve()
    width, height, fps = data["width"], data["height"], data["fps"]
    names = [film["name"] for film in data["films"]]
    outputs = [film["output"] for film in data["films"]]
    if len(names) != len(set(names)) or len(outputs) != len(set(outputs)):
        raise ValueError("Each batch entry needs a unique name and output path, including outfit variants.")

    def save():
        temporary = args.manifest.with_suffix(".json.tmp")
        temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        temporary.replace(args.manifest)

    def call_cli(action, film, *extra):
        subprocess.run(
            [sys.executable, "-X", "utf8", str(cli), action, "--project", str(project),
             "--config", str(project / film["config"]), "--timeout", str(args.timeout), *map(str, extra)],
            check=True,
        )

    def probe(path):
        return json.loads(subprocess.check_output(
            [str(args.ffprobe), "-v", "error", "-show_entries",
             "stream=codec_type,codec_name,width,height,r_frame_rate,nb_frames,duration:format=duration,size",
             "-of", "json", str(path)], encoding="utf-8",
        ))

    def wait_for_editor():
        deadline = time.monotonic() + args.timeout
        last_notice = 0
        while time.monotonic() < deadline:
            blocked = []
            stage_work = project / "Library/HoMmdStages"
            for name in ("running.json", "request.json"):
                if (stage_work / name).exists():
                    blocked.append("stage " + name)
            if (project / "Library/HoMmdVideo/request.json").exists():
                blocked.append("video request")
            try:
                state = json.loads((stage_work / "status.json").read_text(encoding="utf-8-sig"))
                if state.get("playing") or state.get("compiling"):
                    blocked.append("playing/compiling")
                if any(scene.get("dirty") for scene in state.get("scenes", [])):
                    raise RuntimeError("Save open scene edits before recording; the batch will not discard them.")
            except (OSError, json.JSONDecodeError):
                blocked.append("editor status unavailable")
            if not blocked:
                return
            if time.monotonic() - last_notice >= 30:
                print("WAIT_EDITOR " + ", ".join(blocked), flush=True)
                last_notice = time.monotonic()
            time.sleep(1)
        raise TimeoutError("Editor remained busy. Inspect its current task before resuming.")

    def check_report(report, enable_parts, disable_parts):
        assert (report["width"], report["height"], report["fps"]) == (width, height, fps)
        if data["antialiasing"] == "taa":
            assert report["taaVerified"], "Requested TAA was not verified"
            assert (report["taaHistoryWidth"], report["taaHistoryHeight"]) == (width, height)
            assert report["postExecution"]["temporalAA"]
        assert report["postExecution"]["hoOnly"] and not report["postExecution"]["nativeColorLut"]
        expected = {name: False for name in disable_parts}
        expected.update({name: True for name in enable_parts})
        actual = {entry["name"]: entry["active"] for entry in report["partStates"]}
        assert all(actual.get(name) is active for name, active in expected.items()), actual
        assert report["screenProcessFrames"] > 0 and report["imageProcessFrames"] > 0

    for film in data["films"]:
        if args.only and film["name"] not in args.only:
            continue
        enable_parts = film.get("enableParts", data.get("enableParts", []))
        disable_parts = film.get("disableParts", data.get("disableParts", []))
        if set(enable_parts) & set(disable_parts):
            raise ValueError("A Parts child cannot be enabled and disabled: " + film["name"])
        output = project / film["output"]
        if film["status"] == "complete" and output.is_file():
            print("ALREADY_COMPLETE " + film["name"], flush=True)
            continue
        if not (project / film["scene"]).is_file():
            raise FileNotFoundError("Build the scene before recording: " + film["scene"])
        silent = project / "Library/HoMmdVideo" / data["batch"] / (film["name"] + "-silent")
        film["silent"] = str(silent)
        film["status"] = "recording"
        save()
        try:
            report_path = Path(str(silent) + ".render.json")
            silent_video = Path(str(silent) + ".mp4")
            if not silent_video.exists():
                wait_for_editor()
                extra = [
                    "--quality", data["qualityPreset"], "--aspect", data["aspectPreset"],
                    "--fps", fps, "--aa", data["antialiasing"], "--output", silent,
                ]
                for name in disable_parts:
                    extra.extend(["--disable-part", name])
                for name in enable_parts:
                    extra.extend(["--enable-part", name])
                print("RECORD " + film["name"], flush=True)
                call_cli("record", film, *extra)
            # An existing invalid intermediate is never silently overwritten.
            report = json.loads(report_path.read_text(encoding="utf-8-sig"))
            check_report(report, enable_parts, disable_parts)
            # A canceled take can still have a report containing the requested
            # frame count. Verify the encoded intermediate before publishing it.
            silent_info = probe(silent_video)
            silent_stream = next(stream for stream in silent_info["streams"] if stream["codec_type"] == "video")
            if int(silent_stream.get("nb_frames", 0)) != report["frames"]:
                raise RuntimeError("Incomplete silent capture; preserve or move this take before retrying: " + str(silent_video))
            film["status"] = "recorded"
            save()
            if not output.exists():
                call_cli("mux", film, "--video", silent_video, "--output", output, "--ffmpeg", args.ffmpeg)
            info = probe(output)
            video = next(stream for stream in info["streams"] if stream["codec_type"] == "video")
            assert (video["width"], video["height"], video["r_frame_rate"]) == (width, height, f"{fps}/1")
            assert int(video["nb_frames"]) == report["frames"], (video, report["frames"])
            assert any(stream["codec_type"] == "audio" for stream in info["streams"])
            subprocess.run([str(args.ffmpeg), "-v", "error", "-i", str(output), "-f", "null", "-"], check=True)
            review = output.with_name(output.stem + "-review.jpg")
            if not review.exists():
                count = int(video["nb_frames"])
                frames = sorted({min(count - 1, int(count * fraction)) for fraction in (.12, .36, .62, .88)})
                select = "+".join(f"eq(n\\,{frame})" for frame in frames)
                subprocess.run(
                    [str(args.ffmpeg), "-v", "error", "-n", "-i", str(output),
                     "-vf", f"select={select},scale=270:-1,tile=4x1",
                     "-vsync", "0", "-frames:v", "1", "-q:v", "2", str(review)], check=True,
                )
            sidecar = output.with_suffix(".render.json")
            shutil.copyfile(report_path, sidecar)
            film.update(
                status="complete", frames=int(video["nb_frames"]),
                duration=float(info["format"]["duration"]), bytes=int(info["format"]["size"]),
                review=str(review.relative_to(project)).replace("\\", "/"),
                renderReport=str(sidecar.relative_to(project)).replace("\\", "/"),
            )
            film.pop("error", None)
            save()
            print("COMPLETE " + json.dumps(
                {key: film[key] for key in ("name", "frames", "duration", "bytes", "output", "review")},
                ensure_ascii=False), flush=True)
        except Exception as error:
            film.update(status="failed", error=str(error))
            save()
            raise

    print("BATCH_STATUS " + json.dumps(
        {film["name"]: film["status"] for film in data["films"]}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
