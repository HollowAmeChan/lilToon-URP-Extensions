"""Verify captured skinned GI evidence and immutable source/package inputs."""
from pathlib import Path
import hashlib
import json
import re

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
r=read(HERE/'results/run.json');s=read(HERE/'snapshot.json')
assert r['activeProcess'] is None and len(r['entries'])==6 and sha(HERE/'snapshot.json')==r['snapshotSha256']
for source in s['overlays']:assert sha(Path(s['snapshot'])/source['path'])==source['sha256'],source['path']
summary={'editorSnapshotSha256':sha(HERE/'snapshot.json'),'editor':[],'players':[]}
for e in r['entries']:
    assert e['exitCode']==0 and e['marker'] and e['reportFresh'] and e['reportPass'] and e['shaderCsDiagnostics']==0
    assert sha(Path(e['log']))==e['logSha256']
    report=HERE/'results'/(e['label']+'.json');assert sha(report)==e['reportSha256'];d=read(report)
    assert d['pass'];summary['editor'].append({'label':e['label'],'checks':len(d['checks']),'frames':d.get('frames'),'bakes':d.get('bakes'),'reportSha256':sha(report)})
p=read(HERE/'player-run.json');assert p['activeProcess'] is None and len(p['entries'])==2 and p['step']==9
assert sha(ROOT/'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs')==p['runtimeProbeSha256']
assert sha(ROOT/'Editor/RTGI/HoRTGIPlayerValidation.cs')==p['editorBuilderSha256']
for e in p['entries']:
    assert e['buildExit']==0 and e['buildMarker'] and e['playerExit']==0 and e['playerMarker'] and e['reportFresh'] and e['reportPass']
    for key in ('buildLog','log'):assert sha(Path(e[key]))==e[key+'Sha256']
    report=HERE/'player-results'/('linear-'+e['mode']+'.json');assert sha(report)==e['reportSha256'];d=read(report)
    assert d['pass'] and d['skinnedProbe'] and d['cutoutProbe'] and d['renderCallbacks']==315 and len(d['frames'])==310
    assert d['api']=='Direct3D12' and d['colorSpace']=='Linear' and d['compatibility']==(e['mode']=='compat')
    for a,b in zip(d['frames'],d['frames'][1:]):assert b['applicationFrame']>a['applicationFrame'] and b['time']>a['time']
    for sample in (309,311,313):
        x=next(x for x in d['frames'] if x['sample']==sample);assert x['reset'] and x['raw']['b']<.0001 and x['filtered']['b']<.0001
    for sample in (310,312,314):
        x=next(x for x in d['frames'] if x['sample']==sample);assert x['reset'] and x['raw']['b']>.05 and x['filtered']['b']>.05
    folder='PlayerGISkinned' if e['mode']=='rg' else 'PlayerGISkinnedCompatibility';build=ROOT/'.codex-research/RTGIValidation'/folder
    files={str(x.relative_to(build)).replace('\\','/'):sha(x) for x in sorted(build.rglob('*')) if x.is_file()}
    assert files['HoRTGIProbe.exe']==e['exeSha256'] and files['HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll']==e['nativeSha256']
    log=Path(e['log']).read_text(encoding='utf-8',errors='replace');assert not re.search(r'Shader error|NullReferenceException|InvalidOperationException',log)
    summary['players'].append({'mode':e['mode'],'requests':d['renderCallbacks'],'samples':len(d['frames']),'checks':len(d['checks']),'reportSha256':sha(report),'payloadFiles':files,'runtimeWarnings':[x for x in log.splitlines() if 'unavailable because shader' in x or 'GarbageCollector disposing' in x]})
(HERE/'evidence.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print('Verified six Editor runs and both real 315-request skinned Mono Players, source/log/report and complete payload hashes')
