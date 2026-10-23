"""Independent immutable evidence, layer/ABI checks and measured pool accounting."""
from pathlib import Path
import hashlib,json,gzip,re,subprocess,collections
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
def sha(b):return hashlib.sha256(b).hexdigest().upper()
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def code(p):
 s=p.read_text(encoding='utf-8');s=re.sub(r'"(?:\\.|[^"\\])*"','""',s);return re.sub(r'//[^\n]*|/\*.*?\*/','',s,flags=re.S)
index=read(HERE/'source-index.json')
for digest,item in index.items():
 b=(HERE/item['path']).read_bytes();raw=gzip.decompress(b);assert sha(b)==item['archiveSha256'] and sha(raw)==digest and len(raw)==item['bytes']
baseline=read(HERE/'baseline.json');rev=baseline['revision']
for item in baseline['files']:assert sha((HERE/item['snapshot']).read_bytes())==item['sha256']
for item in read(HERE/'initial-compile/sources.json'):assert item['sha256'] in index
assert read(HERE/'initial-compile/outcome.json')['actualExitCode']==1
assert 'error CS0246' in (HERE/'initial-compile/unity.log').read_text(encoding='utf-8-sig')
accepted=[]
for item in read(HERE/'runs.json'):
 folder=HERE/item['directory'];run=read(folder/'run.json');assert run['activeProcess'] is None and run['sourceDrift']==0 and sha((folder/'unity.log').read_bytes())==run['logSha256']
 for source in run['sources']:assert source['sha256'] in index
 for artifact in item['artifacts']:assert sha((HERE/artifact['path']).read_bytes())==artifact['sha256'] and (HERE/artifact['path']).stat().st_size==artifact['bytes']
 if item['accepted']:
  if item['mode']=='playerBuild':
   review=read(folder/'build-warning-review.json');assert run['exitCode']==0 and run['diagnostics']==3 and review['acceptedExistingWarningsOnly'] and not review['unhandled'];assert sha((ROOT/review['source']).read_bytes())==review['sourceSha256'];known=read(ROOT/review['source'])['knownWarningLines'];assert all(x in known for x in review['knownLines']);assert '[HoGIPlayerBuild] PASS' in (folder/'unity.log').read_text(encoding='utf-8-sig');accepted.append(item['mode']);continue
  r=read(folder/'report.json');assert run['exitCode']==run['diagnostics']==0 and r['pass'];accepted.append(item['mode'])
  if item['mode']=='backend':assert r['gpuFrames']==33 and r['gpuComponents']==540672 and r['diDispatchCases']==64 and r['independentHistories'] and r['isolatedResetObserved'] and len(set(r['gpuHashes']))>3 and len(r['diDispatchHashes'])==64
  if item['mode'] in ['gi','giCompat']:assert r['frames']==243 and len(r['checks'])==41
  if item['mode'] in ['combined','combinedCompat']:assert r['frames']==42 and len(r['checks'])==10 and r['diInstance']!=r['giInstance']
  if item['mode']=='player':
   assert r['renderCallbacks']==304 and len(r['checks'])==16 and r['api']=='Direct3D12' and r['colorSpace']=='Linear' and not run['abort'];payload=read(folder/'payload.json');native=[x for x in payload if x['path'].endswith('/HoRTDINRD.dll')];assert len(native)==1 and native[0]['sha256']==sha((ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll').read_bytes())
  if item['mode']=='small':assert r['shared']['restoredHook'] and r['shared']['released'] and r['shared']['independentPools'] and r['shared']['sharedInputBindings'] and r['shared']['recorded']==64
 else:
  assert run['exitCode']!=0 or run['diagnostics']>0
  if item['mode']=='composite':assert run['exitCode']==0 and run['diagnostics']==2 and read(folder/'report.json')['pass'] and 'ScratchAccelerationStructureData' in (folder/'unity.log').read_text(encoding='utf-8-sig')
assert set(accepted)>={'backend','small','gi','giCompat','combined','combinedCompat','playerBuild','player'}
last=read(HERE/'player-20261023T200124860/run.json')
for item in last['sources']:assert sha((ROOT/item['path']).read_bytes())==item['sha256'],item['path']
for p in (ROOT/'Runtime/RTGI').rglob('*.cs'):assert not re.search(r'\bHoRTDI\w*|using\s+[^;]*\.RTDI',code(p)),str(p)
for p in (ROOT/'Runtime/NRD').rglob('*.cs'):assert not re.search(r'\bHoRT[DG]I\w*|using\s+[^;]*\.RT[DG]I',code(p)),str(p)
for p in (ROOT/'Runtime/RTDI').rglob('*.cs'):assert not re.search(r'\bHoRTGI\w*|using\s+[^;]*\.RTGI',code(p)),str(p)
# Native binary/serialized asset GUIDs are moved, never rebuilt or replaced.
pairs=[('Runtime/RTDI/NRD/HoRTDINrdNative.cs.meta','Runtime/NRD/HoNrdNative.cs.meta'),('Runtime/RTDI/NRD/HoRTDINrdTiming.cs.meta','Runtime/NRD/HoNrdTiming.cs.meta'),('Runtime/RTDI/Plugins.meta','Runtime/NRD/Plugins.meta'),('Runtime/RTDI/Plugins/x86_64.meta','Runtime/NRD/Plugins/x86_64.meta'),('Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll.meta','Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll.meta'),('Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')]
for old,new in pairs:assert subprocess.check_output(['git','show',rev+':'+old],cwd=ROOT)==(ROOT/new).read_bytes(),new
# Baseline generation changes C# type/namespace names, not shader ABI strings.
for name in ['Context','Native','Timing','Manifest']:
 a=(HERE/'baseline'/('HoRTDINrd'+name+'.cs')).read_text(encoding='utf-8');b=(ROOT/'.codex-research/RTGIValidation/Assets/HoNrdArchitecture'/('HoLegacyNrd'+name+'.cs')).read_text(encoding='utf-8');assert re.findall(r'"(?:\\.|[^"\\])*"',a)==re.findall(r'"(?:\\.|[^"\\])*"',b)
r=read(HERE/'backend-20261023T194239312/report.json');sizes={'R8_UNorm':1,'R8G8B8A8_UNorm':4,'R16_SFloat':2,'R16G16B16A16_SFloat':8,'R32_SFloat':4,'R32G32B32A32_SFloat':16}
for item in r['pools']:assert item['bytes']==item['width']*item['height']*sizes[item['format']]
assert sum(x['bytes'] for x in r['pools'] if x['channel']=='DI')==r['diPoolBytes']==327696
assert sum(x['bytes'] for x in r['pools'] if x['channel']=='GI')==r['giPoolBytes']==307216
counts=collections.defaultdict(lambda:collections.Counter())
for row in r['pools']:
 if row['pool']=='transient':counts[(row['format'],row['factor'])][row['channel']]+=1
n64=n1080=0
for (fmt,factor),channels in counts.items():
 n=min(channels['DI'],channels['GI']);n64+=n*((64+factor-1)//factor)**2*sizes[fmt];n1080+=n*((1920+factor-1)//factor)*((1080+factor-1)//factor)*sizes[fmt]
assert n64==r['matchingTransientBytes64']==4112 and n1080==r['matchingTransientLayoutBytes1080']==2081760
assert read(HERE/'active-run.json')['activeProcess'] is None
print('PASS: neutral runtime dependency directions; original-vs-current GPU and DI native trace qualification; independent histories/resets/disposal; real RG/compatibility scenes; original DLL/GUIDs and failures preserved; memory unchanged, matching transient layout is only1.985MiB/1080 arithmetic')
