"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import load, raw, summarize, SEEDS, vec
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
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag'
for label,n,warm,measured in [('qualification',2,16,16),('matrix',8,64,256)]:
    run=read(HERE/f'{label}-run.json');assert run['step']==30 and run['activeProcess'] is None and run['debugLayerRequested']
    e=run['entries'][0];assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['seedFunctional']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    d,r,m=load(HERE,label);assert sha(raw(HERE/f'results/{label}.json'))==e['reportSha256']
    assert d['pass'] and d['frames']==32+n*(warm+measured)+2 and r['functionalPass'] and r['terminal'] and r['testedModes']==['temporal-spatial']
    assert len(r['referencePoints'])==16 and sum(p['converged'] for p in r['referencePoints'])==15 and len(r['segments'])==n
    for inp in r['referenceInputs']:assert sha((ROOT/inp['path']).read_bytes())==inp['sha256']
    c=r['candidate'];before=bytes.fromhex(c['settingsBeforeHex']);after=bytes.fromhex(c['settingsAfterHex']);assert c['policy']=='atrous3' and c['appliedAtrous']==3 and c['restoredNativeAtrous']==5 and c['settingsHookRestored']
    assert len(before)==len(after)==148 and c['atrousOffset']==92 and before[:92]==after[:92] and before[96:]==after[96:]
    assert int.from_bytes(before[92:96],'little')==5 and int.from_bytes(after[92:96],'little')==3
    for i,seg in enumerate(r['segments']):
        assert seg['seed']==SEEDS[i] and seg['mode']=='temporal-spatial' and seg['warmup']==warm and seg['measured']==measured and seg['appliedAtrous']==seg['firstNativeAtrousDispatches']==seg['lastNativeAtrousDispatches']==3
        assert seg['actualTemporal'] and seg['temporal'] and seg['spatial'] and seg['environment'] and seg['alphaVisibility'] and seg['lightCount']==18 and seg['instances']==15 and seg['cutouts']==12 and seg['maxPrimaryError']<=.0005
        assert seg['firstShaderFrame']==SEEDS[i]+warm and seg['lastShaderFrame']==SEEDS[i]+warm+measured-1 and seg['lastApplicationFrame']-seg['firstApplicationFrame']>=measured-1
        assert m.shape==(n,256,256,6) and np.isfinite(m).all() and (m>=0).all()
        for record,part in [(seg['full'],m[i])]+[(p,m[i,p['phaseY']::4,p['phaseX']::4]) for p in seg['phases']]:
            assert np.allclose(part[:,:,:3].mean((0,1),dtype=np.float64),vec(record['raw']),rtol=2e-7,atol=1e-13)
            assert np.allclose(part[:,:,3:].mean((0,1),dtype=np.float64),vec(record['filtered']),rtol=2e-7,atol=1e-13)
        assert len(seg['phases'])==16 and seg['full']['pixels']==65536 and seg['full']['samples']==65536*measured
f=read(HERE/'filtered-run.json');assert f['activeProcess'] is None and f['step']==30
e=f['entries'][0];assert e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['localFunctional'] and e['copyBitExact'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
b=raw(HERE/'filtered-results/qualification.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'));r=d['localStageAudit'];assert d['frames']==98 and r['complete'] and r['precision']['copyBitExact'] and r['precision']['copiedComponents']==64
for seg in r['segments']:
    b=raw(HERE/'filtered-results/qualification'/seg['stream']);assert sha(b)==seg['streamSha256']
    dtype=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(9,18,3))]);a=np.frombuffer(b,dtype=dtype,offset=208);assert len(a)==16 and np.isfinite(a['rgb']).all() and np.array_equal(a['writes'],np.tile([1,1,1,1,1,4],(16,1)))
for source in f['sources']:assert source['sha256'] in index and sha((ROOT/source['path']).read_bytes())==source['sha256']
review=read(HERE/'filter-review.json');assert not review['runtimeChanged'] and not review['scanIteration2or4']
for item in review['files']:
    value=(ROOT/item['path']).read_bytes();assert sha(value)==item['filteredSha256'] and item['testedCandidateSha256'] in index
    assert value==subprocess.check_output(['git','show',item['restoreFrom']+':'+item['path']],cwd=ROOT)
assert 'RunAtrousThree' not in (ROOT/'Editor/RTGI/HoRTGILocalStageAudit.cs').read_text(encoding='utf-8-sig') and 'seedAtrousThree' not in (ROOT/'Editor/RTGI/HoRTGICastleValidation.cs').read_text(encoding='utf-8-sig')
inputs=read(HERE/'inputs.json')
for item in [inputs['baselineReport']]+inputs['baselineMaps']:assert sha(raw(HERE/item['path']))==item['rawSha256']
for key in ['newReference','previousFourSeed']:
    item=inputs[key];packed=(HERE/item['path']).read_bytes();assert sha(packed)==item['archiveSha256'] and sha(gzip.decompress(packed))==item['rawSha256']
s=summarize();assert read(HERE/'analysis.json')==s and not s['productionChanged'] and not s['eligibleForDynamicHdrPlayerGpuQualification']
assert len(s['all34ReferencePoints'])==34 and sum(p['referenceQualified'] for p in s['all34ReferencePoints'])==33
assert [(p['x'],p['y']) for p in s['all34ReferencePoints'] if not p['referenceQualified']]==[(16,16)]
assert [(p['group'],p['x'],p['y'],p['channel']) for p in s['hardRegressions']]==[('legacy',141,157,1),('legacy',141,157,2)]
assert all(all(v for k,v in row.items() if k!='seed') for row in s['observerChecks']+s['crossChecks'])
assert s['full']['candidatePooledGatePass'] and s['full']['candidatePassingSeeds']==7 and s['full']['pooledPhaseFailures']==8
print('PASS: full8-seed candidate data and 34 controls, legacy hard regressions preserved; candidate shelved, three Editor files restored exactly and actual default5 rendering requalified')
