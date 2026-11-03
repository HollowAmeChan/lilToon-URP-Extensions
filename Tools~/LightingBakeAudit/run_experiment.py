"""Run one explicit phase in the isolated project, with a hidden D3D12 editor."""
import argparse
import pathlib
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")
repo = pathlib.Path(__file__).resolve().parents[2]
root = repo / ".codex-research/LightingBakeAudit-20261102"
out = repo / "Temp~/LightingBakeAudit-20261102"
parser = argparse.ArgumentParser(__doc__)
parser.add_argument("phase", choices=["repair-import", "export", "prepare", "pose", "prepare-probes", "bake", "render", "bench", "build-player", "player-run"])
parser.add_argument("--stage", default="花吐电気")
parser.add_argument("--mode", default="A", choices=list("ABCDEF"))
parser.add_argument("--probe-system", default="APV", choices=["APV", "Legacy"])
args = parser.parse_args()
assert (root / "Library/HoLightingBakeAudit.private").read_text() == "ho-lighting-bake-audit-20261102"
if args.phase == "player-run":
    system = args.probe_system
    executable = out / "Player" / system / "HoLightingBakeAudit.exe"
    if not executable.is_file():
        raise SystemExit(f"Build missing: {executable}")
    result_path = out / "Player" / args.stage / system / args.mode
    result_path.parent.mkdir(parents=True, exist_ok=True)
    log = result_path.with_suffix(".log")
    command = [str(executable), "-force-d3d12", "-screen-fullscreen", "0",
               "-screen-width", "1280", "-screen-height", "720",
               "-logFile", str(result_path.with_suffix(".unity.log")),
               "-hoStage", args.stage, "-hoMode", args.mode, "-hoOutput", str(result_path)]
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    with log.open("w", encoding="utf-8") as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, startupinfo=startup,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            result_code = process.wait(timeout=300)
        except subprocess.TimeoutExpired:
            process.terminate()
            raise SystemExit("Player timed out after five minutes; inspect the per-mode log.")
    report = result_path.with_suffix(".json")
    text = log.read_text(encoding="utf-8-sig", errors="replace")
    if result_code != 0 or not report.is_file():
        print("\n".join(text.splitlines()[-28:]))
        raise SystemExit(f"Player exited {result_code}; no verified timing report.")
    print(report.read_text(encoding="utf-8-sig"))
    raise SystemExit(0)
destination = root / "Assets/00动画/MMD场景/_制作工具/Editor"
for source in (repo / "Tools~/LightingBakeAudit/Unity/Editor").glob("*.cs"):
    shutil.copy2(source, destination / source.name)
runtime = root / "Assets/00动画/MMD场景/_制作工具/Editor/HoLightingBakePlayer.cs"
shutil.copy2(repo / "Tools~/LightingBakeAudit/Unity/Runtime/HoLightingBakePlayer.cs", runtime)
for name in ("HoLightingBakeExport.cs", "HoLightingAudit.cs"):
    shutil.copy2(repo / "Tools~/MmdStages/Unity/Editor" / name, destination / name)
gi_file = root / "LocalPackages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/HoRTGIRendererFeature.cs"
gi_source = gi_file.read_text(encoding="utf-8-sig")
old_class = "private sealed class HoRTGIPass : ScriptableRenderPass\n        {"
new_class = old_class + '\n            internal static readonly ProfilingSampler LightingBakeAuditSampler = new ProfilingSampler("Ho-RTGI Audit");'
if new_class not in gi_source:
    if gi_source.count(old_class) != 1:
        raise SystemExit("Unable to install the isolated RTGI GPU timing marker")
    gi_source = gi_source.replace(old_class, new_class)
old_pass = 'graph.AddUnsafePass<Data>("Ho-RTGI reference GI passes",out var data)'
new_pass = 'graph.AddUnsafePass<Data>("Ho-RTGI reference GI passes",out var data,LightingBakeAuditSampler)'
if new_pass not in gi_source:
    if gi_source.count(old_pass) != 1:
        raise SystemExit("Unable to locate the isolated RTGI pass")
    gi_source = gi_source.replace(old_pass, new_pass)
    gi_file.write_text(gi_source, encoding="utf-8")
log = out / f"{args.stage}-{args.mode}-{args.phase}.log"
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
method = "HoLightingBakeExport.RunBatch" if args.phase == "export" else "HoLightingBakeExperiment.RunBatch"
arguments = [
    "C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe",
    "-batchmode", "-force-d3d12", "-projectPath", "D:/HoBakeAudit26",
    "-executeMethod", method,
    "-logFile", str(log),
]
if args.phase == "export":
    arguments += ["-hoOutput", str(out / "import-inspection-final.json")]
elif args.phase == "build-player":
    arguments += ["-hoPhase", args.phase, "-hoStage", args.stage, "-hoMode", args.mode,
                  "-hoProbeSystem", args.probe_system]
else:
    arguments += ["-hoPhase", args.phase, "-hoStage", args.stage, "-hoMode", args.mode]
result = subprocess.run(arguments, startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW)
text = log.read_text(encoding="utf-8-sig", errors="replace")
expected = "HO_BAKE_EXPERIMENT=" if args.phase != "export" else "HO_BAKE_EXPORT="
if result.returncode or expected not in text:
    lines = [line[:260] for line in text.splitlines()
             if any(term in line for term in ("error CS", "Exception:", "HO_BAKE_", "ERROR", "Error:"))]
    print("\n".join(lines[-25:]))
    raise SystemExit(result.returncode or 1)
marker = "HO_LIGHTING_BAKE_BUILD=" if args.phase == "build-player" else expected
if args.phase == "build-player":
    marker = "HO_BAKE_EXPERIMENT="
print(next(line for line in reversed(text.splitlines()) if marker in line))
