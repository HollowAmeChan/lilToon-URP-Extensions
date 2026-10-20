"""Copy the already isolated castle dependency closure, source side read-only."""
from pathlib import Path
from urllib.parse import unquote
import hashlib
import json
import re
import shutil

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
SOURCE=ROOT/'.codex-research/RTInputValidation'
TARGET=ROOT/'.codex-research/RTGIValidation'
ASSETS=SOURCE/'Assets'
scene=ASSETS/'mmd场景测试/血月古堡/血月古堡.unity'
assert scene.is_file()
guid_pattern=re.compile(r'guid:\s*([a-fA-F0-9]{32})')
guid_map={}
for meta in ASSETS.rglob('*.meta'):
    match=re.search(r'^guid:\s*([a-fA-F0-9]{32})',meta.read_text(encoding='utf-8-sig',errors='replace'),re.M)
    if match:guid_map[match[1].lower()]=Path(str(meta)[:-5])
seen=set();queue=[scene];external=set();records=[]
def add(path):
    path=path.resolve()
    assert path.is_relative_to(ASSETS.resolve()),path
    if path.is_file() and path not in seen:queue.append(path)
while queue:
    asset=queue.pop().resolve()
    if asset in seen:continue
    seen.add(asset)
    if not asset.is_file():continue
    texts=[]
    if asset.suffix.lower() in ('.unity','.asset','.mat','.prefab','.gltf','.shader','.json'):
        texts.append(asset.read_text(encoding='utf-8-sig',errors='replace'))
    meta=Path(str(asset)+'.meta')
    if meta.is_file():texts.append(meta.read_text(encoding='utf-8-sig',errors='replace'));seen.add(meta)
    for text in texts:
        for guid in guid_pattern.findall(text):
            linked=guid_map.get(guid.lower())
            if linked is None:external.add(guid.lower())
            elif linked.is_file():add(linked)
    if asset.suffix.lower()=='.gltf':
        gltf=json.loads(texts[0])
        for entry in gltf.get('buffers',[])+gltf.get('images',[]):
            uri=entry.get('uri','')
            if uri and not uri.startswith('data:'):add(asset.parent/unquote(uri))
    # Folder GUIDs are also part of the source import layout.
    parent=asset.parent
    while parent!=ASSETS:
        folder_meta=Path(str(parent)+'.meta')
        if folder_meta.is_file():seen.add(folder_meta)
        parent=parent.parent
for source in sorted(seen):
    if not source.is_file():continue
    relative=source.relative_to(SOURCE.resolve());destination=TARGET/relative
    before=hashlib.sha256(source.read_bytes()).hexdigest().upper()
    if destination.exists():assert hashlib.sha256(destination.read_bytes()).hexdigest().upper()==before, f'Existing GI file differs: {destination}'
    else:destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,destination)
    after=hashlib.sha256(source.read_bytes()).hexdigest().upper();assert before==after
    records.append({'path':str(relative).replace('\\','/'),'sha256':before,'bytes':source.stat().st_size})
package=SOURCE/'Packages/org.khronos.unitygltf';destination=TARGET/'Packages/org.khronos.unitygltf'
if destination.exists():
    for file in package.rglob('*'):
        if not file.is_file() or any(part in ('.git','Library','Temp','obj','bin') for part in file.relative_to(package).parts):continue
        target=destination/file.relative_to(package);assert target.is_file() and target.read_bytes()==file.read_bytes(),f'Existing private GLTF package differs: {target}'
else:shutil.copytree(package,destination,ignore=shutil.ignore_patterns('.git','Library','Temp','obj','bin'))
package_records={str(p.relative_to(destination)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest().upper() for p in sorted(destination.rglob('*')) if p.is_file()}
manifest_path=TARGET/'Packages/manifest.json';manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
original=manifest_path.read_text(encoding='utf-8-sig')
manifest['dependencies']['org.khronos.unitygltf']='file:org.khronos.unitygltf'
manifest['dependencies']['jp.lilxyzw.liltoon.unitygltf.extensions']='file:D:/Unity_Fork/lilToon-UnityGLTF-Extensions'
manifest_path.write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
(HERE/'dependencies.json').write_text(json.dumps({'source':str(SOURCE),'target':str(TARGET),'scene':str(scene.relative_to(SOURCE)),'copiedFiles':records,'externalOrBuiltinGuids':sorted(external),'gltfPackageFiles':package_records,'previousManifest':original},indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print('Copied',len(records),'dependency files,',sum(x['bytes'] for x in records),'bytes and private UnityGLTF package; source unchanged')
