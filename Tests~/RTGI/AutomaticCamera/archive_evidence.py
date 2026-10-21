"""Freeze exact source bytes and full logs; reuse already archived source objects."""
import hashlib
import json
import os
import shutil
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(b): return hashlib.sha256(b).hexdigest().upper()
def write(p, v): p.write_text(json.dumps(v, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

previous = HERE.parent/'PrecisionQualification'
index = read(HERE/'source-index.json') if (HERE/'source-index.json').exists() else {}
for digest, item in read(previous/'source-index.json').items():
    entry = dict(item)
    if 'path' in entry: entry['path'] = os.path.relpath(previous/entry['path'], HERE).replace('\\', '/')
    index.setdefault(digest, entry)
runs = ['rejected-uncreated-target/run.json', 'player-run.json', 'compat-visible-run.json']
used = {}; logs = {}
for name in runs:
    run = read(HERE/name)
    source_dir = HERE/('rejected-uncreated-target/source' if name.startswith('rejected') else 'player-original/source')
    for item in run['sources']:
        digest = item['sha256']; path = item['path']; used[name+':'+path] = digest
        if digest in index: continue
        source = source_dir/path
        if not source.exists(): source = ROOT/path
        value = source.read_bytes(); assert sha(value) == digest, path
        target = HERE/'source-blobs'/(digest[:16]+'-'+source.name)
        target.parent.mkdir(exist_ok=True); target.write_bytes(value)
        index[digest] = {'path': target.relative_to(HERE).as_posix(), 'bytes': len(value)}
    for entry in run['entries']:
        for key in ['buildLog', 'log']:
            source = Path(entry[key]); value = source.read_bytes()
            assert sha(value) == entry[key+'Sha256']
            target = HERE/'logs'/source.name; target.parent.mkdir(exist_ok=True); target.write_bytes(value)
            logs[str(source)] = {'path': target.relative_to(HERE).as_posix(), 'sha256': sha(value)}
visible = read(HERE/'visible-run.json')
value = (HERE/'visible-results/rg/player.log').read_bytes(); assert sha(value) == visible['logSha256']
logs['visible-rg'] = {'path': 'visible-results/rg/player.log', 'sha256': sha(value)}
write(HERE/'source-index.json', {k:index[k] for k in sorted(set(used.values()))})
write(HERE/'source-files.json', used); write(HERE/'log-index.json', logs)
# The original first executable was overwritten by the second build. Preserve its
# manifest, but verify only the still-existing final RG and compatibility payloads.
payloads = {}
for mode, folder, manifest in [('rg','PlayerGISkinnedAutomatic','player-results/rg-payload.json'), ('compat','PlayerGISkinnedCompatibilityAutomatic','visible-results/compat-payload.json')]:
    items = read(HERE/manifest); output = ROOT/'.codex-research/RTGIValidation'/folder
    for item in items:
        value = (output/item['path']).read_bytes(); assert len(value) == item['bytes'] and sha(value) == item['sha256']
    assert not any(Path(i['path']).name == 'HoGIGpuProbe.dll' for i in items)
    payloads[mode] = {'manifest':manifest,'sha256':sha((HERE/manifest).read_bytes()),'files':len(items),'verifiedAfterVisibleTest':True}
write(HERE/'payload-verification.json', payloads)
# Only exact, verified redundant source copies within this evidence folder.
for relative in ['player-original/source', 'rejected-uncreated-target/source']:
    target = (HERE/relative).resolve(); assert target.is_relative_to(HERE) and target.name == 'source'
    if target.exists(): shutil.rmtree(target)
print('Frozen',len(set(used.values())),'source objects and',len(logs),'complete logs; both final payloads verified')
