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
 run=read(HERE/f'{label}-run.json');entry=run['entries'][0];assert run['step']==46 and run['activeProcess'] is None and run['debugLayerRequested'];assert entry['exitCode']==0 and entry['abortReason'] is None and entry['fresh'] and entry['marker'] and entry['pass'] and entry['lineagePass'] and entry['restored'] and entry['debugErrors']==entry['shaderCsDiagnostics']==entry['sourceDrift']==0
 for item in run['sources']:assert item['sha256'] in index
 assert logs[entry['log']]['sha256']==entry['logSha256'];assert not re.search(bad,(HERE/logs[entry['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
 data=raw(HERE/f'results/{label}.json');assert sha(data)==entry['reportSha256'];report=json.loads(data.decode('utf-8-sig'));r=report['selectionLineage'];nf=16 if label=='qualification' else 128;assert report['pass'] and r['pass'] and r['restored'] and r['endpointReplay'] and len(r['frames'])==entry['comparedFrames']==nf
 for f in r['frames']:assert f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha']
 analysis=summarize(label);assert analysis==read(HERE/(label+'-analysis.json')) and not analysis['productionChanged'] and analysis['zeroToBright']>0 and analysis['brightToZero']>0
badRun=read(HERE/'initial-sharing-run.json');e=badRun['entries'][0];assert badRun['activeProcess'] is None and e['exitCode']==1 and e['fresh'] and not e['marker'] and not e['pass'] and e['comparedFrames']==16 and e['sourceDrift']==0 and e['debugErrors']==e['shaderCsDiagnostics']==0
assert 'Sharing violation' in (HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert sha(raw(HERE/'results/initial-sharing.json'))==e['reportSha256'];assert not read(HERE/'results/initial-sharing.json')['pass']
for item in read(HERE/'compat-run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
for name,item in read(HERE/'inputs.json').items():assert sha((ROOT/name).read_bytes())==item['sha256']
m=read(HERE/'oracle-model-review.json');assert m['rawGpuDataUnchanged'] and m['thresholdUnchanged'] and sha((HERE/m['initialSource']).read_bytes())==m['sha256']
a=read(HERE/'matrix-analysis.json');b=read(HERE/'compat-analysis.json');assert a==b
assert not subprocess.check_output(['git','diff','--name-only','HEAD','--','Runtime'],cwd=ROOT)
print('PASS: independent plane/alpha/BRDF and full-codec same-sample confidence, actual independent shared-input NRD branches; strict original packet/raw/NRD parity; failed stream-save and initial CPU model retained; production history/quality NOT accepted')
