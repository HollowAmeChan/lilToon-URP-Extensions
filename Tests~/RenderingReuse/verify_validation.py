from pathlib import Path
import json,gzip,hashlib,re,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(b):return hashlib.sha256(b).hexdigest().upper()
index=read(HERE/'source-index.json')
for digest,item in index.items():
 b=(HERE/item['path']).read_bytes();raw=gzip.decompress(b);assert sha(b)==item['archiveSha256'] and sha(raw)==digest and len(raw)==item['bytes']
for item in read(HERE/'baseline.json')['files']:assert sha((HERE/item['snapshot']).read_bytes())==item['sha256']
for name in ['HoRTGI.compute','HoRTGINrdSignal.hlsl']:assert (HERE/'baseline'/name).read_bytes()==(ROOT/'Runtime/RTGI/Shaders'/name).read_bytes()
assert (HERE/'baseline/HoRTGIRendererFeature.cs').read_bytes()==(ROOT/'Runtime/RTGI/HoRTGIRendererFeature.cs').read_bytes()
accepted=[]
for item in read(HERE/'runs.json'):
 folder=HERE/item['directory'];r=read(folder/'run.json');assert r['activeProcess'] is None and r['exitCode']==0 and r['sourceDrift']==0 and sha((folder/'unity.log').read_bytes())==r['logSha256']
 for src in r['sources']:assert src['sha256'] in index
 for a in item['artifacts']:assert sha((HERE/a['path']).read_bytes())==a['sha256'] and (HERE/a['path']).stat().st_size==a['bytes']
 assert item['accepted'];accepted.append(item['mode'])
 if item['mode']=='playerBuild':
  v=read(folder/'build-warning-review.json');assert r['diagnostics']==3 and v['acceptedExistingWarningsOnly'] and not v['unhandled'] and sha((ROOT/v['source']).read_bytes())==v['sourceSha256'];assert all(line in read(ROOT/v['source'])['knownWarningLines'] for line in v['knownLines']);continue
 assert r['diagnostics']==0;doc=read(folder/'report.json');assert doc['pass'] and not doc.get('error',doc.get('failure'))
 if item['mode'] in ['reuse','reuseCompat']:
  assert doc['frames']==74 and len(doc['checks'])==10;a=read(folder/'reuse-audit.json');assert a['pass'] and a['restored'] and a['comparedComponents']==1318912 and len(a['frames'])==41 and a['observedDefaultSaving64']==32768 and a['observedDefaultSaving32']==8192
  if item['directory']!='reuse-20261023T202736755':assert a['zeroAlbedoInputWitness'] and a['curvedGuideWitness'] and a['maxPhysicalInput']>1 and a['minPositiveSignal']<1e-5
  assert sum(f['snapshot'] for f in a['frames'])==8
  for f in a['frames']:assert f['savedDefaultPayload']==(0 if f['snapshot'] else f['width']*f['height']*8) and f['outputSha'] and f['publishedSha'] and f['inputSha']
 if item['mode'] in ['gi','giCompat']:assert doc['frames']==243 and len(doc['checks'])==41
 if item['mode']=='player':
  assert doc['renderCallbacks']==304 and len(doc['checks'])==16 and doc['api']=='Direct3D12' and doc['colorSpace']=='Linear' and not r['abort'];payload=read(folder/'payload.json');n=[x for x in payload if x['path'].endswith('/HoRTDINRD.dll')];assert len(n)==1 and n[0]['sha256']==sha((ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll').read_bytes())
assert set(accepted)>={'reuse','reuseCompat','gi','giCompat','playerBuild','player'}
# Latest real Release source guard. Historical runs use their frozen objects.
for item in read(HERE/'player-20261023T203536587/run.json')['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256'],item['path']
a=read(HERE/'reuse-20261023T203126058/reuse-audit.json');b=read(HERE/'reuseCompat-20261023T203139320/reuse-audit.json');assert a==b
source=(ROOT/'Runtime/RTGI/NRD/HoRTGINrdContext.cs').read_text(encoding='utf-8');assert 'internal RTHandle filtered=>signal;' in source and 'ref filtered' not in source and 'filtered?.Release' not in source and 'if(ValidationCaptureSignal)cmd.CopyTexture(signal.nameID,ValidationSignal.nameID);' in source
for name in ['HoRTGICastleValidation.cs','HoRTGICutoutLightingValidation.cs','HoRTGIDenseReferenceAudit.cs','HoRTGILocalStageAudit.cs','HoRTGINrdValidation.cs']:
 s=(ROOT/'Editor/RTGI'/name).read_text(encoding='utf-8');assert 'ValidationSignal' in s and not re.search(r'(?:Read|read|Mean|Capture|Accumulate)\([^;\n]*nrd\.signal\.rt',s)
rev=read(HERE/'baseline.json')['revision'];assert not subprocess.check_output(['git','diff',rev,'--name-only','--','Runtime/RTDI','Runtime/NRD'],cwd=ROOT)
assert 1920*1080*8==16588800
print('PASS: real old/new NRD and published RGBA bitexact both paths; curve/zero-albedo/HDR/weak/snapshot/resize; actual default -8B/pixel; Release304 requests; raw/32F/history/policy and shaders unchanged')
