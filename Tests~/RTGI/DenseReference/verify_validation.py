"""Verify the AS range repair, its regressions, and precisely bounded reference evidence."""
import hashlib
import json
import math
import re
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def xyz(v):return [v[c] for c in ['x','y','z']]
index=read(HERE/'source-index.json')
for digest,item in index.items():assert sha(HERE/item['path'])==digest
def sources(run):
    for source in run.get('sources',[]):
        if source['path'].endswith('.dll'):assert sha(ROOT/source['path'])==source['sha256']
        else:assert source['sha256'] in index
def entry(folder,label,report=None):
    run=read(HERE/folder/'run.json');assert run['activeProcess'] is None;sources(run)
    e=next(x for x in run['entries'] if x['label']==label);assert sha(Path(e['log']))==e['logSha256']
    log=Path(e['log']).read_text(encoding='utf-8-sig')
    data=None
    if e.get('reportSha256'):
        path=HERE/report if report else HERE/'results'/(label+'.json');assert sha(path)==e['reportSha256'];data=read(path)
    return e,log,data

before,log,_=entry('debug-qualification-original','debug-qualification')
messages=re.findall(r'(?im)^d3d12 validation error.*$',log)
assert before['exitCode']==0 and before['debugErrors']==0 and len(messages)==14
for message in messages:
    assert 'BuildRaytracingAccelerationStructure' in message and 'VertexBuffer' in message
    addresses=re.search(r'range \[(0x[0-9a-f]+), (0x[0-9a-f]+)\]',message,re.I)
    assert addresses and int(addresses[2],16)-int(addresses[1],16)+1==10052812
assert not read(HERE/'as-validation-review.json')['previousDenseGroundTruthQualified']
for folder,label in [('rejected-device-removal','reference256'),('rejected-scheduled-controls','scheduled-controls')]:
    e,log,_=entry(folder,label);assert e['exitCode']==-1 and not e['fresh'] and e['abortReason']=='Unity D3D12Fence::Wait error'
    assert '18446744073709551615. Device removal' in log
progress=read(HERE/'rejected-scheduled-controls/nonterminal-progress.json');assert not progress['terminal'] and progress['completedBatches']==1024
for folder,needle in [('rejected-early-allocation','ReAllocateIfNeeded'),('rejected-missing-kernel',"Kernel 'Final' not found")]:
    e,log,d=entry(folder,'qualification',folder+'/qualification.json');assert e['exitCode']==1 and not e['pass'] and needle in log
recovery=read(HERE/'gpu-recovery.json');assert recovery['finished'] and recovery['exitCode']==0 and recovery['pass'] and recovery['fresh'] and recovery['fenceErrors']==0
assert sha(Path(recovery['log']))==recovery['logSha256'] and sha(HERE/'results/gpu-recovery.json')==recovery['reportSha256']
q=read(HERE/'results/gpu-recovery.json');assert len(q['samples'])==24 and q['longMedian']>2*q['shortMedian']
for s in q['samples']:assert 0<s['targetFence']<=s['completedFence']<2**64-1 and 0<s['begin']<=s['end'] and s['frequency']>0

e,log,_=entry('fixed-as-debug-original','fixed-as-debug');assert e['exitCode']==0 and e['debugErrors']==0 and not re.search(r'd3d12( validation)? (error|corruption)',log,re.I)
e,log,layout=entry('post-fix-original','canonical-layout');assert e['exitCode']==0 and layout['pass'] and layout['vertexBuffersUnchanged'] and layout['indexTuplesUnchanged'] and layout['normalizedSubmeshes']==14
raw=[r for r in layout['rows'] if r['baked'] and not r['canonical'] and not r['excludedSky']]
canonical=[r for r in layout['rows'] if r['canonical']]
assert len(raw)==14 and len(canonical)==15
assert all(r['baseVertex']>0 and r['baseOffsetEndBytes']>r['fullVertexBytes'] for r in raw)
assert all(r['baseVertex']==0 and r['baseOffsetEndBytes']<=r['fullVertexBytes'] for r in canonical)
assert all(r['meshVertices']==251321 and r['positionStride']==40 and r['fullVertexBytes']==10052840 for r in canonical)
for label in ['base-scene-rg','base-scene-compat','base-gi-hit']:
    e,log,d=entry('base-regressions-original',label);assert e['exitCode']==0 and e['pass'] and d['pass'] and e['debugErrors']==0
    assert not re.search(r'd3d12( validation)? (error|corruption)',log,re.I)
    assert len(d['checks'])==(141 if label=='base-gi-hit' else 145)
e,log,spatial=entry('post-fix-original','fixed-spatial');assert e['exitCode']==1 and e['debugErrors']==0 and not spatial['energyGatePass']
assert not spatial['spatialAudit']['full']['energyGatePass']

def dense(folder,label):
    e,log,d=entry(folder,label);a=d['denseReference']
    assert e['exitCode']==1 and e['debugErrors']==0 and e['denseFunctional'] and not re.search(r'd3d12( validation)? (error|corruption)',log,re.I)
    assert a['functionalPass'] and a['stableDomain'] and a['scheduledEditorTicks'] and a['mainFrames']==256
    assert (a['lightCount'],a['instances'],a['cutouts'],a['secondaryLightSamples'],a['secondaryEnvironmentSamples'])==(18,15,12,8,4)
    assert a['alphaVisibility'] and a['environment'] and a['maxDistance']==1000
    assert len(a['batches'])==a['batchCount'] and len({b['seed'] for b in a['batches']})==a['batchCount']
    assert all(b['applicationFrame']<c['applicationFrame'] and b['editorTimeSeconds']<c['editorTimeSeconds'] for b,c in zip(a['batches'],a['batches'][1:]))
    for i,p in enumerate(a['points']):
        assert p['samples']==a['batchCount']*a['samplesPerPoint'] and p['maxPrimaryError']<=.0005 and p['maxRayPrimaryError']<=.0005
        assert p['positiveSamples']==sum(b['positiveSamples'][i] for b in a['batches'])
        for c,key in enumerate(['x','y','z']):
            mean=sum(b['means'][i][key] for b in a['batches'])/a['batchCount']
            assert math.isclose(mean,p['referenceMean'][key],rel_tol=2e-6,abs_tol=1e-12)
    return a
a=dense('post-fix-original','fixed-reference');assert a['batchCount']==2048 and sum(p['converged'] for p in a['points'])==14
b=dense('fixed-controls-original','fixed-controls');assert b['batchCount']==16384 and len(b['points'])==2
assert b['points'][1]['converged'] and not b['points'][0]['converged']
assert b['points'][0]['batchMeanStandardError']['y']/b['points'][0]['referenceMean']['y']>.02

players=read(HERE/'player-run.json');assert players['activeProcess'] is None and len(players['entries'])==2
assert sha(ROOT/'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs')==players['runtimeProbeSha256']
assert sha(ROOT/'Editor/RTGI/HoRTGIPlayerValidation.cs')==players['builderSha256']
assert sha(ROOT/'Runtime/RayTracing/HoRTRayMeshIndices.cs')==players['normalizerSha256']
payloads=read(HERE/'player-payloads.json')
for e in players['entries']:
    assert e['buildExit']==e['playerExit']==0 and e['buildMarker'] and e['fresh'] and e['pass'] and e['baseVertexProbe'] and e['probeDllAbsent']
    assert sha(Path(e['buildLog']))==e['buildLogSha256'] and sha(Path(e['log']))==e['logSha256']
    p=HERE/'player-results'/(e['mode']+'.json');assert sha(p)==e['reportSha256'];d=read(p)
    assert d['pass'] and d['baseVertexProbe'] and d['renderCallbacks']==315 and len(d['checks'])==35
    root=Path(e['exe']).parent;assert not list(root.rglob('HoGIGpuProbe.dll'))
    for file in payloads[e['mode']]:assert sha(root/file['path'])==file['sha256']

evidence={'ownedSkinnedASRepairQualified':True,'vertexAndAbsoluteIndexTuplesPreserved':True,'preFixDenseGroundTruthQualified':False,'selectedConvergedPoints':15,'selectedPoints':16,'oneDarkGreenControlUncertain':True,'fullSceneQualityQualified':False,'normalAutomaticCameraQualified':False,'staticNonzeroBaseVertexQualified':False,'playerBaseVertexRegressions':2,'postFixFullImageError':spatial['spatialAudit']['full']['relativeErrorPercent']}
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified AS range repair 14->0, exact vertex/index semantics, Editor/native hit regressions, two 315-request base-vertex Players, and post-fix references: 15/16 selected points converged; full scene quality and one dark control remain unqualified.')
