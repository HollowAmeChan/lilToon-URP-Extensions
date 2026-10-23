"""Check default-screen automatic events, decoded captures and original failure."""
import hashlib
import json
import math
import re
import subprocess
import gzip
from analyze import raw,summarize
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
bad=r'd3d12(?: validation)? (?:error|corruption)|Shader error|error CS|Compute shader .*Property|UAV.*(?:error|not set)|D3D12Fence::Wait'
known=read(HERE.parent/'Backbuffer/build-warning-review.json')['knownWarningLines']
for mode in ['rg','compat']:
    run=read(HERE/(mode+'-run.json'));assert run['activeProcess'] is None and run['step']==40 and len(run['entries'])==1
    e=run['entries'][0];assert e['buildExit']==e['playerExit']==0 and e['buildAbort'] is None and e['playerAbort'] is None and e['buildMarker'] and e['marker'] and e['fresh'] and e['pass']
    assert e['buildDebugErrors']==e['debugErrors']==e['diagnostics']==e['sourceDrift']==0 and e['buildDiagnostics']==5 and sorted(e['buildKnownWarnings'])==sorted(known) and not e['buildUnhandledDiagnostics']
    for item in run['sources']:assert item['sha256'] in index
    for k in ['buildLog','log']:
        b=(HERE/logs[e[k]]['path']).read_bytes();assert sha(b)==e[k+'Sha256'] and not re.search(bad,b.decode('utf-8-sig'),re.I)
    d=read(HERE/f'player-results/{mode}/report.json');assert sha(raw(HERE/f'player-results/{mode}/report.json'))==e['reportSha256'] and d['pass'] and not d['error'] and not d['batchMode'] and d['compatibility']==(mode=='compat')
    assert d['api']=='Direct3D12' and d['unity']=='6000.3.15f1' and d['width']==d['height']==512 and d['rendered']==e['automaticCameras']==len(d['frames'])==240 and len(d['checks'])==22
    assert 0<d['elapsed']<8 and d['motionFrames']==d['motionResetFrames']==48 and d['initialInstances']==8
    for i,f in enumerate(d['frames']):
        assert f['sample']==i and f['captureFrame']==f['renderFrame'] and 1<=f['applicationFrame']-f['renderFrame']<=2
        assert f['instances']==8 and f['cutouts']==1 and f['snapshots']==3 and f['alphaVisibility']
        for field in ['raw','filtered','screen']:assert len(f[field])==3 and all(math.isfinite(c[k]) for c in f[field] for k in 'rgba')
    payload=read(HERE/f'player-results/{mode}-payload.json');assert e['probeDllAbsent'] and not any(Path(x['path']).name=='HoGIGpuProbe.dll' for x in payload)
    proof=read(HERE/'payload-verification.json')[mode];assert proof['files']==len(payload) and proof['verifiedAfterVisibleTest'] and proof['sha256']==sha((HERE/proof['manifest']).read_bytes())
    exe=next(x for x in payload if x['path']=='HoRTGIProbe.exe');assert exe['sha256']==e['exeSha256']
for name,item in read(HERE/'capture-archives.json').items():
    b=(HERE/item['path']).read_bytes();v=gzip.decompress(b);assert sha(b)==item['archiveSha256'] and sha(v)==item['rawSha256'] and len(v)==item['rawBytes']
images=read(HERE/'image-artifacts.json');assert len(images)==18
for name,item in images.items():
    b=(HERE/name).read_bytes();assert sha(b)==item['sha256'] and len(b)==item['bytes'] and b[:8]==b'\x89PNG\r\n\x1a\n' and int.from_bytes(b[16:20],'big')==int.from_bytes(b[20:24],'big')==512
original=read(HERE/'initial-metadata/rg-run.json');corrected=read(HERE/'rg-run.json');assert len(original['entries'])==2 and original['entries'][0]==original['entries'][1]==corrected['entries'][0]
assert original['sources']==corrected['sources'] and sha((HERE/'initial-metadata/rg-run.json').read_bytes())==corrected['metadataCorrection']['originalSha256']
# Current wrapper is the later corrected compat source. Earlier successful RG source stays archived.
for item in read(HERE/'compat-run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256']
review=read(HERE/'reference-review.json')
for item in review['files']:assert sha((HERE/item['path']).read_bytes())==item['sha256']
probe=(ROOT/'Tests~/RTGI/ComplexMotion/HoRTGIComplexMotionProbe.cs').read_text(encoding='utf-8-sig');assert not re.search(r'\.Render\s*\(|SubmitRenderRequest|VolumeManager.*Update',probe)
a=summarize();assert a==read(HERE/'analysis.json') and not a['productionChanged']
for mode,b in a['backends'].items():
    assert b['cameraFrames']==240 and b['activeGiFrames']==224 and b['activeGiFrameSequenceContiguous'] and b['callbackFramesContiguous'] and b['captureMatchesEveryCameraFrame']
    assert max(b['allRgbCompositeMaxErrors'].values())<.015 and b['volumeOffRasterMaxError']<.01 and max(b['firstEditedFrameOldColourMaxima'].values())<.005
    assert b['motionNrdResetFrames']==48 and b['motionRawTemporalUsedFrames']==0 and b['motionBakeDelta']==48 and b['stationaryBakeDelta']==0 and b['stationaryTemporalResumed'] and b['reEnabledReset']
assert a['crossBackend']['raw']['bitwiseEquivalentReportedFloatValues']
print('PASS: two actual Release Players240 native frames/224 GI updates each;8 fixed instances/3 skins;allRGB screen/Cutout/emission/Volume;48 global resets recorded, initial metadata correction preserved, exact sources/payloads/18 PNG')
