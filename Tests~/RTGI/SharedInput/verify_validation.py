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
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS'
for label in ['small','qualification','matrix']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==36 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['marker'] and e['fresh'] and e['sharedReleased'] and e['restoredHook'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    value=raw(HERE/f'results/{label}.json');assert sha(value)==e['reportSha256'];d=json.loads(value.decode('utf-8-sig'));r=d['shared'] if label=='small' else d['localStageAudit']['shared']
    assert d['pass'] and r['independentPools'] and r['sharedInputBindings'] and r['restoredHook'] and r['released'] and r['noop']['noopClone'] and r['noop']['restoredOriginal']
    n=64 if label=='small' else 32 if label=='qualification' else 1024
    assert len(r['frames'])==e['comparedFrames']==n and r['recorded']==(64 if label=='small' else 64 if label=='qualification' else 1280)
    components=(64 if label=='small' else 256)**2*4
    for f in r['frames']:
        assert f['comparedComponents']==components and f['mismatches0']==0 and f['maxError0']==0 and f['baselineSha256']==f['branch0Sha256']
        if label!='small' or f['shaderFrame']<24 or f['shaderFrame']>=48:
            assert f['mismatches1']==0 and f['maxError1']==0 and f['baselineSha256']==f['branch1Sha256']
        assert len(f['commonSha256'])==len(f['settingsSha256'])==64
    if label=='small':
        assert r['frames'][24]['isolatedReset1'] and sum(f['isolatedReset1'] for f in r['frames'])==1
        assert all(f['mismatches1']>4096 for f in r['frames'][24:48]) # More than alpha-only history-length differences.
        assert r['frames'][0]['baselineReset'] and r['frames'][48]['baselineReset']
    else:
        local=d['localStageAudit'];assert local['complete'] and local['precision']['copyBitExact'] and local['precision']['copiedComponents']==64
        assert d['frames']==(98 if label=='qualification' else 1314)
        for seg in local['segments']:
            assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005
            for field in ['stream','weightStream','publicationMap']:
                v=raw(HERE/'results'/label/seg[field]);assert sha(v)==seg[field+'Sha256']
            assert all(c['writes']==seg['measured']*(4 if c['pipeline']==10 else 1) for c in seg['captures'])
    gen=HERE/'generated'/label;body=(gen/'AtrousTrace.hlsl').read_bytes();wrapper=(gen/'AtrousTrace.compute').read_bytes();assert sha(body)==r['noop']['bodySha256'] and sha(wrapper)==r['noop']['wrapperSha256']
    normalized=body.decode('utf-8-sig').replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/','').replace('\r\n','\n');original=(ROOT/'Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl').read_text(encoding='utf-8-sig').replace('\r\n','\n');assert normalized==original
initial=read(HERE/'initial-allocation/report.json');assert not initial['pass'] and 'ReAllocateIfNeeded' in initial['error']
review=read(HERE/'initial-allocation/observation-review.json');assert review['wrapperExit']==1 and review['unityActualExit'] is None and review['expectedIsNotObservedExit']
for source in read(HERE/'initial-allocation/run.json')['sources']:assert source['sha256'] in index
for source in read(HERE/'matrix-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
# Both callback definition and invocation must remain inside UNITY_EDITOR blocks.
text=(ROOT/'Runtime/RTGI/NRD/HoRTGINrdContext.cs').read_text(encoding='utf-8-sig');inside=False;editorDepth=0
for line in text.splitlines():
    if line.strip()=='#if UNITY_EDITOR':editorDepth+=1
    if 'ValidationRecordInputs' in line:assert editorDepth>0
    if line.strip()=='#endif' and editorDepth:editorDepth-=1
item=read(HERE/'inputs.json')['baselineReport'];assert sha(raw(HERE/item['path']))==item['rawSha256']
s=summarize();assert read(HERE/'analysis.json')==s and not s['productionBehaviorChanged']
assert s['scene']['baselineVsBranch0Mismatches']==s['scene']['baselineVsNoopBranch1Mismatches']==0
assert all(all(v for k,v in x.items() if k!='seed') for x in s['observerChecks'])
print('PASS: small reset/history isolation, 1024 real GI frames full-RGBA bit equality, exact old baseline maps, independent resources/restored hooks, Editor-only callback; original allocation/wrapper failure kept')
