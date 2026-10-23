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
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|error CS'
for label in ['initial-capture','initial-uav','qualification','matrix','compat']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==42 and run['debugLayerRequested']
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];text=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,text,re.I)
    assert e['debugErrors']==e['sourceDrift']==0 and e['abortReason'] is None
    if label=='initial-capture':
        assert e['exitCode']==1 and not e['fresh'] and e['shaderCsDiagnostics']==5
        d=read(HERE/'initial-capture/report.json');assert not d['pass'] and 'witnesses incomplete' in d['error']
        assert text.count("Shader error in 'HoRTGISelectionCapture'")==4 and text.count("Shader warning in 'Lineage'")==1
        continue
    b=raw(HERE/f'results/{label}.json');assert sha(b)==e['reportSha256'];d=__import__('json').loads(b.decode('utf-8-sig'))
    if label=='initial-uav':assert e['exitCode']==1 and e['shaderCsDiagnostics']==1 and not d['pass'] and '12 UAVs' in d['error'];continue
    assert e['exitCode']==0 and e['pass'] and e['lineagePass'] and e['restored'] and e['fresh'] and e['marker'] and e['shaderCsDiagnostics']==0 and not d['error'] and d['pass']
    r=d['selectionLineage'];assert r['pass'] and r['restored'] and r['width']==r['height']==64 and r['logicalBufferBytes']==1966080
    count=32 if label=='qualification' else 128;assert len(r['frames'])==e['comparedFrames']==count
    for f in r['frames']:assert f['comparedPackets']==12288 and f['sourceChecks']==16384 and f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha']
    result=summarize(label);assert result==read(HERE/(label+'-analysis.json')) and not result['productionChanged'] and result['sourceChecks']==count*16384
    assert all(x['initialSampleKinds']==[1,2] and x['validSecondaryDiChoices']>0 and x['environmentChoices']>0 for x in result['rows'])
    assert result['rows'][1]['selectedPreviousFrame']>0 and result['rows'][3]['selectedPreviousFrame']>0 and result['rows'][2]['spatialDifferentOrigin']>0 and result['rows'][3]['spatialDifferentOrigin']>0
# Current source is later compatibility runner version. Raw failed variants stay archived.
for item in read(HERE/'compat-run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
assert read(HERE/'matrix-analysis.json')['rows']==read(HERE/'compat-analysis.json')['rows']
assert not __import__('subprocess').check_output(['git','diff','--name-only','HEAD','--','Runtime'],cwd=ROOT)
print('PASS: actual dualGI fourT/S RG+compat,all originalGI32/raw/NRD outputs bitexact;4,194,304immutable source checks;preS history/Spatial1 actualShade association;capture/UAV failures preserved;no confidence or exhaustive branch acceptance')
