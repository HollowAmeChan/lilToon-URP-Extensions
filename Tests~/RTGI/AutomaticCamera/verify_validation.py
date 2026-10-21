"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
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
bad=r'd3d12(?: validation)? (?:error|corruption)|Shader error|error CS|Compute shader .*Property|UAV.*(?:error|not set)|D3D12Fence::Wait'
known=read(HERE/'build-warning-review.json')['knownWarningLines']
for name,report,created in [('rejected-uncreated-target/run.json','rejected-uncreated-target/player-results/rg/report.json',False),('player-run.json','player-results/rg/report.json',True),('compat-visible-run.json','visible-results/compat/report.json',True)]:
    run=read(HERE/name); assert run['activeProcess'] is None and run['debugLayerRequested']
    e=run['entries'][0]
    for s in run['sources']: assert s['sha256'] in index
    assert e['buildExit']==0 and e['buildAbort'] is None and e['buildMarker']
    assert len(e['buildKnownWarnings'])==5 and not e['buildUnhandledDiagnostics']
    for key in ['buildLog','log']:
        item=logs[e[key]]; assert item['sha256']==e[key+'Sha256']
        log=(HERE/item['path']).read_text(encoding='utf-8-sig'); assert not re.search(bad,log,re.I)
        if key=='buildLog': assert [v for v in log.splitlines() if 'Shader warning' in v]==e['buildKnownWarnings'] and all(v in known for v in e['buildKnownWarnings'])
        else: assert 'Shader warning' not in log
    raw=(HERE/report).read_bytes(); assert sha(raw)==e['reportSha256']; r=json.loads(raw.decode('utf-8-sig'))
    assert r['targetCreated']==created and r['cameraEnabled'] and not r['batchMode'] and r['api']=='Direct3D12'
    assert e['sourceDrift']==0 and e['playerAbort'] is None
    if name!='compat-visible-run.json':
        assert e['playerExit']==1 and not r['pass'] and r['renderedCameras']==0 and r['updates']==180 and not r['frames']
        assert 'no new automatic camera completion' in r['error']
    else: assert e['playerExit']==0 and e['pass'] and e['diagnostics']==0 and e['elapsedSeconds']<15
visible=read(HERE/'visible-run.json'); hidden=read(HERE/'player-run.json')
assert visible['hiddenRunSha256']==sha((HERE/'player-run.json').read_bytes())
assert visible['exeSha256']==hidden['entries'][0]['exeSha256'] and visible['exit']==0 and visible['abort'] is None and visible['elapsedSeconds']<15
assert visible['windowStyle']=='Normal' and sha((HERE/'visible-results/rg/report.json').read_bytes())==visible['reportSha256']
assert visible['logSha256']==logs['visible-rg']['sha256']
assert not re.search(bad+'|Shader warning',(HERE/'visible-results/rg/player.log').read_text(encoding='utf-8-sig'),re.I)
summary={}
for mode in ['rg','compat']:
    r=read(HERE/f'visible-results/{mode}/report.json'); f=r['frames']
    assert r['pass'] and r['compatibility']==(mode=='compat') and r['cameraEnabledAtStart'] and r['targetAtStart']
    assert r['renderedCameras']==240 and r['updates']==241 and len(f)==240 and len(r['checks'])==10
    assert all(v['sample']==i and v['cameraEnabled'] and v['applicationFrame']==v['renderFrame']+1 and v['rendered']==i+1 for i,v in enumerate(f))
    assert all(f[i+1]['renderFrame']==f[i]['renderFrame']+1 for i in range(239))
    assert all(math.isfinite(v[k][c]) for v in f for k in ['cameraColor','raw','filtered'] for c in 'rgb')
    basis=f[31]['cameraColor']; errors={}
    for n,amount in [(95,.25),(127,1)]:
        error=max(abs(f[n]['cameraColor'][c]-basis[c]-amount*f[n]['filtered'][c]) for c in 'rgb'); assert error<.015; errors[str(amount)]=error
    assert all(not v['active'] and max(abs(v['cameraColor'][c]-basis[c]) for c in 'rgb')<.01 for v in f[128:160])
    assert f[160]['active'] and f[160]['reset'] and sum(not v['reset'] for v in f[180:210])>=27
    assert f[210]['reset'] and f[210]['raw']['r']<.005 and f[210]['filtered']['r']<.005
    summary[mode]={'events':len(f),'reprojected':r['reprojectedFrames'],'additiveMaxRgbError':errors,'screen':[r['screenWidth'],r['screenHeight']]}
probe=(ROOT/'Tests~/RTGI/Dynamic/HoRTGIAutomaticProbe.cs').read_text(encoding='utf-8')
assert not re.search(r'\b(?:SubmitRenderRequest|RenderSingleCamera|\.Render|\.UpdateVolumeStack)\s*\(',probe)
assert 'endCameraRendering+=' in probe and 'targetTexture.Create()' in probe
for mode,item in read(HERE/'payload-verification.json').items():
    value=(HERE/item['manifest']).read_bytes(); assert sha(value)==item['sha256']
    files=json.loads(value.decode('utf-8-sig')); assert len(files)==item['files'] and item['verifiedAfterVisibleTest']
    assert not any(Path(v['path']).name=='HoGIGpuProbe.dll' for v in files)
print(json.dumps(summary,indent=2)); print('PASS: automatic target-texture cameras; hidden failures retained; screen presentation remains unverified')
