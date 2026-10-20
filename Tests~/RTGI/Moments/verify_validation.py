"""Verify real slow-moment precision evidence without qualifying RGB quality."""
import hashlib
import json
import math
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
interpretation=read(HERE/'baseline-interpretation.json');assert interpretation['validMomentPipelines']==[4,6] and interpretation['excludedPipeline']==5
evidence={'slowMomentPrecisionDefectObserved':True,'physicalRawUnchanged':True,'rgbQualityQualified':False,'productionScaleChanged':False,'historyFixExcluded':True,'runs':[]}
baseline_frames=None
for folder,label,scale in [('baseline-original','baseline64',64),('scale1-original','scale1',1),('scale0125-original','scale0125',.125),('bindings-original','bindings',64)]:
    run=read(HERE/folder/'run.json');assert run['activeProcess'] is None and len(run['entries'])==1
    entry=run['entries'][0];assert entry['label']==label and entry['fresh'] and entry['shaderCsDiagnostics']==0 and entry['abortReason'] is None
    assert sha(Path(entry['log']))==entry['logSha256'];log=Path(entry['log']).read_text(encoding='utf-8-sig');assert 'D3D12Fence::Wait' not in log
    report=HERE/'results'/(label+'.json');assert sha(report)==entry['reportSha256'];d=read(report)
    for s in run['sources']:
        p=ROOT/s['path'] if s['path'].endswith('.dll') else HERE/folder/'source'/s['path']
        assert sha(p)==s['sha256'],s['path']
        if folder=='bindings-original' and not s['path'].endswith('.dll'):assert p.read_text(encoding='utf-8')==(ROOT/s['path']).read_text(encoding='utf-8'),s['path']
    if label=='bindings':
        assert entry['exitCode']==0 and entry['marker'] and d['pass']
        bindings={(b['pipeline'],b['name']):b for b in d['momentBindings']}
        slow=bindings[4,'gOut_Diff'];fast=bindings[4,'gOut_DiffFast'];fix=bindings[5,'gOut_Diff']
        assert slow['resourceType']==fast['resourceType']==fix['resourceType']=='TRANSIENT_POOL'
        assert slow['poolIndex']==bindings[5,'gIn_Diff']['poolIndex']==bindings[6,'gIn_Diff']['poolIndex']
        assert fast['poolIndex']==fix['poolIndex']==bindings[6,'gIn_DiffFast']['poolIndex']!=slow['poolIndex']
        assert bindings[6,'gOut_Diff']['resourceType']=='PERMANENT_POOL'
        evidence['actualBindingAliasesVerified']=True
        continue
    assert entry['exitCode']==1 and not entry['pass'] and d['momentAuditComplete'] and not d['energyGatePass'] and not d['pass']
    assert d['observedFrames']==256 and 'Castle filtered RGB time-mean failed original 5% energy gate' in log
    if label=='scale1':assert not entry['marker'] # original runner marker mismatch is retained, not rewritten
    else:assert entry['marker']
    raw_frames=[x['raw'] for x in d['frameMeans']];assert len(raw_frames)==256
    if baseline_frames is None:baseline_frames=raw_frames
    else:assert raw_frames==baseline_frames
    errors=[100*(d['staticFiltered'][c]/d['staticRaw'][c]-1) for c in ['x','y','z']]
    assert any(abs(x)>5 for x in errors)
    item={'label':label,'actualExitCode':entry['exitCode'],'signalScale':scale,'rgbRelativeErrorPercent':errors,'filteredVariance':d['filteredVariance'],'moments':[],'reportSha256':sha(report)}
    for stage in d['moments']:
        if stage['pipeline']==5:assert label=='baseline64';continue
        assert stage['pipeline'] in [4,6] and stage['signalScale']==scale
        t=stage['total'];assert t['samples']==1048576 and t['positiveMean']>0 and not t['momentSaturated']
        assert sum(x['samples'] for x in stage['bins'])==t['samples']
        assert sum(x['positiveMean'] for x in stage['bins'])==t['positiveMean']
        for key in ['zeroMomentWithPositiveMean','momentBelowSquared','severeMomentBelowSquared']:assert sum(x[key] for x in stage['bins'])==t[key]
        ratio=t['zeroMomentWithPositiveMean']/t['positiveMean']
        if scale==64:assert ratio>.98 and t['severeMomentBelowSquared']>180000
        elif scale==1:assert ratio<.008 and t['severeMomentBelowSquared']<70000
        else:assert ratio<.00004 and t['severeMomentBelowSquared']<100
        for point in stage['largestDeficits']:
            assert point['physicalDeficit']>0 and point['scaledMoment']>=0
            assert math.isclose(point['physicalSecondMoment'],point['scaledMoment']*scale*scale,rel_tol=1e-6,abs_tol=1e-12)
            assert math.isclose(point['physicalSquaredMean'],point['scaledLuminance']**2*scale*scale,rel_tol=1e-6,abs_tol=1e-12)
        item['moments'].append({'pipeline':stage['pipeline'],'zeroMomentPositiveFraction':ratio,'severeBelowSquaredSamples':t['severeMomentBelowSquared'],'samples':t['samples']})
    assert len(item['moments'])==2;evidence['runs'].append(item)
assert 'internal const float SignalScale=64;' in (ROOT/'Runtime/RTGI/NRD/HoRTGINrdContext.cs').read_text(encoding='utf-8')
assert 'fastSigma=2' in (ROOT/'Runtime/RTGI/NRD/HoRTGINrdContext.cs').read_text(encoding='utf-8')
assert 'float diffuse2ndMoment = diffuse1stMoment * diffuse1stMoment;' in (ROOT/'Runtime/RTDI/ThirdParty/NRD/RELAX_TemporalAccumulation.cs.hlsl').read_text(encoding='utf-8')
evidence['momentRatioImprovesButEnergyStillFails']=True
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified baseline plus two real unit experiments, identical 256 raw frame means, slow/fast actual descriptor aliases and moment precision improvement; all RGB energy gates still fail and production scale remains 64.')
