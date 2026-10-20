"""Verify the complete-domain failure and a rejected scoped DI-derived candidate."""
import hashlib
import json
import math
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def v(p):return [p[k] for k in ['x','y','z']]
def near(a,b,tolerance=5e-10):assert all(abs(x-y)<=tolerance for x,y in zip(a,b)),(a,b)
review=read(HERE/'candidate-review.json');assert not review['candidateRetained'] and not review['runtimeChanged'] and review['scopeRestoredOriginal']
assert sha(ROOT/'Editor/RTGI/HoRTGICastleValidation.cs')==review['restoredBuilderSha256']
assert not (ROOT/'Editor/RTGI/HoRTGIClampingCandidate.cs').exists()
assert not (ROOT/'Editor/RTGI/HoRTGIClampingCandidate.cs.meta').exists()
evidence={'fullDomainQualityQualified':False,'phaseBiasExplainsOriginalFailure':False,'candidateAccepted':False,'runtimeChanged':False,'runs':[]}
raw_frames=None;baseline_candidate_reference=None
for folder in ['baseline-original','convex-original']:
    run=read(HERE/folder/'run.json');assert run['activeProcess'] is None
    assert len(run['entries'])==(2 if folder=='baseline-original' else 1)
    for s in run['sources']:
        p=ROOT/s['path'] if s['path'].endswith('.dll') else HERE/folder/'source'/s['path'];assert sha(p)==s['sha256'],s['path']
        if folder=='baseline-original' and not s['path'].endswith('.dll'):assert p.read_text(encoding='utf-8')==(ROOT/s['path']).read_text(encoding='utf-8'),s['path']
    for entry in run['entries']:
        assert entry['exitCode']==1 and entry['fresh'] and entry['marker'] and not entry['pass'] and not entry['energyGatePass'] and entry['spatialAuditComplete'] and entry['shaderCsDiagnostics']==0 and entry['abortReason'] is None
        assert sha(Path(entry['log']))==entry['logSha256'];log=Path(entry['log']).read_text(encoding='utf-8-sig');assert 'D3D12Fence::Wait' not in log and 'Castle filtered RGB time-mean failed original 5% energy gate' in log
        p=HERE/'results'/(entry['label']+'.json');assert sha(p)==entry['reportSha256'];d=read(p);a=d['spatialAudit']
        assert not d['energyGatePass'] and d['observedFrames']==a['frames']==256 and a['complete']
        assert a['width']==a['height']==256 and a['full']['pixels']==65536 and a['full']['samples']==16777216
        assert len(a['phases'])==16 and {(x['phaseX'],x['phaseY']) for x in a['phases']}=={(x,y) for x in range(4) for y in range(4)}
        for domain in [a['full']]+a['phases']:
            assert not domain['energyGatePass'] and domain['samples']==domain['pixels']*256
            assert domain['normalizedL1']>0 and 0<=domain['filteredVariance']<domain['rawVariance']
            raw,filtered=v(domain['raw']),v(domain['filtered']);near(v(domain['relativeErrorPercent']),[100*(b/r-1) for r,b in zip(raw,filtered)],2e-5)
            assert any(abs(b-r)>=max(.000005,abs(r)*.05) for r,b in zip(raw,filtered))
        assert sum(x['pixels'] for x in a['phases'])==65536
        for key in ['raw','filtered']:
            near(v(a['full'][key]),[sum(v(x[key])[c]*x['pixels'] for x in a['phases'])/65536 for c in range(3)])
        near(v(a['phases'][0]['raw']),v(d['staticRaw']));near(v(a['phases'][0]['filtered']),v(d['staticFiltered']))
        near(v(a['full']['raw']),[sum(v(x['raw'])[c] for x in a['frameMeans'])/256 for c in range(3)])
        frames=[x['raw'] for x in a['frameMeans']];assert len(frames)==256
        if raw_frames is None:raw_frames=frames
        else:assert raw_frames==frames
        assert [x['pipeline'] for x in a['stages']]==[-1,3,4,5,6,9,10]
        assert 'fast' in next(x['meaning'] for x in a['stages'] if x['pipeline']==5)
        near(v(a['stages'][0]['full']),v(a['full']['raw']),1e-7)
        assert len(a['largestLosses'])==len(a['largestGains'])==12
        point=a['largestLosses'][0];assert point['x']==87 and point['y']==165 and point['rawVariance']>point['filteredVariance']
        assert point['raw']['x']>point['filtered']['x']*5
        item={'label':entry['label'],'fullRelativeErrorPercent':a['full']['relativeErrorPercent'],'phaseZeroRelativeErrorPercent':a['phases'][0]['relativeErrorPercent'],'normalizedL1':a['full']['normalizedL1'],'filteredVariance':a['full']['filteredVariance'],'all16PhasesFail':True,'reportSha256':sha(p)}
        if entry['label']=='scale0125':baseline_candidate_reference=a
        if entry['label']=='convex0125':
            candidate=d['clampingCandidate'];assert candidate['restoredOriginal'] and candidate['replacementCount']==1
            assert sha(HERE/'candidate/HoRTGIClamping.hlsl')==candidate['bodySha256'];assert sha(HERE/'candidate/HoRTGIClamping.compute')==candidate['wrapperSha256']
            assert sha(HERE/'candidate/DIReferenceClamping.hlsl')==candidate['diReferenceSha256']
            assert sha(ROOT/'Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryClamping.cs.hlsl')==candidate['sourceSha256']
            assert baseline_candidate_reference is not None and a['full']['normalizedL1']>baseline_candidate_reference['full']['normalizedL1']
            assert all(x<y for x,y in zip(v(a['full']['relativeErrorPercent']),v(baseline_candidate_reference['full']['relativeErrorPercent'])))
            negatives=next(x['negativeComponents'] for x in a['stages'] if x['pipeline']==6)
            assert negatives<next(x['negativeComponents'] for x in baseline_candidate_reference['stages'] if x['pipeline']==6)
            item['negativeClampingComponents']=negatives
        evidence['runs'].append(item)
compile=read(HERE/'filtered-compile.json');assert compile['finished'] and compile['exitCode']==compile['csErrors']==compile['fenceErrors']==0 and sha(Path(compile['log']))==compile['logSha256']
evidence['filteredCompilePassed']=True;evidence['rawFrameMeansIdentical']=True
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified three real complete-image/16-phase failures, identical raw frame means, worsened rejected convex candidate, shader restoration and successful filtered-source Unity compile; original 5% gate is retained.')
