"""Check default-screen automatic events, decoded captures and original failure."""
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

missing=read(HERE/'rejected-missing-module/run.json'); assert missing['activeProcess'] is None
original=missing['entries'][0]; assert original['buildExit']==1 and not original['buildMarker'] and len(original['buildUnhandledDiagnostics'])==3
assert all('error CS0103' in x and 'ScreenCapture' in x for x in original['buildUnhandledDiagnostics'])
assert not (HERE/'rejected-missing-module/player-results').exists()
assert 'com.unity.modules.screencapture' not in read(HERE/'rejected-missing-module/project-manifest.json')['dependencies']
summary={}
for mode in ['rg','compat']:
    run=read(HERE/f'{mode}-run.json');assert run['activeProcess'] is None and run['step']==25 and run['debugLayerRequested']
    e=run['entries'][0];assert e['buildExit']==e['playerExit']==0 and e['buildAbort'] is None and e['playerAbort'] is None
    assert e['fresh'] and e['pass'] and e['marker'] and e['automaticCameras']==240
    assert e['debugErrors']==e['diagnostics']==e['sourceDrift']==0 and e['probeDllAbsent']
    assert len(e['buildKnownWarnings'])==5 and not e['buildUnhandledDiagnostics'] and all(x in known for x in e['buildKnownWarnings'])
    for source in run['sources']:assert source['sha256'] in index
    manifest=next(x for x in run['sources'] if x['path'].endswith('Packages/manifest.json'));mi=index[manifest['sha256']]
    assert read(HERE/mi['path'])['dependencies']['com.unity.modules.screencapture']=='1.0.0'
    for key in ['buildLog','log']:
        item=logs[e[key]];assert item['sha256']==e[key+'Sha256']
        value=(HERE/item['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,value,re.I)
        if key=='buildLog':assert [x for x in value.splitlines() if 'Shader warning' in x]==e['buildKnownWarnings']
        else:
            assert 'Shader warning' not in value
            notices=[v for v in value.splitlines() if 'unavailable because shader' in v]
            assert notices==[
                "GeometryBuffer fallback output is unavailable because shader 'Hidden/lilToon/URP/GeometryBuffer/Fallback' could not be found.",
                "GeometryBuffer MSAA resolve is unavailable because shader 'Hidden/lilToon/URP/GeometryBuffer/Resolve' could not be found."]
    value=(HERE/f'player-results/{mode}/report.json').read_bytes();assert sha(value)==e['reportSha256'];r=json.loads(value.decode('utf-8-sig'));f=r['frames']
    assert r['pass'] and not r['batchMode'] and r['api']=='Direct3D12' and r['compatibility']==(mode=='compat')
    assert r['cameraEnabledAtStart'] and not r['targetAtStart'] and r['cameraEnabled'] and not r['targetCreated']
    assert r['screenWidth']==r['screenHeight']==512 and r['updates']==241 and r['renderedCameras']==len(f)==240 and len(r['checks'])==11
    assert all(v['sample']==i and v['cameraEnabled'] and v['applicationFrame']==v['renderFrame']+1 and v['rendered']==i+1 for i,v in enumerate(f))
    assert all(f[i+1]['renderFrame']==f[i]['renderFrame']+1 for i in range(239))
    assert all(math.isfinite(v[k][c]) for v in f for k in ['cameraColor','raw','filtered'] for c in 'rgb')
    basis=f[31]['cameraColor'];assert max(abs(basis[c]-.2158203125) for c in 'rgb')<.01
    errors={}
    for n,amount in [(95,.25),(127,1)]:
        error=max(abs(f[n]['cameraColor'][c]-basis[c]-amount*f[n]['filtered'][c]) for c in 'rgb');assert error<.015;errors[str(amount)]=error
    assert all(not v['active'] and max(abs(v['cameraColor'][c]-basis[c]) for c in 'rgb')<.01 for v in f[128:160])
    assert f[160]['active'] and f[160]['reset'] and r['reprojectedFrames']==30 and all(not v['reset'] for v in f[180:210])
    assert f[210]['reset'] and f[210]['raw']['r']<.005 and f[210]['filtered']['r']<.005
    summary[mode]={'events':240,'linearRaster':basis['r'],'additiveMaxRgbError':errors,'measuredFrameSpanSeconds':f[-1]['time']-f[0]['time']}
probe=(ROOT/'Tests~/RTGI/Dynamic/HoRTGIBackbufferProbe.cs').read_text(encoding='utf-8')
assert not re.search(r'\b(?:SubmitRenderRequest|RenderSingleCamera|\.Render|\.UpdateVolumeStack)\s*\(',probe)
assert 'new WaitForEndOfFrame()' in probe and 'pixels[y*screenshot.width+x].linear' in probe and 'capturedFrame!=lastRenderFrame' in probe
assert 'CaptureScreenshotAsTexture()' in probe and 'targetTexture.Create()' not in probe
for mode,item in read(HERE/'payload-verification.json').items():
    value=(HERE/item['manifest']).read_bytes();assert sha(value)==item['sha256'];files=json.loads(value.decode('utf-8-sig'))
    assert len(files)==item['files'] and item['verifiedAfterVisibleTest'] and not any(Path(x['path']).name=='HoGIGpuProbe.dll' for x in files)
images=read(HERE/'image-artifacts.json');assert len(images)==10
for name,item in images.items():
    value=(HERE/name).read_bytes();assert sha(value)==item['sha256'] and len(value)==item['bytes'] and item['width']==item['height']==512
for name,item in read(HERE/'environment-index.json').items():assert sha((HERE/item['path']).read_bytes())==item['sha256']
print(json.dumps(summary,indent=2));print('PASS: automatic default-screen cameras; decoded screen publication and original compiler failure preserved')
