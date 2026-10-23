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
from compare_observer import compare, stages
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
inputs=read(HERE/'policy-inputs.json')['entries']
for label in ['qualification','matrix']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==38 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['marker'] and e['fresh'] and e['sharedReleased'] and e['restoredHook'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    assert e['baselineMismatches']==e['physicalCalibrationMismatches']==0 and e['pairedRestored'] and e['gatherBitExact'] and e['gatherComparedComponents']==112
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    value=raw(HERE/f'results/{label}.json');assert sha(value)==e['reportSha256'];doc=json.loads(value.decode('utf-8-sig'));d,r,ss=stages(label);shared=d['shared']
    n=32 if label=='qualification' else 1024;assert e['comparedFrames']==e['physicalCalibratedFrames']==r['calibratedFrames']==len(shared['frames'])==n
    assert shared['recorded']==(64 if label=='qualification' else 1280) and r['firstWrites']==shared['recorded'] and r['laterWrites']==4*r['firstWrites']
    assert shared['released'] and shared['restoredHook'] and shared['independentPools'] and shared['sharedInputBindings'] and r['restored'] and r['released'] and r['gatherBitExact'] and r['gatherComparedComponents']==112
    assert r['maxAddedGuideError']<=.0005 and r['addedReference']['converged'] and (r['addedReference']['x'],r['addedReference']['y'])==(196,141)
    for f in shared['frames']:assert f['comparedComponents']==256*256*4 and f['mismatches0']==0 and f['maxError0']==0 and f['baselineSha256']==f['branch0Sha256'] and f['mismatches1']>0
    assert doc['frames']==(98 if label=='qualification' else 1314) and d['complete'] and d['precision']['copyBitExact'] and d['precision']['copiedComponents']==64
    for seg in d['segments']:
        assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005
        for field in ['stream','weightStream','publicationMap']:assert sha(raw(HERE/'results'/label/seg[field]))==seg[field+'Sha256']
        assert all(c['writes']==seg['measured']*(4 if c['pipeline']==10 else 1) for c in seg['captures'])
    for seg in r['segments']:
        for field in ['map','stream']:assert sha(raw(HERE/'results'/label/seg[field]))==seg[field+'Sha256']
    for seg,ids,coords,frames in ss:
        assert np.array_equal(ids,[3,4,5,6,9,101,102,103,104]) and coords[-1].tolist()==[196,141]
        assert np.array_equal(frames['shader'],np.arange(seg['seed']+(16 if label=='qualification' else 64),seg['seed']+(16 if label=='qualification' else 64)+seg['frames']))
        assert np.all(frames['rgba'][:,:,:, :,3]>=0)
        # Settled HistoryFix left fast/responsive alpha zero; final alpha is history length.
        assert np.all(frames['rgba'][:,:,2,:,3]==0)
        for guide in [seg['firstGuide'],seg['lastGuide']]:assert guide['maxError']<=.0005 and guide['originalWorld']['w']>.5 and guide['rabWorld']['w']>.5
    for item in inputs:assert sha((HERE/'generated'/label/item['name']).read_bytes())==item['sha256'] and sha((ROOT/item['path']).read_bytes())==item['sha256']
    result=compare(label);assert result['qualified'] and result==read(HERE/(label+'-observer.json'))
for source in read(HERE/'matrix-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
for relative,item in read(HERE/'inputs.json').items():assert sha((ROOT/relative).read_bytes())==item['sha256']
s=summarize();assert s==read(HERE/'analysis.json') and not s['productionChanged'] and not s['policyAccepted']
assert all(v['rgbDifferentComponents']==v['alphaDifferentComponents']==0 for v in s['upstreamStageEquality'][:4])
assert s['upstreamStageEquality'][4]['rgbDifferentComponents']>0 and s['upstreamStageEquality'][-1]['alphaDifferentComponents']==0
assert len(s['oldFailureStageTrajectories'])==2 and len(s['counter141157NineNeighborVarianceBlur'])==2
assert not read(HERE.parent/'DiffusePolicy/analysis.json')['eligibleForFurtherEightSeedDynamicHdrPlayerGpuQualification']
figure=read(HERE/'plot-attempt.json');assert figure['renderedFigureReviewed'] and figure['notSceneScreenshot'] and sha((HERE/'stage-trajectories.png').read_bytes())==figure['figureSha256'] and sha((HERE/'analysis.json').read_bytes())==figure['sourceAnalysisSha256']
print('PASS: 112 F32 gather components;1024 shared physical calibrations;stock/candidate publications exact037;all9 stage copies qualified;28 guides and real variance/alpha semantics;rejected policy remains unaccepted')
