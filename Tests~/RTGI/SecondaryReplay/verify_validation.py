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
for label in ['initial-parity','capture-only','qualification','matrix','compat']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==44 and run['debugLayerRequested']
    for item in run['sources']:assert item['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'] and not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0 and e['abortReason'] is None and e['fresh']
    b=raw(HERE/f'results/{label}.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'));r=d['selectionLineage']
    if label in ['initial-parity','capture-only']:
        assert e['exitCode']==1 and not d['pass'] and 'packed GI output at1293' in d['error'].replace('at ','at') and len(r['frames'])==2
        if label=='capture-only':
            a=r['failedControl'];b=r['failedTrace'];assert r['failedPacketIndex']==1293 and r['failedShaderFrame']==1002 and a['radiance']!=b['radiance']
            assert all(a[k]==b[k] for k in ['position','misc','weight','normal','unused'])
        continue
    assert e['exitCode']==0 and e['pass'] and e['marker'] and e['lineagePass'] and e['restored'] and d['pass'] and not d['error'] and r['pass'] and r['restored'] and r['secondaryReplay']
    count=16 if label=='qualification' else 64;assert e['comparedFrames']==len(r['frames'])==count and r['width']==r['height']==64 and r['logicalBufferBytes']==3538944
    for f in r['frames']:assert f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha'] and f['exactReplayMismatches']==f['primaryVisibilityMismatches']==0
    assert sha(raw(HERE/'results'/label/r['stream']))==r['streamSha256']
    a=summarize(label);assert a==read(HERE/(label+'-analysis.json')) and not a['productionChanged'] and not a['partial']
    assert a['exactViewPackedLogLuvMismatches']==a['primaryVisibilityMismatches']==a['oracleViolatingComponents']==a['sampleLightOracleViolations']==0
    assert a['validAnalyticChoices']>0 and a['validEnvironmentChoices']>0 and a['lightDirectionOracleMaxAbsError']<1e-5 and a['packedViewPackedLogLuvMismatches']>0
for item in read(HERE/'compat-run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
for path,item in read(HERE/'inputs.json').items():assert sha((ROOT/path).read_bytes())==item['sha256']
assert summarize('initial-parity',True)==read(HERE/'initial-parity-analysis.json')
assert not subprocess.check_output(['git','diff','--name-only','HEAD','--','Runtime'],cwd=ROOT)
print('PASS: isolated observer originalpacket/raw/NRD exact;RG+compat static currentTLAS same-DI-choice exactview cacheLogLuv;independent CPU BRDF/analyticlight/envdirection gates;packedview differences+two parity failures preserved;no endpoint/dynamic confidence accepted')
