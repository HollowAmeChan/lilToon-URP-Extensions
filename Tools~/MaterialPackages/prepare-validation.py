"""Create an empty, physically no-lilToon material integration host."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--shader-root", type=Path, required=True)
    parser.add_argument("--urp-root", type=Path, required=True)
    parser.add_argument("--config-root", type=Path, required=True)
    parser.add_argument("--editor-version", default="6000.3.15f1")
    args = parser.parse_args()
    pipeline = Path(__file__).resolve().parents[2]
    sources = [pipeline, args.shader_root.resolve(), args.urp_root.resolve(), args.config_root.resolve()]
    target = args.output.resolve()
    if any(target == source or source in target.parents for source in sources):
        parser.error("Validation output must be outside all source repositories.")
    if target.exists() and any(target.iterdir()):
        parser.error("Output must be a new or empty directory; existing projects are never rewritten.")
    packages = {
        "jp.lilxyzw.liltoon.urp.extensions": pipeline,
        "com.unity.render-pipelines.universal": sources[2],
        "com.unity.render-pipelines.universal-config": sources[3],
    }
    for folder in ["Cloth_URP", "Hair_URP", "Skin_URP", "Environment_URP"]:
        root = sources[1] / folder
        package = json.loads((root / "package.json").read_text(encoding="utf-8-sig"))
        packages[package["name"]] = root
    for package_id, root in packages.items():
        identity = json.loads((root / "package.json").read_text(encoding="utf-8-sig"))["name"]
        if identity != package_id:
            parser.error(f"Package identity mismatch: {root}")
    dependencies = {name: "file:" + root.as_posix() for name, root in packages.items()}
    dependencies.update({"com.unity.modules.imgui": "1.0.0", "com.unity.modules.animation": "1.0.0",
                         "com.unity.modules.uielements": "1.0.0"})
    for folder in ["Assets", "Packages", "ProjectSettings"]:
        (target / folder).mkdir(parents=True, exist_ok=True)
    (target / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2) + "\n", encoding="utf-8")
    (target / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + args.editor_version + "\n", encoding="utf-8")
    print(json.dumps({"project": str(target), "materialPackageAbsent": "jp.lilxyzw.liltoon",
                      "setupMethod": "lilToon.URP.Extensions.Editor.RTGI.HoSpecializedMaterialValidation.Setup",
                      "runMethod": "lilToon.URP.Extensions.Editor.RTGI.HoSpecializedMaterialValidation.Run"}, indent=2))


if __name__ == "__main__":
    main()
