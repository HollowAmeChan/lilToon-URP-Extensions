"""Inventory saved Unity assets and emit a conservative Ho Shadows migration plan as JSON."""

import argparse
from collections import Counter, defaultdict
from functools import lru_cache
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import subprocess
import sys


EXTENSIONS = Path(__file__).resolve().parents[2]
SCRIPT_PATHS = {
    "cast": "Runtime/ShadowCast/HoShadowCastRendererFeature.cs",
    "castLight": "Runtime/ShadowCast/HoShadowCastLight.cs",
    "character": "Runtime/CharacterShadow/HoCharacterShadowRendererFeature.cs",
    "characterBounds": "Runtime/CharacterShadow/HoCharacterShadow.cs",
    "characterVolume": "Runtime/CharacterShadow/HoCharacterShadowVolume.cs",
    "rt": "Runtime/RTShadow/HoRTShadowRendererFeature.cs",
    "rtLight": "Runtime/RTShadow/HoRTShadowLight.cs",
    "rtReceiver": "Runtime/RTShadow/HoRTShadowReceiver.cs",
    "rtVolume": "Runtime/RTShadow/HoRTShadowVolume.cs",
    "unified": "Runtime/Shadows/HoShadowsRendererFeature.cs",
    "characterSpecialization": "Runtime/CharacterSpecialization/HoCharacterSpecializationRendererFeature.cs",
    "objectGroup": "Runtime/ObjectBuffer/HoObjectBufferGroup.cs",
    "oit": "Runtime/OIT/WeightedOITRendererFeature.cs",
}
SHADERS = {
    "Skin": ("com.hoshader.skin-urp-stylized", "Skin_URP/Runtime/Shaders/Skin.shader"),
    "Eye": ("com.hoshader.skin-urp-stylized", "Skin_URP/Runtime/Shaders/Eye.shader"),
    "Environment": ("com.hoshader.environment-urp", "Environment_URP/Runtime/Shaders/Environment.shader"),
    "Cloth": ("com.hoshader.cloth-urp", "Cloth_URP/Runtime/Shaders/Cloth.shader"),
    "Hair": ("com.hoshader.hair-urp", "Hair_URP/Runtime/Shaders/Hair.shader"),
    "Effect": ("com.hoshader.effect-urp", "Effect_URP/Runtime/Shaders/EffectToon.shader"),
    "CommercialSkin": ("com.hoshader.skin-urp", "Skin_URP_Commercial/Runtime/Shaders/SkinUrp.shader"),
}
PROPERTIES = (
    "_HoRPShadowMode", "_UseShadow", "_ReceiveShadows", "_ReceiveCastShadows",
    "_AdditionalLightIntensity", "_AdditionalLightShadowStrength", "_ShadowMode",
    "_SurfaceMode", "_Surface", "_AlphaClip", "_UseHoOIT", "_AlphaToCoverage",
    "_TransparentHoCoverage", "_TransparentZWrite", "_HoCharacterCaptureOpacity",
    "_ReceiveShadowStrength", "_AdditionalLightStrength", "_RenderMode", "_SurfaceEnabled",
    "_ZWrite", "_SrcBlend", "_DstBlend", "_BlendOp", "_QueueControl", "_StencilComp",
    "_MainLightShadowStrength",
)
PREVIEW = "_HO_SHADOWS_SCREEN_PREVIEW"
SYMBOLS = re.compile(
    r"Ho(?:ShadowCast|CharacterShadow|RTShadow)\w+|"
    r"_Ho(?:RPShadowMode|ShadowCast\w*|CSActive|RTShadow\w*|ShadowsScreen\w*)|"
    r"\b(?:_UseShadow|_ReceiveShadows|_AdditionalLightShadowStrength|_UseHoOIT|_HO_SHADOWS_SCREEN_PREVIEW)\b"
)
PROPERTY_OVERRIDES = re.compile(
    r"(?m)^\s*(?:propertyPath|attribute):\s*(.*(?:_HoRPShadowMode|_UseShadow|_ReceiveShadows"
    r"|_AdditionalLightShadowStrength|_UseHoOIT|_SurfaceMode|_AlphaToCoverage|_HoCharacterCaptureOpacity"
    r"|_HO_SHADOWS_SCREEN_PREVIEW|m_RendererIndex|m_RenderShadows).*)$"
)
GUID = re.compile(r"\bguid: ([a-f0-9]{32})\b")
DOC = re.compile(r"(?m)^--- !u!(\d+) &(-?\d+)[^\n]*\n")
NUMBER = r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?"


def read(path):
    return path.read_text(encoding="utf-8-sig", errors="replace")


def guid_for(path):
    meta = Path(str(path) + ".meta")
    match = GUID.search(read(meta)) if meta.is_file() else None
    return match[1] if match else None


def asset_stamp(path):
    return {"path": path.as_posix(), "sha256": sha256(path.read_bytes()).hexdigest(), "guid": guid_for(path)}


def field(source, name, indent=2, default=None):
    match = re.search(rf"(?m)^ {{{indent}}}{re.escape(name)}:[ \t]*([^\n]*)$", source)
    return match[1].strip() if match else default


def field_guid(source, name):
    match = GUID.search(field(source, name, default=""))
    return match[1] if match else None


def file_id(value):
    match = re.search(r"fileID: (-?\d+)", value or "")
    return int(match[1]) if match else None


def scalar_fields(source, indent):
    return {
        key: value.strip()
        for key, value in re.findall(rf"(?m)^ {{{indent}}}(\w+): ([^\n]+)", source)
    }


def yaml_documents(source):
    headers = list(DOC.finditer(source))
    for index, header in enumerate(headers):
        end = headers[index + 1].start() if index + 1 < len(headers) else len(source)
        yield int(header[1]), int(header[2]), source[header.end():end]


def listed(source, key):
    match = re.search(rf"(?m)^  {key}:\s*\n((?:  - [^\n]+\n?)+)", source)
    return [item.strip()[2:] for item in match[1].splitlines()] if match else []


def files(project, *patterns, roots=("Assets", "Packages")):
    command = ["rg", "--files", "--hidden", "--no-ignore", "-0", *roots]
    for pattern in patterns:
        command.extend(("-g", pattern))
    result = subprocess.run(command, cwd=project, capture_output=True, check=False)
    if result.returncode not in (0, 1):
        raise ValueError(result.stderr.decode("utf-8", errors="replace"))
    return sorted(project / name.decode("utf-8") for name in result.stdout.split(b"\0") if name)


def head(root):
    result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, check=False,
                            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"})
    return result.stdout.decode("utf-8").strip() if result.returncode == 0 else None


def local_package(project, dependencies, package):
    binding = dependencies.get(package, "")
    return (project / "Packages" / binding[5:]).resolve() if binding.startswith("file:") else None


def read_shaders(project, hoshader, dependencies):
    shaders = {}
    for family, (package, path) in SHADERS.items():
        source = hoshader / path
        if not source.is_file() or not guid_for(source):
            raise ValueError(f"Missing shader or shader GUID: {source}")
        text = read(source)
        defaults = {}
        for prop in PROPERTIES:
            match = re.search(rf"(?m)\b{re.escape(prop)}\s*\([^\n]*?\)\s*=\s*({NUMBER})[ \t]*$", text)
            if match:
                defaults[prop] = float(match[1])
        bound_package = local_package(project, dependencies, package)
        bound_source = bound_package is not None and bound_package == (hoshader / Path(path).parts[0]).resolve()
        shaders[guid_for(source)] = {
            **asset_stamp(source), "family": family, "package": package, "manifestBinding": dependencies.get(package),
            "matchesLocalBinding": bound_source, "defaults": defaults, "screenVariant": PREVIEW in text,
        }
    return shaders


def read_materials(project, shaders):
    materials = {}
    for path in files(project, "*.mat"):
        identity = guid_for(path)
        if not identity:
            continue
        source = read(path)
        shader = field_guid(source, "m_Shader")
        props = {key: float(value) for key, value in re.findall(rf"(?m)^\s+- (_\w+): ({NUMBER})[ \t]*$", source)
                 if key in PROPERTIES}
        if identity in materials:
            raise ValueError(f"Duplicate material GUID {identity}, cannot resolve variants safely")
        materials[identity] = {
            "path": path, "shader": shader, "parent": field_guid(source, "m_Parent"), "properties": props,
            "customRenderQueue": int(field(source, "m_CustomRenderQueue", default="-1")),
            "disabledPasses": listed(source, "disabledShaderPasses"),
            "keywords": sorted(set(listed(source, "m_ValidKeywords")) | set(field(source, "m_ShaderKeywords", default="").split())),
            "locked": bool(field(source, "m_LockedProperties", default="") or listed(source, "m_LockedProperties")),
        }

    @lru_cache(maxsize=None)
    def resolve(identity, ancestors=()):
        item = materials[identity]
        parent = item["parent"]
        if parent in (*ancestors, identity):
            return item["shader"], item["properties"], [identity], "variant_cycle"
        if parent and parent not in materials:
            return item["shader"], item["properties"], [identity], "unresolved_parent"
        if parent:
            shader, props, chain, warning = resolve(parent, (*ancestors, identity))
            if shader and item["shader"] and shader != item["shader"]:
                return item["shader"], item["properties"], [*chain, identity], "variant_shader_mismatch"
            return item["shader"] or shader, props | item["properties"], [*chain, identity], warning
        return item["shader"], item["properties"], [identity], None

    for identity, record in materials.items():
        shader, values, chain, warning = resolve(identity)
        if shader in shaders or "_HoRPShadowMode" in values:
            yield identity, record, shaders.get(shader), values, chain, warning


def scan_assets(project, script_guids, camera_guid):
    nodes, scene_info = {}, {}
    declarations, profiles, property_overrides, stage, renderer_assets, camera_routes = [], [], [], [], [], []
    for path in files(project, "*.unity", "*.prefab", "*.fbx.meta", "*.pmx.meta"):
        source = read(path)
        guid = GUID.search(source) if path.suffix == ".meta" else None
        identity = guid[1] if guid else guid_for(path)
        if identity:
            nodes[identity] = set(GUID.findall(source)) - {identity}
        is_scene = path.suffix == ".unity"
        has_camera = camera_guid and camera_guid in source and path.suffix in {".unity", ".prefab"}
        has_declarations = any(guid in source for guid in script_guids)
        docs = list(yaml_documents(source)) if is_scene or has_camera or has_declarations else []
        if is_scene:
            scene_info[path.as_posix()] = scene_lights(docs, identity, set(GUID.findall(source)))
        if has_camera:
            objects = {local_id: block for kind, local_id, block in docs if kind == 1}
            for kind, local_id, block in docs:
                if kind != 114 or field_guid(block, "m_Script") != camera_guid:
                    continue
                game_object = objects.get(file_id(field(block, "m_GameObject")), "")
                camera_routes.append({"path": path.as_posix(), "fileId": local_id, "name": field(game_object, "m_Name"),
                                      "embeddedActive": field(game_object, "m_IsActive") == "1", "enabled": field(block, "m_Enabled") == "1",
                                      "rendererIndex": int(field(block, "m_RendererIndex", default="-1")),
                                      "cameraType": "Overlay" if field(block, "m_CameraType") == "1" else "Base",
                                      "renderShadows": field(block, "m_RenderShadows", default="1") == "1",
                                      "stackCameraFileIds": [file_id(value) for value in listed(block, "m_Cameras") if file_id(value)]})
        property_overrides.extend(
            {"path": path.as_posix(), "property": match[1].strip(), "line": source.count("\n", 0, match.start()) + 1}
            for match in PROPERTY_OVERRIDES.finditer(source)
        )
        for kind, local_id, block in docs if has_declarations else ():
            role = script_guids.get(field_guid(block, "m_Script")) if kind == 114 else None
            if role in {"castLight", "characterBounds", "rtLight", "rtReceiver", "objectGroup"}:
                tags = [int(value) for value in re.findall(r"(?m)^    tags: (\d+)$", block)]
                if role == "objectGroup" and not any(tag & 10 for tag in tags):
                    continue
                declarations.append({"path": path.as_posix(), "fileId": local_id, "role": role, "assetGuid": identity,
                                     "enabled": field(block, "m_Enabled") == "1", "captureTags": sorted(set(t for t in tags if t & 10)),
                                     "includeInMainShadow": field(block, "includeInMainShadow")})

    for path in files(project, "*.asset", "*.anim", "*.preset"):
        # Large import blobs are not Unity text serialization.
        with path.open("rb") as handle:
            if not handle.read(8).startswith(b"%YAML"):
                continue
        source = read(path)
        if path.suffix == ".asset" and re.search(r"(?m)^  m_RendererFeatures:", source):
            renderer_assets.append(path)
        if path.suffix in (".anim", ".preset"):
            property_overrides.extend(
                {"path": path.as_posix(), "property": match[1].strip(), "line": source.count("\n", 0, match.start()) + 1}
                for match in PROPERTY_OVERRIDES.finditer(source)
            )
        if not any(guid in source for guid, role in script_guids.items() if role.endswith("Volume")):
            continue
        for kind, local_id, block in yaml_documents(source):
            role = script_guids.get(field_guid(block, "m_Script")) if kind == 114 else None
            if role not in {"characterVolume", "rtVolume"}:
                continue
            parameters = {key: {"override": override == "1", "value": value.strip()}
                          for key, override, value in re.findall(
                              r"(?m)^  (\w+):\n    m_OverrideState: (\d+)\n    m_Value: ([^\n]+)", block)}
            profiles.append({**asset_stamp(path), "fileId": local_id, "role": role,
                             "active": field(block, "active") == "1", "parameters": parameters})

    for origin, paths in (
        ("project", files(project, "*.cs")),
        ("extensions_tools", files(EXTENSIONS, "*.cs", roots=("Tools~",))),
    ):
        for path in paths:
            matches = [{"line": line, "symbols": sorted(set(SYMBOLS.findall(code)))}
                       for line, code in enumerate(read(path).splitlines(), 1) if SYMBOLS.search(code)]
            if matches:
                stage.append({**asset_stamp(path), "origin": origin, "references": matches})

    scenes_by_guid = defaultdict(list)
    for name, summary in scene_info.items():
        pending = set(summary.pop("directReferences"))
        visited = set()
        while pending:
            reference = pending.pop()
            if reference in visited:
                continue
            visited.add(reference)
            pending.update(nodes.get(reference, set()) - visited)
        for identity in visited:
            scenes_by_guid[identity].append(name)
    return scene_info, scenes_by_guid, declarations, profiles, property_overrides, stage, renderer_assets, camera_routes


def scene_lights(docs, identity, references):
    direct_enabled = {local_id for kind, local_id, block in docs if kind == 1 and field(block, "m_IsActive") == "1"}
    counts = Counter()
    for kind, _, block in docs:
        if kind != 108 or field(block, "m_Enabled") != "1" or file_id(field(block, "m_GameObject")) not in direct_enabled:
            continue
        if field(block, "m_Lightmapping") == "2":
            continue
        shadow_type = field(block, "m_Type", indent=4)
        if shadow_type not in {"1", "2"} or float(field(block, "m_Strength", indent=4, default="0")) <= 0:
            continue
        if field(block, "m_Type") in {"0", "2"}:
            counts["shadowingPunctual"] += 1
        if field(block, "m_Type") == "1":
            counts["shadowingDirectional"] += 1
    return {"guid": identity, "directReferences": references,
            "shadowingPunctual": counts["shadowingPunctual"], "shadowingDirectional": counts["shadowingDirectional"]}


def plan_material(identity, record, shader, saved, chain, warning, variant_parents, materials_scenes, scenes):
    family = shader["family"] if shader else "UnknownCastScope"
    values = (shader["defaults"] if shader else {}) | saved
    previous = values.get("_HoRPShadowMode")
    receive = values.get("_ReceiveShadowStrength", 1) if family == "Hair" else values.get("_ReceiveShadows", 1)
    active = values.get("_UseShadow", 0) >= .5 and receive > (.5 if family in {"Cloth", "Environment"} else 0)
    intensity = values.get("_AdditionalLightStrength", 1) if family in {"Hair", "Effect"} else values.get("_AdditionalLightIntensity", 1)
    punctual = active and min(intensity, values.get("_AdditionalLightShadowStrength", 1)) > 0
    scope = "Full" if previous is not None and previous >= 1.5 else "Main" if previous is not None else None
    mode = values.get("_RenderMode" if family == "Effect" else "_SurfaceMode", 0)
    transparent = values.get("_Surface", 0) > .5 if family == "Environment" else mode >= 1.5
    surface = "Transparent" if transparent else "Cutout" if values.get("_AlphaClip", mode) >= .5 else "Opaque"
    queue = record["customRenderQueue"]
    special = []
    if transparent:
        if family == "Eye":
            special.append("transparent_eye")
        elif family == "Cloth":
            special.append("cloth_oit" if values.get("_UseHoOIT", 0) > .5 else "transparent_cloth_forward")
        else:
            special.append("transparent")
        if 0 <= queue <= 2500:
            special.append("transparent_in_opaque_queue")
    if family == "Effect" and values.get("_SurfaceEnabled", 1) <= .5:
        special.append("outline_only")
    if family in {"Skin", "Eye"} and "HoCharacterCapture" not in record["disabledPasses"]:
        special.append("capture_capable_needs_owner_scene_check")
    references = materials_scenes.get(identity, [])
    local_witnesses = [scene for scene in references if scenes[scene]["shadowingPunctual"]]

    if shader is None or shader["manifestBinding"] is None or not shader["matchesLocalBinding"]:
        action, why = "review_shader_binding", "No known active package binding for this serialized shader."
    elif "outline_only" in special:
        action, why = "hold_special_surface", "Only the Effect body pass currently has a managed preview."
    elif "transparent_in_opaque_queue" in special and active:
        action, why = "hold_special_surface", "This blended material draws in the opaque phase. Verify its early receiver, depth state and owner while preserving the saved queue."
    elif family == "Effect" and (mode < 0 or queue > 2500 or values.get("_ZWrite", 1) <= .5
                                or (values.get("_SrcBlend", 1), values.get("_DstBlend", 0), values.get("_BlendOp", 0)) != (1, 0, 0)):
        action, why = "hold_special_surface", "Effect body render state does not satisfy the managed opaque or cutout gate."
    elif previous is None or not active:
        action, why = "keep_inactive", "Current shadow reception is disabled or has no serialized Cast Scope."
    elif warning or len(chain) > 1 or identity in variant_parents or record["locked"]:
        action, why = "hold_variant", "Verify actual Unity variant overrides and locks before changing a parent or child."
    elif transparent:
        action, why = "hold_special_surface", "Verify the actual transparent or OIT receiver domain before opting into screen shadows."
    elif scope == "Full":
        action, why = "keep_full", "Full is already serialized. Verify the supported screen consumer before enabling it."
    elif punctual and local_witnesses:
        action, why = "review_full_candidate", "Old Main can receive native punctual shadows. Confirm this material receives the witness light before changing scope."
    elif punctual:
        action, why = "review_main_scope", "Old Main can receive native punctual shadows. No saved scene reference with an embedded shadowing punctual light was found."
    else:
        action, why = "keep_main", "Saved additional-light intensity or shadow strength is zero."

    return {**asset_stamp(record["path"]), "family": family, "sourceShaderGuid": record["shader"], "parentGuid": record["parent"],
            "variantChain": chain, "variantWarning": warning, "scope": scope, "effectiveShadowReceive": active,
            "lightingModel": ("ThreeBand" if values.get("_ShadowMode", 0) >= .5 else "Native") if family in {"Skin", "Eye"} else None,
            "canReceivePunctual": punctual, "surface": surface, "customRenderQueue": queue, "specialDomains": special,
            "previewKeywordSaved": PREVIEW in record["keywords"], "nativeReceiveOffKeywordSaved": "_RECEIVE_SHADOWS_OFF" in record["keywords"],
            "disabledPasses": record["disabledPasses"], "saved": record["properties"], "effective": values,
            "sceneReferences": references, "scenesWithPunctualEvidence": local_witnesses, "action": action, "reason": why,
            "proposedAfterProof": {"_HoRPShadowMode": 2} if action == "review_full_candidate" else {}}


def renderer_plan(path, script_guids):
    docs = {local_id: block for kind, local_id, block in yaml_documents(read(path)) if kind == 114}
    root = next(block for block in docs.values() if field(block, "m_RendererFeatures") is not None)
    features = []
    actions = {
        "unified": ("pilot", "Use once on a pilot renderer. Keep RT off by default and verify each enabled transparency domain."),
        "cast": ("defer_retirement", "Keep until material and special-domain comparisons pass. Retire duplicate atlas and old artistic light scope at cutover."),
        "character": ("move_managed_atlas_then_disable", "Transfer the atlas settings to Ho Shadows and disable the standalone producer on the candidate renderer."),
        "rt": ("move_opt_in_then_disable", "The managed provider is off by default. Preserve intentional shots and resource references before disabling this legacy producer."),
        "characterSpecialization": ("retain_and_review_capture", "Keep capture behavior. Enable new capture shadow inputs only on a validated configuration."),
        "oit": ("retain_and_review_oit", "Keep transparency composition. Validate the configured shadow layers and overflow before changing receive behavior."),
    }
    for rank, value in enumerate(listed(root, "m_RendererFeatures")):
        local_id = file_id(value)
        block = docs.get(local_id, "")
        role = script_guids.get(field_guid(block, "m_Script"))
        action, reason = actions.get(role, ("retain", "No planned change. Preserve the current producer or renderer service."))
        features.append({"index": rank, "fileId": local_id, "name": field(block, "m_Name"), "role": role,
                         "scriptGuid": field_guid(block, "m_Script"), "active": field(block, "m_Active") == "1",
                         "settings": scalar_fields(block, 4) | {key: field(block, key) for key in
                             ("rayTracedShadows", "earlyTransparentShadows", "transparentEyeShadows", "captureShadowInputsEnabled", "additionalLightBudget")
                             if field(block, key) is not None}, "action": action, "reason": reason})
    return {**asset_stamp(path), "renderingMode": field(root, "m_RenderingMode"),
            "transparentReceiveShadows": field(root, "m_ShadowTransparentReceive"), "features": features,
            "pilotAction": "reuse_unified_feature" if any(f["role"] == "unified" for f in features) else "add_unified_to_isolated_renderer",
            "ordering": "Keep Ho-RT before managed pre-cull and Character, Object, Geometry and Surface producers before Ho Shadows. Verify actual pass events."}


def build_plan(project, hoshader):
    manifest = project / "Packages/manifest.json"
    dependencies = json.loads(read(manifest))["dependencies"]
    script_guids = {guid_for(EXTENSIONS / path): role for role, path in SCRIPT_PATHS.items()}
    if None in script_guids:
        raise ValueError("Extensions source is missing a required script GUID")
    shaders = read_shaders(project, hoshader, dependencies)
    urp = local_package(project, dependencies, "com.unity.render-pipelines.universal")
    camera_guid = guid_for(urp / "Runtime/UniversalAdditionalCameraData.cs") if urp else None
    scenes, usage, declarations, volumes, overrides, stages, renderer_assets, camera_routes = scan_assets(project, script_guids, camera_guid)
    inputs = list(read_materials(project, shaders))
    variant_parents = {parent for _, _, _, _, chain, _ in inputs for parent in chain[:-1]}
    materials = [plan_material(identity, data, shader, saved, chain, error, variant_parents, usage, scenes)
                 for identity, data, shader, saved, chain, error in inputs]
    for volume in volumes:
        volume["sceneReferences"] = usage.get(volume["guid"], [])
        enable = volume["parameters"].get("enable", {})
        if volume["role"] == "characterVolume":
            volume["action"] = "retain_character_authoring"
            volume["reason"] = "Preserve Character enable, local resolution and bounds behavior. Retire PCSS fields only after cutover."
        elif Path(volume["path"]).name == "DefaultVolumeProfile.asset":
            volume["action"] = "keep_managed_rt_master_off"
            volume["reason"] = "A default profile may request RT only where a renderer explicitly opts in."
        elif volume["active"] and enable.get("override") and enable.get("value") == "1":
            volume["action"] = "review_named_rt_opt_in"
            volume["reason"] = "Preserve intentional RT shots in explicit opt-in configurations after comparison."
        else:
            volume["action"] = "retain_rt_profile"
            volume["reason"] = "No enabled RT override identified in this profile."
    declaration_actions = {
        "castLight": ("review_artistic_main_shadow", "Replacing a matched light cannot transport the old additional-light multiplier on main."),
        "characterBounds": ("retain_character_bounds", "Bounds, receivers and blend edges remain authored on the character."),
        "rtLight": ("review_rt_opt_in", "Retain authored per-light requests only in explicit RT configurations."),
        "rtReceiver": ("retain_rt_receiver", "Registration is an authoring input for explicit RT shots and does not enable default RT."),
        "objectGroup": ("retain_capture_identity", "Face/Eye tags are part of the Capture owner contract."),
    }
    for item in declarations:
        item["sceneReferences"] = usage.get(item["assetGuid"], [])
        item["action"], item["reason"] = declaration_actions[item["role"]]
    overridden_assets = {item["path"] for item in overrides}
    override_hashes = {path: sha256(Path(path).read_bytes()).hexdigest() for path in overridden_assets}
    for item in overrides:
        item["sha256"] = override_hashes[item["path"]]
    rp_asset = project / "Assets/Settings/PC_RPAsset.asset"
    pc_renderer = project / "Assets/Settings/PC_Renderer.asset"
    graphics = project / "ProjectSettings/GraphicsSettings.asset"
    quality = project / "ProjectSettings/QualitySettings.asset"
    global_settings = project / "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"
    rp = read(rp_asset)
    return {
        "schema": 1, "mode": "read-only", "project": project.as_posix(),
        "sourceHeads": {"product": head(project), "extensions": head(EXTENSIONS), "hoshader": head(hoshader),
                        "urp": head(urp) if urp and urp.is_dir() else None},
        "packageBindings": {package: dependencies.get(package) for package in sorted({
            "com.unity.render-pipelines.universal", "jp.lilxyzw.liltoon.urp.extensions",
            *(package for package, _ in SHADERS.values())
        })},
        "cutover": {
            "status": "hold_for_runtime_acceptance", "projectEditsAllowedByThisPlan": False, "proposedDefaultRayTracing": False,
            "opaquePilot": "Test Main and Full on an isolated renderer. Require paired production pixels before writing scope or screen keywords.",
            "character": "Compare the managed Character atlas and screen receiver on the saved camera before removing the legacy renderer route.",
            "specialDomains": "Verify ordinary early and late transparent draws, Face/Eye Capture, and Cloth OIT with actual coverage, depth and owner data.",
            "stageTools": "Replace legacy feature creation/queries and validation of old globals before removing old types.",
            "assetWriteGuard": "Recheck per-file SHA-256 and GUID before any later migration. Do not infer scene usage from filenames.",
        },
        "manifest": asset_stamp(manifest),
        "packageLock": asset_stamp(project / "Packages/packages-lock.json"),
        "pipelineSelection": {
            "graphics": {**asset_stamp(graphics), "defaultPipeline": field_guid(read(graphics), "m_CustomRenderPipeline")},
            "quality": {**asset_stamp(quality), "currentQuality": field(read(quality), "m_CurrentQuality"),
                        "qualityPipelines": sorted(set(re.findall(r"(?m)^ +customRenderPipeline:.*\bguid: ([a-f0-9]{32})", read(quality))))},
            "globalSettings": asset_stamp(global_settings),
        },
        "nativePipeline": {**asset_stamp(rp_asset), "settings": {key: field(rp, key) for key in (
            "m_MainLightShadowsSupported", "m_MainLightShadowmapResolution", "m_AdditionalLightsRenderingMode",
            "m_AdditionalLightShadowsSupported", "m_AdditionalLightsShadowmapResolution", "m_ShadowDistance", "m_ShadowCascadeCount", "m_MSAA",
            "m_DefaultRendererIndex")}, "rendererReferences": listed(rp, "m_RendererDataList")},
        "renderer": renderer_plan(pc_renderer, script_guids),
        "otherRendererAssets": [renderer_plan(path, script_guids) for path in renderer_assets if path != pc_renderer],
        "cameraRendererRoutes": camera_routes,
        "shaders": shaders, "settingsAndComponents": declarations, "volumeProfiles": volumes,
        "sceneCandidates": {path: {**asset_stamp(Path(path)), **summary, "reviewSecondDirectional": summary["shadowingDirectional"] > 1}
                            for path, summary in scenes.items()
                            if summary["shadowingPunctual"] or summary["shadowingDirectional"] > 1},
        "materialSummary": {family: dict(Counter(item["action"] for item in materials if item["family"] == family))
                            for family in sorted({item["family"] for item in materials})},
        "materialRecords": materials, "serializedOverridesAndPresets": overrides, "scriptReferences": stages,
        "limits": [
            "Unsaved edits in an open Unity Editor are outside this inventory.",
            "Scene links follow saved GUID references through prefabs and FBX/PMX remap metadata. This does not resolve every imported slot, procedural assignment or active timeline.",
            "Light witnesses count embedded scene Lights with enabled GameObjects. Ancestor activation, prefab overrides, camera culling and light linking require a render witness.",
            "Camera routes are saved URP camera components in scenes and prefabs. Effective active state and stack routing still require a loaded scene.",
            "Material variants are advisory until evaluated through Unity. A capture-capable pass is not proof of a registered capture receiver.",
            "A worktree can contain untracked authored files and edited packages. The serialized values and hashes here are from disk, alongside the current Git HEADs.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path, help="Unity project to inspect")
    parser.add_argument("--hoshader", type=Path, default=EXTENSIONS.parent / "HoShader", help="HoShader checkout matching the project")
    args = parser.parse_args()
    try:
        report = build_plan(args.project.resolve(strict=True), args.hoshader.resolve(strict=True))
    except (OSError, ValueError, KeyError, StopIteration) as error:
        parser.exit(2, f"Cannot plan Ho Shadows migration: {error}\n")
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    print(json.dumps(report, ensure_ascii=False, sort_keys=True, indent=2))


if __name__ == "__main__":
    main()
