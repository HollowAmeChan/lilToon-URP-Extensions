"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
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
    b=(HERE/item['path']).read_bytes();v=gzip.decompress(b);assert sha(b)==item['archiveSha256'] and len(b)==item['archiveBytes'] and sha(v)==item['rawSha256'] and len(v)==item['rawBytes']
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|error CS'
for label in ['initial-compile','qualification']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==41 and run['debugLayerRequested']
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];text=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,text,re.I)
    b=raw(HERE/f'results/{label}.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'))
    assert e['fresh'] and e['abortReason'] is None and e['debugErrors']==e['sourceDrift']==0
    if label=='initial-compile':
        assert e['exitCode']==1 and not d['pass'] and e['shaderCsDiagnostics']==3 and 'Actual GPU packet field interpretation differs' in d['error']
        assert text.count("Shader error in 'HoRTGIReplayContract'")==3 and d['cpuEqualWordChecks']==0
        continue
    assert e['exitCode']==0 and e['pass'] and e['marker'] and e['shaderCsDiagnostics']==0 and not d['error'] and d['api']=='Direct3D12' and d['unity']=='6000.3.15f1'
    assert d['cases']==64 and d['wordsPerCase']==10 and d['cpuEqualWordChecks']==1024 and d['collisions']==32 and d['selectedB']==48 and d['missingMisc']==d['missingUnused']==64
    assert len(raw(HERE/'results/qualification'/d['stream']))==10240 and sha(raw(HERE/'results/qualification'/d['stream']))==d['streamSha256']
    for name in ['editorLayout','playerProbeLayout']:
        v=d[name];assert [v[k] for k in ['size','position','misc','radiance','weight','normal','unused']]==[32,0,12,16,20,24,28]
    for item in run['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
for relative,item in read(HERE/'inputs.json').items():assert sha((ROOT/relative).read_bytes())==item['sha256']
s=summarize();assert s==read(HERE/'analysis.json') and not s['productionChanged'] and s['oracleMaxAbsError']<1e-6
assert s['sameChoiceUnchangedRadianceBitExactCases']==64 and s['packed32ByteCollisionCases']==32 and s['newChoiceStaticFalseGradientCases']==32 and s['newChoiceMissedRealChangeCollisionCases']==16
assert s['tentativeMemoryBudgets'][-1]['extraBytes']==199802880
old=read(HERE/'legacy-layout-review.json');assert old['newSchema']=='GI32-v2'
for relative,item in old['sourceBefore'].items():assert sha(subprocess.check_output(['git','cat-file','blob',item['gitBlob']],cwd=ROOT))==item['sha256']
print('PASS: actual SDK GPU GI32 semantic ABI,default metadata loss,selection/opaque ledger;independent analytic oracle3.674e-8,negative controls;1024whole-word checks;future labels fixed,initial compile failure preserved;no production confidence acceptance')
