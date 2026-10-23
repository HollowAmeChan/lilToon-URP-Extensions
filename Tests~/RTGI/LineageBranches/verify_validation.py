"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import lzma
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, read

import numpy as np
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
def read(p): return json.loads(raw(p).decode('utf-8-sig'))
def sha(b): return hashlib.sha256(b).hexdigest().upper()
index = read(HERE/'source-index.json')
objects = sorted({v['gitBlob'] for v in index.values() if 'gitBlob' in v})
data = subprocess.check_output(['git','cat-file','--batch'],input=('\n'.join(objects)+'\n').encode(),cwd=ROOT)
at=0; blobs={}
for oid in objects:
    end=data.index(b'\n',at); size=int(data[at:end].split()[2]); at=end+1; blobs[oid]=data[at:at+size]; at+=size+1
trees={}
for rev in {v['gitRevision'] for v in index.values() if 'gitRevision' in v}:
    trees[rev]={}
    for entry in subprocess.check_output(['git','ls-tree','-r','-z',rev],cwd=ROOT).split(b'\0'):
        if entry:
            meta,name=entry.split(b'\t',1); trees[rev][name.decode()]=meta.split()[2].decode()
for digest,item in index.items():
    if 'gitBlob' in item:
        assert trees[item['gitRevision']][item['gitPath']]==item['gitBlob']; value=blobs[item['gitBlob']]
        if item['lineEndings']=='lf': value=value.replace(b'\r\n',b'\n')
        if item['lineEndings']=='crlf': value=value.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n')
    else: value=(HERE/item['path']).read_bytes()
    assert sha(value)==digest and len(value)==item['bytes']
for digest in read(HERE/'source-files.json').values(): assert digest in index
logs=read(HERE/'log-index.json')
for item in logs.values(): assert sha((HERE/item['path']).read_bytes())==item['sha256']




archives=read(HERE/'capture-archives.json')
for name,item in archives.items():
    b=(HERE/item['path']).read_bytes();v=lzma.decompress(b) if item.get('codec')=='xz' else gzip.decompress(b);assert sha(b)==item['archiveSha256'] and len(b)==item['archiveBytes'] and sha(v)==item['rawSha256'] and len(v)==item['rawBytes']
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS'
for label in ['qualification','matrix','compat']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==43 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['fresh'] and e['marker'] and e['pass'] and e['lineagePass'] and e['restored'] and e['abortReason'] is None and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    for item in run['sources']:assert item['sha256'] in index
    b=raw(HERE/f'results/{label}.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'));r=d['selectionLineage'];assert d['pass'] and not d['error'] and r['pass'] and r['restored'] and r['branchCoverage'] and r['contextGenerations']==2
    count=40 if label=='qualification' else 80;assert e['comparedFrames']==len(r['frames'])==count and r['width']==r['height']==64 and r['logicalBufferBytes']==2031616
    for f in r['frames']:assert f['comparedPackets']==12288 and f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha'] and len(f['eventCounts'])==17
    a=summarize(label);assert a==read(HERE/(label+'-analysis.json')) and not a['productionChanged']
    assert a['rows'][0]['events']['ageRejected']>0 and a['rows'][1]['events']['jacobianRejected']>0 and a['rows'][1]['events']['materialRejected']>0 and a['rows'][1]['events']['projectionInvalid']>0 and a['rows'][1]['events']['noMatch']>0
    assert a['rows'][2]['invalidInitial']>0 and a['rows'][2]['events']['previousSurfaceInvalid']>0 and a['rows'][3]['blackInitial']>0 and len(a['rows'][4]['generations'])==2
    assert all(row['events']['previousReservoirInvalid']==0 for row in a['rows'])
for item in read(HERE/'compat-run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
reach=read(HERE/'reachability.json')
for path,item in reach['files'].items():assert sha((ROOT/path).read_bytes())==item['sha256']
assert len(reach['disabledOptionalPaths'])==3 and reach['previousReservoirInvalid']['observedEvents']==0
assert read(HERE/'matrix-analysis.json')['rows']==read(HERE/'compat-analysis.json')['rows']
assert not subprocess.check_output(['git','diff','--name-only','HEAD','--','Runtime'],cwd=ROOT)
print('PASS: original/private GI32/raw/NRD exact through age/Jacobian/view/material/invalid/black/rebuild events;independent event predicates+immutable origins;optionaldisabled/defensivezero not claimed executed;no production reset change')
