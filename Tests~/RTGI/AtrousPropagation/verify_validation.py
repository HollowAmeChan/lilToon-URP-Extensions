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
for label,count,warm,measured,replace in [('control',2,16,16,False),('control-matrix',4,64,256,False),('qualification',2,16,16,True),('matrix',4,64,256,True)]:
    run=read(HERE/f'{label}-run.json');assert run['activeProcess'] is None and run['step']==31 and run['debugLayerRequested'];e=run['entries'][0]
    assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['localFunctional'] and e['copyBitExact']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0 and e['segments']==count
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    d,r,arrays,weights=load(label);assert sha(raw(HERE/f'results/{label}.json'))==e['reportSha256']
    assert d['pass'] and d['frames']==32+count*(warm+measured)+2 and r['complete'] and r['stageIds']==IDS and len(r['referencePoints'])==27
    assert sum(p['converged'] and p['samples']>0 for p in r['referencePoints'])==19 and all(p['samples']==0 and not p['converged'] for p in r['referencePoints'][19:])
    assert r['trace']['replacementActive']==replace and r['trace']['restoredOriginal'] and r['trace']['pointCount']==27 and r['trace']['vectorsPerPoint']==37
    assert r['precision']['copyBitExact'] and r['precision']['copiedComponents']==64 and r['precision']['mode']=='production' and r['precision']['signalScale']==64
    assert sha((ROOT/r['legacyReferenceInput']).read_bytes())==r['legacyReferenceSha256']
    for seg,a,w in zip(r['segments'],arrays,weights):
        assert np.array_equal(a['shader'],np.arange(seg['seed']+warm,seg['seed']+warm+measured)) and np.all(np.diff(a['application'])>=1)
        assert np.array_equal(a['writes'],np.tile([1,1,1,1,1,4],(measured,1)))
        assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005 and seg['lightCount']==18 and seg['instances']==15 and seg['cutouts']==12
        average=a['rgb'].astype(float).mean(0);assert [(p['x'],p['y']) for p in seg['points']]==[(p['x'],p['y']) for p in r['referencePoints']]
        for i,p in enumerate(seg['points']):
            assert [v['pipeline'] for v in p['stages']]==IDS
            for j,v in enumerate(p['stages']):assert np.allclose(average[j,i],vec(v['rgb']),rtol=2e-7,atol=1e-12)
        if not replace:assert len(w)==0
        else:
            expected=[0,measured-1] if measured==16 else [0,32,64,96,128,160,192,224,255]
            assert w['observed'].tolist()==expected and np.array_equal(w['shader'],seg['seed']+warm+w['observed'])
            v=w['v'].astype(float);assert np.array_equal(v[:,:,:,0,2],np.broadcast_to(np.array([2,4,8,16])[None,:,None],v.shape[:3]))
            assert np.array_equal(v[:,:,:,0,3],np.broadcast_to(np.array([0,0,0,1])[None,:,None],v.shape[:3]))
            taps=v[:,:,:,5::4];donors=v[:,:,:,6::4];factors=v[:,:,:,7::4];weights_=taps[:,:,:, :,2];assert (weights_>=0).all()
            assert np.allclose(factors.prod(-1),weights_,rtol=2e-6,atol=1e-9)
            sumW=v[:,:,:,3,2];assert np.max(np.abs(CENTER+weights_.sum(3)-sumW))<2e-7
            expectedOut=(v[:,:,:,1,:3]*CENTER+(donors[:,:,:,:,:3]*weights_[...,None]).sum(3))/sumW[...,None]
            assert np.max(np.abs(expectedOut-v[:,:,:,4,:3]))<1e-9
            for i,frame in enumerate(w['observed']):
                albedo=np.array([vec(p['albedo']) for p in r['referencePoints']]);physical=v[i,:,:,4,:3]*64*albedo[None,:,:]
                assert np.allclose(physical,a['rgb'][frame,7:11],rtol=3e-7,atol=1e-10)
    generated=HERE/'generated'/label;body=(generated/'AtrousTrace.hlsl').read_bytes();wrapper=(generated/'AtrousTrace.compute').read_bytes();assert sha(body)==r['trace']['bodySha256'] and sha(wrapper)==r['trace']['wrapperSha256']
    text=body.decode('utf-8-sig');stripped=re.sub(r'(?ms)^[ \t]*// HO_TRACE_BEGIN [^\n]+\n.*?^[ \t]*// HO_TRACE_END [^\n]+\n?','',text)
    stripped=stripped.replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/','')
    original=(ROOT/'Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl').read_text(encoding='utf-8-sig');assert sha(original.replace('\r\n','\n').encode())==r['trace']['originalSha256']
    assert [v for v in stripped.splitlines() if v.strip()]==[v for v in original.splitlines() if v.strip()]
# The first attempt was functionally complete but did not meet exact observer equality.
i=read(HERE/'initial-debug/run.json');e=i['entries'][0];assert e['exitCode']==0 and e['pass'] and e['debugErrors']==e['shaderCsDiagnostics']==0
assert sha(raw(HERE/'initial-debug/report.json'))==e['reportSha256']
for source in i['sources']:assert source['sha256'] in index
assert logs[e['log']]['sha256']==e['logSha256']
for source in read(HERE/'matrix-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
inputs=read(HERE/'inputs.json');assert sha(raw(HERE/inputs['baseline']['path']))==inputs['baseline']['rawSha256'];assert sha((HERE/inputs['legacyReference']['path']).read_bytes())==inputs['legacyReference']['rawSha256']
b=(HERE/inputs['newReference']['path']).read_bytes();assert sha(b)==inputs['newReference']['archiveSha256'] and sha(gzip.decompress(b))==inputs['newReference']['rawSha256']
s=summarize();assert read(HERE/'analysis.json')==s and not s['productionChanged'] and not s['qualityRepaired']
assert all(x['stockEntireMeanMapBitwiseEqual'] for x in s['observerChecks']) and any(not x['debugEntireMeanMapBitwiseEqual'] for x in s['observerChecks'])
assert all(x['rawLocalAllSamplesBitwiseEqual'] for x in s['observerChecks'])
review=read(HERE/'observer-review.json');assert not review['initialExactQualificationAccepted'] and review['stockControlQualified'] and not review['qualityGatesRelaxed']
assert review['debugMeanMapMaxAbsoluteDifference']==max(x['debugMeanMapMaxAbsoluteDifference'] for x in s['observerChecks'])
print('PASS: five actual processes, exact stock per-round/maps, original math preserved, bounded separate UAV weight reconstruction, both receiver classes and no production changes')
