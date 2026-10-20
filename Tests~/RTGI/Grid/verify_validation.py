"""Validate measured receiver/grid contracts and retain the exact rejected edge fixtures."""
import hashlib
import json
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
run=read(HERE/'run.json')
assert run['activeProcess'] is None and len(run['entries'])==2
for source in run['sources']:
    archive=HERE/'source-at-run'/Path(source['path']).name
    assert sha(archive)==source['sha256']
    assert archive.read_text(encoding='utf-8')==(ROOT/source['path']).read_text(encoding='utf-8')
limits={'positionError':.002,'normalError':.012,'depthError':.002,'primaryUvError':1e-6,'gridUvError':2e-5,'nrdPositionError':.002,'previousPositionError':.002,'previousNrdPositionError':.002,'temporalUvError':2e-5}
names=['full-perspective','half-perspective','half-moving-camera','half-offcenter-resize','half-offcenter-moving','half-orthographic','half-orthographic-offcenter-moving','odd-full-fallback','single-column-full-fallback','single-row-full-fallback','full-restored','full-moving-camera']
history_names={'half-moving-camera','half-offcenter-moving','half-orthographic-offcenter-moving','full-moving-camera'}
evidence={'gridContractQualified':True,'productionHalfGiImplemented':False,'nrdFilteringQualityQualified':False,'actual1080Qualified':False,'runs':[],'rejectedAttempts':[]}
for entry in run['entries']:
    assert entry['exitCode']==0 and entry['marker'] and entry['fresh'] and entry['pass'] and entry['shaderCsDiagnostics']==0 and entry['abortReason'] is None
    log=Path(entry['log']);assert sha(log)==entry['logSha256'] and 'D3D12Fence::Wait' not in log.read_text(encoding='utf-8-sig')
    report=HERE/'results'/(entry['label']+'.json');assert sha(report)==entry['reportSha256'];d=read(report)
    assert d['pass'] and d['api']=='Direct3D12' and d['renderRequests']==12 and [c['name'] for c in d['cases']]==names
    assert d['compatibility']==(entry['label']=='compat')
    assert sum(c['pixels'] for c in d['cases'])==18190
    assert sum(c['historyPixels'] for c in d['cases'])==5961
    assert sum(c['foregroundPixels'] for c in d['cases'])==1443
    assert sum(c['rasterAmbiguousPixels'] for c in d['cases'])==2
    for c in d['cases']:
        assert c['scale']==(2 if c['name'].startswith('half-') else 1)
        assert c['historyAllowed']==(c['name'] in history_names)
        assert (c['historyPixels']>0)==c['historyAllowed']
        for key,limit in limits.items():assert 0<=c[key]<=limit,(entry['label'],c['name'],key,c[key])
        if c['scale']==2:assert c['uncompensatedNrdError']>.005 and c['unmappedPrimaryError']>.1
        else:assert c['uncompensatedNrdError']<=.002 and c['unmappedPrimaryError']<=.002
    evidence['runs'].append({'label':entry['label'],'cases':len(d['cases']),'pixels':sum(c['pixels'] for c in d['cases']),'historyPixels':sum(c['historyPixels'] for c in d['cases']),'maxErrors':{key:max(c[key] for c in d['cases']) for key in limits},'negativeControlMissingProjectionMinError':min(c['uncompensatedNrdError'] for c in d['cases'] if c['scale']==2),'negativeControlMissingMappingMinError':min(c['unmappedPrimaryError'] for c in d['cases'] if c['scale']==2),'reportSha256':sha(report)})
for folder in ['rejected-initial','rejected-edge-diagnostic']:
    r=read(HERE/folder/'run.json');assert r['activeProcess'] is None and len(r['entries'])==1
    entry=r['entries'][0];assert entry['exitCode']==1 and not entry['pass'] and entry['fresh']
    assert sha(Path(entry['log']))==entry['logSha256']
    p=HERE/folder/'rg.json';assert sha(p)==entry['reportSha256'];d=read(p)
    assert not d['pass'] and d['cases'][-1]['name']=='full-moving-camera' and d['cases'][-1]['positionError']>1
    for source in r['sources']:assert sha(HERE/folder/'source'/Path(source['path']).name)==source['sha256']
    evidence['rejectedAttempts'].append({'name':folder,'exitCode':entry['exitCode'],'reportSha256':sha(p),'reason':'CPU centre just outside foreground silhouette; GPU raster covers foreground. Final fixture permits both independent ray/plane candidates only in a measured 1/256-pixel edge band.'})
assert not read(HERE/'../Scene/evidence.json')['qualityQualified']
assert not read(HERE/'../Resources/evidence.json')['actual1080Qualified']
edge=read(HERE/'edge-analysis.json');assert edge['outsideLocalX']>0 and 0<edge['horizontalPixelDistanceToProjectedEdge']<edge['finalFixtureEdgeBandPixels']==1/256
assert '[HoGIGrid] DIAG full-moving-camera pixel=18,31' in Path(read(HERE/'rejected-edge-diagnostic/run.json')['entries'][0]['log']).read_text(encoding='utf-8-sig')
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified two actual 12-request GPU grid audits, 18,190 receivers and 5,961 previous-depth samples each, negative controls, and both rejected edge attempts. Production half GI/filter quality/HD remain unqualified.')
