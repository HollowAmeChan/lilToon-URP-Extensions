#!/usr/bin/env python3
"""Read-only validation of pipeline RTGI libraries and the HoPass wire-encoding mirror."""
from pathlib import Path
import argparse
import hashlib
import json
import re

HERE=Path(__file__).resolve().parent
RTGI_HEADERS=(
    "HoRTGIApplicationBridge.hlsl","HoRTGIGrid.hlsl","HoRTGINativeSceneQuery.hlsl",
    "HoRTGINrdSignal.hlsl","HoRTGIResampling.hlsl","HoRTGISecondaryData.hlsl",
    "HoRTGISecondaryInputs.hlsl","HoRTGISecondaryMaterial.hlsl","HoRTGISecondaryNormals.hlsl",
    "HoRTGISecondarySurface.hlsl","HoRTGISecondaryTension.hlsl","HoRTGIShadingCommon.hlsl")
RTGI_ENTRIES=("HoRTGI.compute","HoRTGI.raytrace","HoRTGIComposite.shader","HoRTGIDebug.shader","HoRTGISecondaryMaterial.shader")
RECEIVER_HEADERS=("HoShadowCastShaderContract.hlsl","HoShadowCastSampling.hlsl","HoRTShadowSampling.hlsl","HoRendererIdentity.hlsl","HoCharacterShadowSampling.hlsl")
RECEIVER_ORIGINS={
    "HoShadowCastShaderContract.hlsl":"Runtime/ShadowCast/Shaders",
    "HoShadowCastSampling.hlsl":"Runtime/ShadowCast/Shaders",
    "HoRTShadowSampling.hlsl":"Runtime/RTShadow/Shaders",
    "HoRendererIdentity.hlsl":"Runtime/ObjectBuffer/Shaders",
    "HoCharacterShadowSampling.hlsl":"Runtime/CharacterShadow/Shaders"}
COMMON_RECEIVERS=("HoShadowCastShaderContract.hlsl","HoRTShadowSampling.hlsl","HoRendererIdentity.hlsl")
SOURCE_SUFFIXES={".hlsl",".hlsli",".cginc",".compute",".raytrace",".shader",".h"}
ENGINE_INCLUDES={"UnityRayQuery.cginc","UnityRayTracingMeshUtils.cginc","UnityCG.cginc","UnityShaderVariables.cginc",
                 "HLSLSupport.cginc","Lighting.cginc","AutoLight.cginc","UnityInstancing.cginc",
                 "UnityPBSLighting.cginc","UnityStandardUtils.cginc","UnityStandardBRDF.cginc"}
HOPASS="ShaderLibrary/HoPass/Encoding/HoBufferEncoding.hlsl"

def uncomment(text):
    return re.sub(r"/\*.*?\*/|//[^\r\n]*","",text,flags=re.S)

def includes(text):
    return re.findall(r'^\s*#\s*include(?:_with_pragmas)?\s+"([^"]+)"',uncomment(text),flags=re.M)

def resolve_include(root,path,name,package):
    """External Unity packages/engine builtins are ownership boundaries; local paths stay in root."""
    if name.startswith("Packages/"):
        pieces=name.split("/",2)
        if len(pieces)!=3:raise ValueError("malformed package include "+name)
        if pieces[1]!=package:return None
        target=root/pieces[2]
    elif name in ENGINE_INCLUDES:return None
    else:
        if "\\" in name or ":" in name or Path(name).is_absolute():raise ValueError("unsafe include "+name)
        target=path.parent/name
    target=target.resolve()
    if not target.is_relative_to(root.resolve()):raise ValueError("include escapes pipeline package: "+name)
    return target

def active_sources(root):
    # Tests~/ historical snapshots and Documentation~ are deliberately not active include roots.
    for area in ("Runtime","Editor","Tests/Editor","ShaderLibrary"):
        base=root/area
        if base.is_dir():
            yield from (p for p in base.rglob("*") if p.is_file() and p.suffix in SOURCE_SUFFIXES)

def library_metadata_errors(root):
    library=root/"ShaderLibrary";errors=[];identities={}
    if not library.is_dir():return ["missing ShaderLibrary"]
    assets=[library]+[p for p in library.rglob("*") if p.is_dir() or p.suffix in SOURCE_SUFFIXES]
    for asset in assets:
        meta=Path(str(asset)+".meta")
        if not meta.is_file():errors.append("missing library meta "+meta.relative_to(root).as_posix())
    for meta in [Path(str(library)+".meta")]+list(library.rglob("*.meta")):
        if not meta.is_file():continue
        if not Path(str(meta)[:-5]).exists():errors.append("orphan library meta "+meta.relative_to(root).as_posix())
        found=re.search(r"(?m)^guid:\s*([0-9a-f]{32})\s*$",meta.read_text(encoding="utf-8-sig"))
        if not found:errors.append("invalid library GUID "+meta.relative_to(root).as_posix());continue
        identities.setdefault(found[1],[]).append(meta.resolve())
    for area in ("Runtime","Editor","Tests"):
        for meta in (root/area).rglob("*.meta"):
            found=re.search(r"(?m)^guid:\s*([0-9a-f]{32})\s*$",meta.read_text(encoding="utf-8-sig"))
            if found and found[1] in identities:identities[found[1]].append(meta.resolve())
    for guid,paths in identities.items():
        if len(set(paths))>1:
            errors.append("duplicate library GUID "+guid+": "+", ".join(p.relative_to(root.resolve()).as_posix() for p in paths))
    return errors

def common_library_errors(root,hoshader_root=None):
    errors=[];local_count=0;authority_count=0
    try:
        contract=json.loads((root/"Tools~/ShaderLibrary/contract.json").read_text(encoding="utf-8-sig"))
        entries=contract["headers"]
        expected={HOPASS:"ShaderLibrary~/HoPass/Encoding/HoBufferEncoding.hlsl"}
        expected.update({"ShaderLibrary/Shadow/Receiver/"+name:"ShaderLibrary~/Shadow/Receiver/"+name for name in COMMON_RECEIVERS})
        if contract.get("schema")!=1 or len(entries)!=4 or {e["local"] for e in entries}!=set(expected):
            raise ValueError("common-library contract must register all four shared headers")
        identities=[e["id"] for e in entries]
        if len(set(identities))!=4 or not all(identities):raise ValueError("shared header IDs must be unique")
        for entry in entries:
            local=entry["local"];canonical=entry["authority"];digest=entry["sha256"]
            if canonical!=expected[local]:raise ValueError("unexpected common-library authority "+canonical)
            if not re.fullmatch("[0-9a-f]{64}",digest):raise ValueError("invalid common-library SHA256 "+local)
            try:
                actual=(root/local).read_bytes()
                if b"\r" in actual:errors.append("local common-library source requires canonical LF: "+local)
                if hashlib.sha256(actual).hexdigest()!=digest:errors.append("local common-library hash drift: "+local)
                local_count+=1
            except OSError as error:errors.append("local common-library check: "+str(error));actual=None
            if hoshader_root is not None:
                try:
                    authority=(hoshader_root.resolve()/canonical).read_bytes()
                    if b"\r" in authority:errors.append("HoShader authority requires canonical LF: "+canonical)
                    if hashlib.sha256(authority).hexdigest()!=digest:errors.append("HoShader authority hash drift: "+canonical)
                    if actual is not None and actual!=authority:errors.append("HoShader authority differs: "+local)
                    authority_count+=1
                except OSError as error:errors.append("HoShader authority check: "+str(error))
    except (OSError,ValueError,KeyError,TypeError) as error:errors.append("common-library contract: "+str(error))
    return errors,local_count,authority_count

def check(root,hoshader_root=None):
    root=root.resolve();errors=[];visited=set();external=set();edges=0
    try:package=json.loads((root/"package.json").read_text(encoding="utf-8-sig"))["name"]
    except (OSError,ValueError,KeyError) as error:
        return {"passed":False,"errors":["package identity: "+str(error)]}
    library=root/"ShaderLibrary";old=root/"Runtime/RTGI/Shaders"
    retired_receiver={(root/directory/name).resolve() for name,directory in RECEIVER_ORIGINS.items()}
    def retired(target):return (target.parent==old.resolve() and target.suffix==".hlsl") or target in retired_receiver
    for name in RTGI_HEADERS:
        if not (library/"RTGI"/name).is_file():errors.append("missing RTGI library header ShaderLibrary/RTGI/"+name)
    for pattern in ("*.hlsl","*.hlsl.meta"):
        for path in old.glob(pattern):errors.append("retired RTGI header remains "+path.relative_to(root).as_posix())
    for name in RTGI_ENTRIES:
        if not (old/name).is_file():errors.append("missing Runtime RTGI entry "+name)
        if (library/"RTGI"/name).exists():errors.append("RTGI entry belongs in Runtime, not ShaderLibrary: "+name)
    for name in RECEIVER_HEADERS:
        if not (library/"Shadow/Receiver"/name).is_file():errors.append("missing Receiver library header "+name)
        source=root/RECEIVER_ORIGINS[name]/name
        if source.exists() or Path(str(source)+".meta").exists():errors.append("retired Receiver header remains "+source.relative_to(root).as_posix())
    sources=sorted(set(active_sources(root)))
    for path in library.rglob("*"):
        if path.is_file() and path.suffix in SOURCE_SUFFIXES and b"\r" in path.read_bytes():
            errors.append("library requires canonical LF: "+path.relative_to(root).as_posix())
    def target_for(path,name):
        nonlocal edges
        edges+=1
        try:target=resolve_include(root,path,name,package)
        except ValueError as error:errors.append(path.relative_to(root).as_posix()+": "+str(error));return None
        if target is None:external.add(name);return None
        if retired(target):
            errors.append(path.relative_to(root).as_posix()+": retired library include "+name)
        if not target.is_file():
            errors.append(path.relative_to(root).as_posix()+": missing include "+name);return None
        return target
    def visit(path):
        path=path.resolve()
        if path in visited:return
        visited.add(path)
        for name in includes(path.read_text(encoding="utf-8-sig")):
            target=target_for(path,name)
            if target is None:continue
            # SDK conditionals and their private search paths belong to the vendored distribution.
            # Validate the edge into it, while keeping its own preprocessor closure out of this tool.
            if "ThirdParty" in target.relative_to(root).parts:continue
            visit(target)
    # Scan active includes for retired paths everywhere; close only RTGI/library dependency graphs.
    for path in sources:
        for name in includes(path.read_text(encoding="utf-8-sig")):
            try:target=resolve_include(root,path,name,package)
            except ValueError:
                # Scoped graph errors are emitted by visit(); unrelated subsystems stay out of scope.
                continue
            if target is not None and retired(target):
                errors.append(path.relative_to(root).as_posix()+": retired library include "+name)
        if path.is_relative_to(library) or path.is_relative_to(root/"Runtime/RTGI") or path.is_relative_to(root/"Editor/RTGI"):
            visit(path)
    errors.extend(library_metadata_errors(root))
    common_errors,local_count,authority_count=common_library_errors(root,hoshader_root)
    errors.extend(common_errors)
    mirror_checked=authority_count==4
    return {"passed":not errors,"rtgiHeaders":len(RTGI_HEADERS),
            "libraryHeaders":sum(1 for p in library.rglob("*") if p.is_file() and p.suffix in SOURCE_SUFFIXES),
            "includeClosureFiles":len(visited),"includeEdges":edges,"externalIncludes":len(external),
            "receiverHeaders":len(RECEIVER_HEADERS),"runtimeRtgiEntries":len(RTGI_ENTRIES),
            "localCommonHeadersChecked":local_count,"hoShaderAuthoritiesChecked":authority_count,"hoPassAuthorityChecked":mirror_checked,"errors":sorted(set(errors))}

def main():
    cli=argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--root",type=Path,default=HERE.parents[1])
    cli.add_argument("--hoshader-root",type=Path)
    args=cli.parse_args()
    try:result=check(args.root,args.hoshader_root)
    except (OSError,ValueError,UnicodeError) as error:result={"passed":False,"errors":[str(error)]}
    print(json.dumps(result,ensure_ascii=False,indent=2));return 0 if result["passed"] else 1
if __name__=="__main__":raise SystemExit(main())
