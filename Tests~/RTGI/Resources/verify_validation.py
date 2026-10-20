"""Verify live resource accounting and retain noisy GPU observations without qualifying a stable budget."""
import hashlib
import json
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
run=read(HERE/'run.json')
assert run['activeProcess'] is None and len(run['entries'])==3
assert sha(HERE/'../GpuTiming/native-build.json')==run['nativeBuildSha256']
build=read(HERE/'../GpuTiming/native-build.json')
assert sha(Path(build['dll']))==build['dllSha256']
for source in run['sources']:
    # Only the runner gained post-run timeout/fence-abort protection. Preserve
    # the exact executed script; do not pretend the new protection was executed.
    if source['path']=='Tests~/RTGI/Resources/run_validation.ps1':path=HERE/'source-at-run/run_validation.ps1'
    elif source['path'] in ['Editor/RTGI/HoRTGIResourceAudit.cs','Editor/RTGI/HoRTGICastleValidation.cs']:
        path=HERE/'source-at-run'/Path(source['path']).name
        assert path.read_text(encoding='utf-8')==(ROOT/source['path']).read_text(encoding='utf-8'),source['path']
    else:path=ROOT/source['path']
    assert sha(path)==source['sha256'],source['path']
evidence={'resourceAccountingQualified':True,'gpuTimestampsValidForCompletedRuns':True,'stableGpuBudgetQualified':False,'actual1080Qualified':False,'filteredQualityQualified':False,'halfGiImplemented':False,'runs':[],'rejectedRuns':[]}
formats={'R8_UNorm':1,'R8G8B8A8_UNorm':4,'R32_SFloat':4,'R16G16B16A16_SFloat':8}
for entry in run['entries']:
    if entry['exitCode']!=0:
        assert entry['label']=='1080-rg' and entry['exitCode']==-1 and not entry['fresh'] and not entry['marker']
        assert sha(Path(entry['log']))==entry['logSha256']
        assert 'D3D12Fence::Wait' in Path(entry['log']).read_text(encoding='utf-8-sig')
        evidence['rejectedRuns'].append({'label':entry['label'],'exitCode':entry['exitCode'],'reason':'Repeated Unity D3D12Fence::Wait errors; owned process terminated; no qualified HD result','logSha256':entry['logSha256']})
        continue
    assert entry['exitCode']==0 and entry['marker'] and entry['fresh'] and entry['pass'] and entry['shaderCsDiagnostics']==0
    assert sha(Path(entry['log']))==entry['logSha256']
    assert 'D3D12Fence::Wait' not in Path(entry['log']).read_text(encoding='utf-8-sig')
    path=HERE/'results'/(entry['label']+'.json');assert sha(path)==entry['reportSha256'];d=read(path)
    w,h=d['width'],d['height'];n=w*h;pitch=((w+15)//16)*256*((h+15)//16)
    assert (w,h)==((1920,1080) if '1080' in entry['label'] else ((256,256) if '256' in entry['label'] else (512,512)))
    assert d['pass'] and d['gpuQualified'] and d['instances']==15 and d['cutouts']==12 and d['skinnedSnapshots']==1
    assert d['nativeCutout'] and d['environment'] and d['stableBakes']==0 and d['valid']>0 and d['changedPixels']>0
    a=d['resourceAudit'];rows={r['binding']:r for r in a['resources']};assert len(rows)==28
    assert a['reservoirArrayPitch']==pitch and a['width']==w and a['height']==h
    expected={'GI.reservoirs':pitch*3*32,'GI.history':n*20,'GI.secondary':n*48,'GI.finalVisibility':n*8,'GI.diffuse':n*8,'GI.lights':32*64,'GI.neighbors':256*8,'GI.environmentRis':16384*8,'GI.environmentPdf':sum(4*(128>>m)**2 for m in range(8)),'Scene.instanceBuffer':16*16}
    for key,value in expected.items():assert rows[key]['payloadBytes']==value,(key,value,rows[key])
    assert a['aliases']==['GI.PublishedDiffuse']
    texture_ids=[]
    for r in rows.values():
        if r['kind']=='GraphicsBuffer':assert r['payloadBytes']==r['count']*r['stride'] and r['count']>0
        else:
            assert r['kind']=='RenderTexture' and r['instanceId']!=0 and r['unityRuntimeBytes']>0
            texture_ids.append(r['instanceId'])
            assert r['payloadBytes']==sum(max(1,r['width']>>m)*max(1,r['height']>>m)*formats[r['format']]*r['volumeDepth']*r['samples'] for m in range(r['mips']))
    assert len(texture_ids)==len(set(texture_ids))
    assert len([r for r in rows if r.startswith('NRD.Core.permanent[')])==6
    assert len([r for r in rows if r.startswith('NRD.Core.transient[')])==4
    assert len([r for r in rows if r.startswith('NRD.Core.constants[')])==3
    tile=((w+15)//16)*((h+15)//16)
    assert rows['NRD.Core.transient[2]']['payloadBytes']==tile
    for group,prefix in [('giPayloadBytes','GI.'),('nrdPayloadBytes','NRD.'),('sceneMappingPayloadBytes','Scene.')]:assert a[group]==sum(r['payloadBytes'] for key,r in rows.items() if key.startswith(prefix))
    assert a['nrdPayloadBytes']==79*n+tile+3*65536
    assert a['totalPayloadBytes']==sum(r['payloadBytes'] for r in rows.values())==259*n+96*(pitch-n)+tile+419412
    assert a['asApi']=='RayTracingAccelerationStructure.GetSize' and a['asApiBytes']>0 and not a['asError']
    assert len(a['poseMeshes'])==1 and a['poseMeshes'][0]['count']==251321
    assert a['poseUnityRuntimeBytes']==a['poseMeshes'][0]['unityRuntimeBytes']>0
    samples=d['gpuSamples'];assert len({s['slot'] for s in samples})==len(samples)
    for s in samples:
        assert s['frequency']>0 and s['targetFence']>0 and s['completedFence']>=s['targetFence'] and s['begin']>0 and s['end']>=s['begin']
        assert abs(s['milliseconds']-1000*(s['end']-s['begin'])/s['frequency'])<1e-9
    for summary in d['gpuSummary']:
        group=sorted(s['milliseconds'] for s in samples if s['name']==summary['name'])
        assert len(group)==40==summary['count'] and summary['median']==group[len(group)//2]
        assert summary['p95']==group[min(len(group)-1,int(len(group)*.95))]
    total=next(s for s in d['gpuSummary'] if s['name']=='GI.Total');assert total['median']>0
    evidence['runs'].append({'label':entry['label'],'width':w,'height':h,'ownedPayloadMiB':a['totalPayloadBytes']/1048576,'giMiB':a['giPayloadBytes']/1048576,'nrdMiB':a['nrdPayloadBytes']/1048576,'asApiMiB':a['asApiBytes']/1048576,'poseUnityRuntimeMiB':a['poseUnityRuntimeBytes']/1048576,'gpuTotalMedianMs':total['median'],'gpuTotalP95Ms':total['p95'],'reportSha256':sha(path)})
rejected=read(HERE/'rejected-compilation.json');assert len(rejected['entries'])==1 and rejected['entries'][0]['exitCode']!=0 and rejected['entries'][0]['shaderCsDiagnostics']>0
assert sha(Path(rejected['entries'][0]['log']))==rejected['entries'][0]['logSha256']
assert read(HERE/'host-observations.json')
assert not read(HERE/'../Scene/evidence.json')['qualityQualified']
w,h=1920,1080;n=w*h;pitch=((w+15)//16)*256*((h+15)//16);tile=((w+15)//16)*((h+15)//16)
evidence['unmeasured1080DescriptorProjection']={'width':w,'height':h,'payloadBytes':259*n+96*(pitch-n)+tile+419412,'basis':'Completed live 256/512 layout, assuming same 32-light capacity/16-instance mapping, environment, Cutout and NRD; excludes AS, pose mesh, GB/SB, assets, driver overhead and scratch. Not a successful HD allocation/render.'}
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified two real live resource/GPU runs and retained aborted HD run. Stable budget, HD rendering and filtered quality remain unqualified; half GI is not implemented.')
