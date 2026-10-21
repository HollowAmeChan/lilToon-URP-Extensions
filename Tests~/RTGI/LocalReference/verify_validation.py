"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, load_seeds
import numpy as np
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
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
    packed=(HERE/item['path']).read_bytes();value=gzip.decompress(packed)
    assert sha(packed)==item['archiveSha256'] and len(packed)==item['archiveBytes'] and sha(value)==item['rawSha256'] and len(value)==item['rawBytes']
# JsonUtility fields are float32; reconstruct their exact IEEE values before cancellation-sensitive split statistics.
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def four(p):return np.array([p[c] for c in 'xyzw'],dtype=np.float32).astype(np.float64)
expected=[(x,y) for y in [204,206,226,227,252,255] for x in [85,86,87]]
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag'
for runname,reportname,batches in [('initial-qualification/run.json','initial-qualification/report.json',2),('reference-run.json','results/reference.json',2048),('qualification-run.json','results/qualification.json',2)]:
    run=read(HERE/runname);assert run['activeProcess'] is None and run['step']==27 and run['debugLayerRequested']
    e=run['entries'][0];assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['denseFunctional']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0 and e['referenceBatches']==batches and e['referencePoints']==18
    assert logs[e['log']]['sha256']==e['logSha256'];log=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,log,re.I)
    for source in run['sources']:assert source['sha256'] in index
    value=raw(HERE/reportname);assert sha(value)==e['reportSha256'];d=json.loads(value.decode('utf-8-sig'));r=d['denseReference']
    assert d['pass'] and not d['compatibility'] and d['frames']==32+batches+2 and d['width']==d['height']==256
    assert r['functionalPass'] and r['stableDomain'] and r['scheduledEditorTicks'] and r['environment'] and r['alphaVisibility']
    assert r['lightCount']==18 and r['instances']==15 and r['cutouts']==12 and r['secondaryLightSamples']==8 and r['secondaryEnvironmentSamples']==4
    assert abs(r['rayBias']-.001)<1e-9 and r['maxDistance']==1000 and r['samplesPerPoint']==512 and r['batchCount']==batches and r['mainFrames']==0
    assert [(p['x'],p['y']) for p in r['points']]==expected
    means=np.array([[vec(v) for v in b['means']] for b in r['batches']]);assert means.shape==(batches,18,3) and np.isfinite(means).all() and (means>=0).all()
    for i,b in enumerate(r['batches']):
        assert b['index']==i and b['seed']==104729+i*8191 and b['samplesPerPoint']==512
        assert len(b['positiveSamples'])==len(b['maxima'])==18 and all(0<=v<=512 for v in b['positiveSamples'])
        if i:assert b['applicationFrame']>r['batches'][i-1]['applicationFrame']
    for i,p in enumerate(r['points']):
        # Existing LINQ Average(float selector) returns float before assignment to double.
        a=means[:,i];mean=a.mean(0).astype(np.float32).astype(np.float64);se=np.sqrt(((a-mean)**2).sum(0)/(batches-1)/batches);first=a[:batches//2].mean(0).astype(np.float32).astype(np.float64);second=a[batches//2:].mean(0).astype(np.float32).astype(np.float64)
        assert np.allclose(mean,vec(p['referenceMean']),rtol=2e-7,atol=1e-12) and np.allclose(se,vec(p['batchMeanStandardError']),rtol=2e-7,atol=1e-12)
        assert np.allclose(first,vec(p['firstHalfMean']),rtol=2e-7,atol=1e-12) and np.allclose(second,vec(p['secondHalfMean']),rtol=2e-7,atol=1e-12)
        split=np.abs(first-second)/np.maximum(np.abs(mean),1e-8);assert np.allclose(split,vec(p['splitRelativeDifference']),rtol=2e-7,atol=1e-10)
        positive=sum(b['positiveSamples'][i] for b in r['batches']);assert p['samples']==batches*512 and p['positiveSamples']==positive
        qualified=batches>=16 and bool(np.all(np.where(mean>1e-6,(se/np.maximum(mean,1e-30)<=.02)&(split<=.05),(se<=1e-7)&(np.abs(first-second)<=1e-7))))
        if positive<64 and np.dot(vec(p['albedo']),vec(p['albedo']))>0:qualified=False
        assert p['converged']==qualified and p['maxPrimaryError']<=.0005 and p['maxRayPrimaryError']<=.0005
        assert all(np.isfinite(vec(p[k])).all() for k in ['primaryWorld','normal','albedo'])
    assert r['allPointsConverged']==all(p['converged'] for p in r['points'])==(batches==2048)
    for name,batch in [('firstGuides',0),('lastGuides',batches-1)]:
        g=r[name];assert g['batch']==batch and g['applicationFrame']==r['batches'][batch]['applicationFrame'] and g['width']==g['height']==256 and g['signalScale']==64
        assert g['normalFormat']==g['signalFormat']==g['rawFormat']==g['filteredFormat']=='R16G16B16A16_SFloat' and g['viewZFormat']=='R32_SFloat'
        assert [(p['x'],p['y']) for p in g['points']]==expected
        for p,q in zip(g['points'],r['points']):
            packed=four(p['packedNormalRoughness']);signal=four(p['scaledSignal']);normal=vec(q['normal']);n=normal/np.abs(normal).sum()
            assert np.isfinite(packed).all() and np.isfinite(signal).all() and np.isfinite(p['viewZ'])
            encoded=np.array([.5*n[0]+.5*n[1]+.5,.5*n[1]+.5-.5*n[0],0 if n[2]<0 else 1,0]);error=np.abs(encoded-packed).max()
            assert error<=.0005 and abs(error-p['normalPackError'])<1e-7
            decoded=np.array([packed[0]-packed[1],packed[0]+packed[1]-1,(-1 if packed[2]*2-1<0 else 1)*(1-abs(packed[0]-packed[1])-abs(packed[0]+packed[1]-1))]);decoded/=np.linalg.norm(decoded)
            assert np.linalg.norm(decoded-normal)<=.004 and np.allclose(decoded,vec(p['decodedNormal']),rtol=2e-7,atol=1e-7)
            assert abs(p['viewZ']-q['viewDepth'])<=.0005
            restored=signal[:3]*64*vec(q['albedo']);physical=vec(p['rawPhysical']);filtered=vec(p['filteredPhysical'])
            assert np.isfinite(physical).all() and np.isfinite(filtered).all() and (physical>=0).all() and (filtered>=0).all()
            assert np.allclose(restored,vec(p['remodulatedSignal']),rtol=2e-7,atol=1e-10) and np.all(np.abs(restored-physical)<=np.maximum(.000005,np.abs(physical)*.002))
review=read(HERE/'review.json');before=(HERE/index[review['beforeSha256']]['path']).read_text(encoding='utf-8-sig').replace('\r\n','\n');after=(HERE/index[review['afterSha256']]['path']).read_text(encoding='utf-8-sig').replace('\r\n','\n')
assert after.replace(review['exactAddedLine']+'\n','')==before
for source in read(HERE/'qualification-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
config=(ROOT/'Runtime/RTDI/ThirdParty/NRD/NRDConfig.hlsli').read_text(encoding='utf-8-sig');assert '#define NRD_NORMAL_ENCODING 2' in config and '#define NRD_ROUGHNESS_ENCODING 1' in config
for item in [read(HERE/'inputs.json')['productionReport']]+read(HERE/'inputs.json')['publicationMaps']:assert sha(raw(HERE/item['path']))==item['rawSha256']
load_seeds();s=summarize();assert read(HERE/'analysis.json')==s and not s['productionChanged'] and not s['qualityRepaired'] and s['allPointsConverged']
assert s['samplesPerPoint']==1048576 and s['totalReferenceSamples']==18874368
assert s['groups']['centers']['qualifiedPoints']==6 and s['groups']['adjacentControls']['qualifiedPoints']==12
assert s['groups']['allFixedReceivers']['filteredNormalizedL1']>s['groups']['allFixedReceivers']['rawNormalizedL1']
print('PASS: 18/18 original convergence gates, three actual processes, exact sources/logs, independent guide decoding and all fixed receiver comparisons; full-image quality still open')
