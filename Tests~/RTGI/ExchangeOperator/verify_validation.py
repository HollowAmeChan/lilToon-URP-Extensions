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
from compare_scope import compare, stages
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
for label in ['initial-local-qualification','qualification','matrix','filtered']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==39 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['marker'] and e['fresh'] and e['sharedReleased'] and e['restoredHook'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    assert e['physicalCalibrationMismatches']==0 and e['pairedRestored'] and e['gatherBitExact'] and e['gatherComparedComponents']==112
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    value=raw(HERE/f'results/{label}.json');assert sha(value)==e['reportSha256'];doc=json.loads(value.decode('utf-8-sig'));d=doc['localStageAudit'];shared=d['shared'];r=shared['paired'] if label=='filtered' else shared['exchange']
    n=1024 if label=='matrix' else 32;assert e['comparedFrames']==e['physicalCalibratedFrames']==r['calibratedFrames']==len(shared['frames'])==n
    assert shared['recorded']==(1280 if label=='matrix' else 64) and r['firstWrites']==shared['recorded'] and r['laterWrites']==4*r['firstWrites']
    assert shared['released'] and shared['restoredHook'] and shared['independentPools'] and shared['sharedInputBindings'] and r['restored'] and r['released'] and r['calibrationMismatches']==0 and r['maxCalibrationError']==0
    assert r['maxAddedGuideError']<=.0005 and r['addedReference']['converged']
    assert doc['frames']==(1314 if label=='matrix' else 98) and d['complete'] and d['precision']['copyBitExact'] and d['precision']['copiedComponents']==64
    for seg in d['segments']:
        assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005
        for field in ['stream','weightStream','publicationMap']:assert sha(raw(HERE/'results'/label/seg[field]))==seg[field+'Sha256']
        assert all(c['writes']==seg['measured']*(4 if c['pipeline']==10 else 1) for c in seg['captures'])
    for seg in r['segments']:
        for field in ['map','stream']+(['referenceMap'] if label!='filtered' else []):assert sha(raw(HERE/'results'/label/seg[field]))==seg[field+'Sha256']
    if label=='filtered':
        assert e['baselineMismatches']==0 and all(f['mismatches0']==0 and f['baselineSha256']==f['branch0Sha256'] for f in shared['frames'])
        continue
    assert r['normalizationAblation'] and all(f['mismatches0']>0 for f in shared['frames'])
    d,r,ss=stages(label)
    for seg,ids,coords,frames in ss:
        assert np.array_equal(ids,[3,4,5,6,9,101,102,103,104]) and coords[-1].tolist()==[196,141]
        assert np.all(frames['rgba'][:,:,:, :,3]>=0) and np.all(frames['rgba'][:,:,2,:,3]==0)
        for guide in [seg['firstGuide'],seg['lastGuide']]:assert guide['maxError']<=.0005
    result=compare(label);assert result['qualified'] and result==read(HERE/(label+'-scope.json'))
    if label=='initial-local-qualification':assert not result['wholeFirstImageQualified'] and r.get('firstStageComparedFrames',0)==0
    else:
        assert result['wholeFirstImageQualified'] and r['firstStageComparedFrames']==len(r['firstInputFrames'])==n and r['firstStageComparedComponents']==n*262144
        for f,c in zip(r['firstInputFrames'],shared['frames']):assert f['shaderFrame']==c['shaderFrame'] and f['components']==262144 and f['referenceSha256']==f['variantSha256']
    policy=read(HERE.parent/'PairedStages/policy-inputs.json')['entries']
    for item in policy:assert sha((HERE/'generated'/label/item['name']).read_bytes())==item['sha256']
    assert sha((HERE/'generated'/label/'ExchangeVariant.hlsl').read_bytes())==r['variantBodySha256']
    assert sha((HERE/'generated'/label/'ExchangeVariant.compute').read_bytes())==r['variantWrapperSha256']
# Current files follow the filtered038 source, not archived039 candidates.
for source in read(HERE/'filtered-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
assert not (ROOT/'Editor/RTGI/HoRTGIExchangeAudit.cs').exists() and 'exchangeAudit' not in (ROOT/'Editor/RTGI/HoRTGICastleValidation.cs').read_text(encoding='utf-8-sig')
for relative,item in read(HERE/'inputs.json').items():assert sha((ROOT/relative).read_bytes())==item['sha256']
s=summarize();assert s==read(HERE/'analysis.json') and s['comparisonQualified'] and not s['productionChanged']
assert s['mechanismPredictionSupported'] and all(.89<p['fractionOfDiRegressionRecovered']<1 for p in s['mechanismRecovery'])
assert not s['eligibleForFurtherEightSeedDynamicHdrPlayerGpuQualification'] and .03<s['groups']['new']['relativeL1Reduction']<.04 and not s['hardRegressions']
assert s['full']['candidatePassingSeeds']==2 and not s['phasePassToFailPairs']
figure=read(HERE/'figure.json');assert figure['dataFigureNotSceneScreenshot'] and sha((HERE/'operator-tradeoff.png').read_bytes())==figure['figureSha256'] and sha((HERE/'analysis.json').read_bytes())==figure['sourceAnalysisSha256']
print('PASS: one normalization block, same-input main/ref/variant,1024 whole-first-image equalities,89-98% old recovery but only3.26%new gain; rejected/removed and current03832frames qualified; initial limited proof preserved')
