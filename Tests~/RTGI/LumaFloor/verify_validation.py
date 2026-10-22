"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, load, IDS, vec, CENTER, LUMA
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
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag'
for label,count,warm,measured in [('qualification',2,16,16),('matrix',4,64,256)]:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==32 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['localFunctional'] and e['copyBitExact']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0 and e['segments']==count
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    d,r,arrays,weights=load(label);assert sha(raw(HERE/f'results/{label}.json'))==e['reportSha256'] and d['frames']==32+count*(warm+measured)+2
    floor=d['lumaFloor'];assert floor['signalScale']==64 and abs(floor['floor']-1e-4/64)<1e-12 and floor['restoredOriginal']
    for item in floor['sources']:
        original=(ROOT/item['originalPath']).read_bytes();assert sha(original)==item['originalSha256'];body=(HERE/'generated'/label/Path(item['bodyPath']).name).read_bytes();wrapper=(HERE/'generated'/label/Path(item['wrapperPath']).name).read_bytes()
        assert sha(body)==item['bodySha256'] and sha(wrapper)==item['wrapperSha256']
        candidate=body.decode('utf-8-sig').replace('\r\n','\n').replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/','');stock=original.decode('utf-8-sig').replace('\r\n','\n')
        before='diffusePhiLIlluminationInv = 1.0 / max(1.0e-4,';after='diffusePhiLIlluminationInv = 1.0 / max((1.0e-4 / 64.0),';assert candidate.count(after)==1 and candidate.replace(after,before)==stock
    assert r['complete'] and r['stageIds']==IDS and len(r['referencePoints'])==27 and not r['trace']['replacementActive'] and r['trace']['restoredOriginal']
    for seg,a,w in zip(r['segments'],arrays,weights):
        assert len(a)==measured and len(w)==0 and seg['actualTemporal'] and seg['maxPrimaryError']<=.0005
        assert np.array_equal(a['writes'],np.tile([1,1,1,1,1,4],(measured,1)))
        assert np.array_equal(a['shader'],np.arange(seg['seed']+warm,seg['seed']+warm+measured))
        assert seg['lightCount']==18 and seg['instances']==15 and seg['cutouts']==12
        average=a['rgb'].astype(float).mean(0)
        for i,p in enumerate(seg['points']):
            for j,v in enumerate(p['stages']):assert np.allclose(average[j,i],vec(v['rgb']),rtol=2e-7,atol=1e-12)
f=read(HERE/'filtered-run.json');e=f['entries'][0];assert f['activeProcess'] is None and e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['localFunctional'] and e['copyBitExact'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
b=raw(HERE/'filtered-results/control.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'));r=d['localStageAudit'];assert d['frames']==98 and r['complete'] and r['precision']['copyBitExact'] and r['trace']['restoredOriginal'] and not r['trace']['replacementActive']
for seg in r['segments']:
    b=raw(HERE/'filtered-results/control'/seg['stream']);assert sha(b)==seg['streamSha256'];dtype=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(12,27,3))]);a=np.frombuffer(b,dtype=dtype,offset=292);assert len(a)==16 and np.array_equal(a['writes'],np.tile([1,1,1,1,1,4],(16,1)))
for source in f['sources']:assert source['sha256'] in index and sha((ROOT/source['path']).read_bytes())==source['sha256']
review=read(HERE/'filter-review.json');assert not review['runtimeChanged'] and not review['scanMoreValues'];item=review['restored'];assert sha((ROOT/item['path']).read_bytes())==item['restoredSha256'] and (ROOT/item['path']).read_bytes()==subprocess.check_output(['git','show',item['fromRevision']+':'+item['path']],cwd=ROOT)
for item in review['removed']:assert not (ROOT/item['path']).exists() and item['testedSha256'] in index
inputs=read(HERE/'inputs.json')
for item in inputs.values():assert sha(raw(HERE/item['path']))==item['rawSha256']
s=summarize();assert read(HERE/'analysis.json')==s and not s['productionChanged'] and not s['eligible'] and s['candidatePassingFullSeeds']==0 and s['baselinePassingFullSeeds']==2 and len(s['phasePassToFailPairs'])==11
assert s['relativeL1Reduction']<read(HERE/'review-contract.json')['minimumWeightedLocalL1Reduction']
assert all(p['rawAllLocalSamplesBitwiseEqual'] and p['preAtrousStagesBitwiseEqual'] for p in s['perSeed'])
print('PASS: two exact-token private shader edits and original5 schedule, three actual processes, insufficient benefit/full-phase regressions preserved, candidate removed and stock requalified')
