"""Validate captured application-frame/Player provenance after real runs."""
import hashlib
import json
from pathlib import Path
import re

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
run=read(HERE/'formal-run.json')
assert run['activeProcess'] is None and len(run['entries'])==2
assert sha(HERE/'HoRTGIRuntimeProbe.cs')==run['runtimeProbeSha256']
assert sha(ROOT/'Editor/RTGI/HoRTGIPlayerValidation.cs')==run['editorBuilderSha256']
native=sha(ROOT/'Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll')
evidence={'head':run['head'],'probeSha256':run['runtimeProbeSha256'],'builderSha256':run['editorBuilderSha256'],'runs':[]}
for entry in run['entries']:
    assert entry['buildExit']==0 and entry['buildMarker'] and entry['playerExit']==0 and entry['playerMarker'] and entry['reportFresh'] and entry['reportPass']
    for label in ('buildLog','log'):assert sha(Path(entry[label]))==entry[label+'Sha256']
    report=HERE/'results'/('linear-'+entry['mode']+'.json');assert sha(report)==entry['reportSha256']
    data=read(report);assert data['pass'] and data['api']=='Direct3D12' and data['colorSpace']=='Linear'
    assert data['compatibility']==(entry['mode']=='compat')
    assert len(data['frames'])>250 and data['renderCallbacks']==304
    for previous,current in zip(data['frames'],data['frames'][1:]):
        assert current['applicationFrame']>previous['applicationFrame'] and current['time']>previous['time']
    assert data['spatialRelativeL1']<.15 and data['filteredVariance']<data['rawVariance']*.4
    stationary=[frame for frame in data['frames'] if 52<=frame['sample']<180]
    means={kind:{channel:sum(frame[kind][channel] for frame in stationary)/len(stationary) for channel in ('r','g','b')} for kind in ('raw','filtered')}
    ratios={channel:means['filtered'][channel]/means['raw'][channel] for channel in ('r','b')}
    assert all(abs(value-1)<.05 for value in ratios.values())
    assert abs(means['filtered']['g']-means['raw']['g'])<.001
    assert data['reprojectedCameraFrames']>=55 and data['movingGeometryResetFrames']==24 and data['compositeError']<.01
    folder='PlayerGIDynamic' if entry['mode']=='rg' else 'PlayerGIDynamicCompatibility'
    build=ROOT/'.codex-research/RTGIValidation'/folder
    assert sha(build/'HoRTGIProbe.exe')==entry['exeSha256']
    assert sha(build/'HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll')==native==entry['nativeSha256']
    tree={str(p.relative_to(build)).replace('\\','/'):sha(p) for p in sorted(build.rglob('*')) if p.is_file()}
    record={'mode':entry['mode'],'reportSha256':sha(report),'applicationSamples':len(data['frames']),'cameraRequests':data['renderCallbacks'],'spatialRelativeL1':data['spatialRelativeL1'],'stationaryMean':means,'litChannelMeanRatios':ratios,'varianceRatio':data['filteredVariance']/data['rawVariance'],'elapsedObservedSeconds':data['frames'][-1]['time']-data['frames'][0]['time'],'playerFiles':tree}
    record['playerTreeSha256']=hashlib.sha256(json.dumps(tree,sort_keys=True).encode()).hexdigest().upper()
    log=Path(entry['log']).read_text(encoding='utf-8',errors='replace')
    assert not re.search(r'Shader error|InvalidOperationException|NullReferenceException',log)
    record['retainedRuntimeWarnings']=[line for line in log.splitlines() if 'unavailable because shader' in line or 'GarbageCollector disposing' in line]
    evidence['runs'].append(record)
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified both real Mono Players, distinct application frames, runtime contracts, logs and complete build payload hashes')
