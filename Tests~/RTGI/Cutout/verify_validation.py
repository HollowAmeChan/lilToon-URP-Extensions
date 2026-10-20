"""Verify real Cutout artifacts; historical Editor snapshot and current Player inputs."""
import hashlib
import json
from pathlib import Path
import re

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
run=read(HERE/'results/run.json');snapshot=read(HERE/'snapshot.json')
assert run['activeProcess'] is None and len(run['entries'])==10 and sha(HERE/'snapshot.json')==run['snapshotSha256']
for source in snapshot['overlays']:
    assert sha(Path(snapshot['snapshot'])/source['path'])==source['sha256'],source['path']
    if source['path'].startswith('Runtime/'):
        assert sha(ROOT/source['path'])==source['sha256'],source['path']
summary={'editorSnapshotSha256':sha(HERE/'snapshot.json'),'editorProcesses':[],'playerProcesses':[]}
for entry in run['entries']:
    assert entry['exitCode']==0 and entry['marker'] and entry['shaderCsDiagnostics']==0,entry
    assert sha(Path(entry['log']))==entry['logSha256']
    result={'label':entry['label'],'exitCode':entry['exitCode']}
    if 'reportSha256' in entry:
        report=HERE/'results'/(entry['label']+'.json');assert entry['reportFresh'] and entry['reportPass'] and sha(report)==entry['reportSha256']
        data=read(report);result.update(reportSha256=sha(report),checks=len(data['checks']))
        if '-full-' in entry['label']:
            assert data['checkedSegments']==1024 and data['blockedSegments']==512
            assert abs(data['halfRaw']['r']-.5)<.025
            assert abs(data['halfFiltered']['r']/data['halfRaw']['r']-1)<.05
            result.update(frames=data['frames'],checkedSegments=data['checkedSegments'],blockedSegments=data['blockedSegments'],halfRaw=data['halfRaw']['r'],halfFiltered=data['halfFiltered']['r'])
    summary['editorProcesses'].append(result)
players=read(HERE/'player-run.json')
assert players['activeProcess'] is None and len(players['entries'])==2
assert sha(ROOT/'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs')==players['runtimeProbeSha256']
assert sha(ROOT/'Editor/RTGI/HoRTGIPlayerValidation.cs')==players['editorBuilderSha256']
for entry in players['entries']:
    assert entry['buildExit']==0 and entry['buildMarker'] and entry['playerExit']==0 and entry['playerMarker'] and entry['reportFresh'] and entry['reportPass']
    for name in ('buildLog','log'):assert sha(Path(entry[name]))==entry[name+'Sha256']
    report=HERE/'player-results'/('linear-'+entry['mode']+'.json');assert sha(report)==entry['reportSha256'];data=read(report)
    assert data['cutoutProbe'] and data['pass'] and data['api']=='Direct3D12' and data['renderCallbacks']==309 and len(data['frames'])==304
    assert data['compatibility']==(entry['mode']=='compat') and data['colorSpace']=='Linear'
    for a,b in zip(data['frames'],data['frames'][1:]):assert b['applicationFrame']>a['applicationFrame'] and b['time']>a['time']
    for sample in (304,307):
        frame=next(x for x in data['frames'] if x['sample']==sample)
        assert frame['reset'] and frame['raw']['b']<.0001 and frame['filtered']['b']<.0001
    folder='PlayerGICutout' if entry['mode']=='rg' else 'PlayerGICutoutCompatibility'
    build=ROOT/'.codex-research/RTGIValidation'/folder
    files={str(p.relative_to(build)).replace('\\','/'):sha(p) for p in sorted(build.rglob('*')) if p.is_file()}
    assert files['HoRTGIProbe.exe']==entry['exeSha256']
    assert files['HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll']==entry['nativeSha256']
    log=Path(entry['log']).read_text(encoding='utf-8',errors='replace');assert not re.search(r'Shader error|InvalidOperationException|NullReferenceException',log)
    stationary=[x for x in data['frames'] if 52<=x['sample']<180]
    meanRatios={c:sum(x['filtered'][c] for x in stationary)/sum(x['raw'][c] for x in stationary) for c in ('r','b')}
    assert all(abs(v-1)<.05 for v in meanRatios.values())
    summary['playerProcesses'].append({'mode':entry['mode'],'checks':len(data['checks']),'frames':len(data['frames']),'requests':data['renderCallbacks'],'reportSha256':sha(report),'meanRatios':meanRatios,'payloadFiles':files,'runtimeWarnings':[line for line in log.splitlines() if 'unavailable because shader' in line or 'GarbageCollector disposing' in line]})
guids={}
for folder in ('Runtime/RTGI','Runtime/RayTracing','Editor/RTGI'):
    for file in (ROOT/folder).rglob('*.meta'):
        match=re.search(r'^guid: ([a-f0-9]{32})$',file.read_text(encoding='utf-8'),re.M);assert match,file
        assert match[1] not in guids,(file,guids.get(match[1]));guids[match[1]]=str(file)
(HERE/'evidence.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print('Verified immutable Editor inputs, current production/Player inputs, 10 Editor exits and both real 309-request Mono Players')
