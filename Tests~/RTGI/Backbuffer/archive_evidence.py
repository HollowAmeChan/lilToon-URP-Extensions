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

previous = HERE.parent/'AutomaticCamera'
index = read(HERE/'source-index.json') if (HERE/'source-index.json').exists() else {}
for digest, item in read(previous/'source-index.json').items():
    entry = dict(item)
    if 'path' in entry: entry['path'] = os.path.relpath(previous/entry['path'], HERE).replace('\\', '/')
    index.setdefault(digest, entry)
runs = ['rejected-missing-module/run.json', 'rg-run.json', 'compat-run.json']
used = {}; logs = {}
for name in runs:
    run = read(HERE/name)
    source_dir = HERE/('rejected-missing-module/source' if name.startswith('rejected') else 'rg-source' if name=='rg-run.json' else 'player-original/source')
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
            if key not in entry: continue
            source = Path(entry[key]); value = source.read_bytes()
            assert sha(value) == entry[key+'Sha256']
            target = HERE/'logs'/source.name; target.parent.mkdir(exist_ok=True); target.write_bytes(value)
            logs[str(source)] = {'path': target.relative_to(HERE).as_posix(), 'sha256': sha(value)}
write(HERE/'source-index.json', {k:index[k] for k in sorted(set(used.values()))})
write(HERE/'source-files.json', used); write(HERE/'log-index.json', logs)
# Both final Player outputs remain independently available.
payloads = {}
for mode, folder, manifest in [('rg','PlayerGISkinnedBackbuffer','player-results/rg-payload.json'), ('compat','PlayerGISkinnedCompatibilityBackbuffer','player-results/compat-payload.json')]:
    items = read(HERE/manifest); output = ROOT/'.codex-research/RTGIValidation'/folder
    for item in items:
        value = (output/item['path']).read_bytes(); assert len(value) == item['bytes'] and sha(value) == item['sha256']
    assert not any(Path(i['path']).name == 'HoGIGpuProbe.dll' for i in items)
    payloads[mode] = {'manifest':manifest,'sha256':sha((HERE/manifest).read_bytes()),'files':len(items),'verifiedAfterVisibleTest':True}
write(HERE/'payload-verification.json', payloads)
# Only exact, verified redundant source copies within this evidence folder.
for relative in ['player-original/source', 'rejected-missing-module/source', 'rg-source']:
    target = (HERE/relative).resolve(); assert target.is_relative_to(HERE) and target.name in ['source','rg-source']
    if target.exists(): shutil.rmtree(target)
print('Frozen',len(set(used.values())),'source objects and',len(logs),'complete logs; both final payloads verified')
