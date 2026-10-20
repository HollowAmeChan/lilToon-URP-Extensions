"""Verify a rejected real half-grid implementation and its filtered reversion."""
import hashlib
import json
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
run=read(HERE/'run.json');rev=read(HERE/'reversion.json')
assert run['activeProcess'] is None and len(run['entries'])==2
assert not rev['retainedProductHalfMode'] and len(rev['restoredSources'])==7
assert sha(HERE/'rejected-runtime.patch')==rev['patchSha256']
restored={s['path']:s['sha256'] for s in rev['restoredSources']}
for name,digest in restored.items():assert sha(ROOT/name)==digest,name
assert not (ROOT/'Editor/RTGI/HoRTGIHalfValidation.cs').exists()
assert not (ROOT/'Editor/RTGI/HoRTGIHalfValidation.cs.meta').exists()
for s in run['sources']:
    archived=HERE/'source-at-run'/s['path'];assert sha(archived)==s['sha256'],s['path']
    if s['path'] not in restored and s['path'] not in ['Editor/RTGI/HoRTGIHalfValidation.cs','Tests~/RTGI/HalfGrid/run_validation.ps1']:assert sha(ROOT/s['path'])==s['sha256'],s['path']
evidence={'flatFunctionalQualified':True,'halfQualityQualified':False,'halfProductionRetained':False,'runs':[]}
for entry in run['entries']:
    assert entry['exitCode']==1 and entry['marker'] and entry['fresh'] and not entry['pass'] and entry['functionalPass'] and not entry['qualityPass'] and entry['shaderCsDiagnostics']==0 and entry['abortReason'] is None
    assert sha(Path(entry['log']))==entry['logSha256']
    assert 'D3D12Fence::Wait' not in Path(entry['log']).read_text(encoding='utf-8-sig')
    p=HERE/'results'/(entry['label']+'.json');assert sha(p)==entry['reportSha256'];d=read(p)
    assert not d['pass'] and d['functionalPass'] and not d['qualityPass'] and d['requests']==169
    assert d['compatibility']==(entry['label']=='compat')
    assert [r['name'] for r in d['rows']]==['full-flat','half-flat','odd-full-fallback','full-thin','half-thin']
    for r in d['rows']:
        assert not r['firstTemporal'] and r['firstNrdReset'] and r['stableTemporal']
        a=r['resources'];assert a['totalPayloadBytes']==sum(x['payloadBytes'] for x in a['resources'])
        for x in a['resources']:
            if x['kind']=='GraphicsBuffer':assert x['payloadBytes']==x['count']*x['stride']
        if r['name'].endswith('flat') or r['name']=='odd-full-fallback':
            assert r['rawError']<.05 and r['filteredError']<.05 and r['valid']==r['inputWidth']*r['inputHeight'] and r['changedPixels']>0
        assert r['scale']==(2 if r['name'].startswith('half-') else 1)
        if r['scale']==2:
            assert (r['inputWidth'],r['inputHeight'],r['giWidth'],r['giHeight'])==(64,64,32,32)
            textures={x['binding']:x for x in a['resources'] if x['kind']=='RenderTexture'}
            for name in ['GI.fullRaw','GI.fullFiltered']:assert textures[name]['width']==64 and textures[name]['height']==64
    full,half=d['rows'][3:]
    assert full['thinRawMin']>.95 and full['thinFilteredMin']>.95
    assert abs(full['thinRawMean']-1)<.05 and abs(full['thinFilteredMean']-1)<.05
    assert half['thinRawMin']==half['thinFilteredMin']==half['thinRawMean']==half['thinFilteredMean']==0
    assert full['resources']['totalPayloadBytes']==1227104 and half['resources']['totalPayloadBytes']==521556
    evidence['runs'].append({'label':entry['label'],'actualExitCode':entry['exitCode'],'requests':d['requests'],'fullPayloadBytes':full['resources']['totalPayloadBytes'],'halfPayloadBytes':half['resources']['totalPayloadBytes'],'payloadReductionPercent':100*(1-half['resources']['totalPayloadBytes']/full['resources']['totalPayloadBytes']),'fullThinMean':full['thinFilteredMean'],'halfThinMean':half['thinFilteredMean'],'reportSha256':sha(p)})
rejected=read(HERE/'rejected-single-frame/run.json');assert len(rejected['entries'])==1
entry=rejected['entries'][0];assert entry['exitCode']==1 and entry['fresh'] and not entry['functionalPass']
assert sha(Path(entry['log']))==entry['logSha256'] and sha(HERE/'rejected-single-frame/rg.json')==entry['reportSha256']
assert read(HERE/'rejected-single-frame/rg.json')['rows'][0]['rawError']>1
for s in rejected['sources']:assert sha(HERE/'rejected-single-frame/source'/s['path'])==s['sha256']
for s in rev['images']:assert sha(HERE/s['path'])==s['sha256']
compile=read(HERE/'restored-compile.json');assert compile['finished'] and compile['exitCode']==compile['csErrors']==compile['fenceErrors']==0
assert sha(Path(compile['log']))==compile['logSha256']
evidence['reversionCompileQualified']=True
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified both actual half-grid quality rejections, flat functional/resource evidence, exact prototype/source/images, and byte-for-byte product reversion with successful Unity compile. Half GI is not retained.')
