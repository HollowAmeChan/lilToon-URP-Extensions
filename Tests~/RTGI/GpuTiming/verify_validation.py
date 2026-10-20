"""Verify fenced GPU timestamp evidence and native-probe isolation."""
import hashlib
import json
from pathlib import Path

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
run=read(HERE/'run.json');build=read(HERE/'native-build.json')
assert run['activeProcess'] is None and len(run['entries'])==7
assert sha(HERE/'native-build.json')==run['nativeBuildSha256']
assert sha(HERE/'Native/HoGIGpuProbe.cpp')==build['sourceSha256'] and sha(Path(build['dll']))==build['dllSha256']
for source in run['sources']:assert sha(ROOT/source['path'])==source['sha256'],source['path']
evidence={'gpuCostQualified':True,'filteredQualityQualified':False,'nativeSha256':build['dllSha256'],'runs':[]}
for entry in run['entries']:
    assert entry['exitCode']==0 and entry['marker'] and entry['fresh'] and entry['pass'] and entry['shaderCsDiagnostics']==0
    assert sha(Path(entry['log']))==entry['logSha256'];report=HERE/'results'/(entry['label']+'.json');assert sha(report)==entry['reportSha256'];d=read(report)
    if entry['label']=='qualification':
        assert len(d['samples'])==24 and d['longMedian']>d['shortMedian']*2 and d['longMedian']>d['emptyMedian']*3
        samples=d['samples']
    else:
        assert d['gpuQualified'];samples=d['gpuSamples']
        assert len({x['slot'] for x in samples})==len(samples)
        for group in d['gpuSummary']:
            assert group['count']>=(1 if group['name']=='GI.BuildEnvironmentPdf' else 32)
        assert any(x['name']=='GI.Total' and x['median']>0 for x in d['gpuSummary'])
        assert any(x['name']=='GI.Composite' and x['median']>0 for x in d['gpuSummary'])
        if entry['label']=='256-rebuild':assert next(x for x in samples if x['name']=='GI.AS' and x['frame']==0)['milliseconds']>0
    for sample in samples:
        assert sample['frequency']>0 and sample['targetFence']>0 and sample['completedFence']>=sample['targetFence']
        assert sample['begin']>0 and sample['end']>=sample['begin']
        assert abs(sample['milliseconds']-1000*(sample['end']-sample['begin'])/sample['frequency'])<1e-9
        if entry['label']=='qualification' and sample['iterations']>0:assert sample['end']>sample['begin']
    evidence['runs'].append({'label':entry['label'],'samples':len(samples),'summary':d.get('gpuSummary'),'reportSha256':sha(report)})
reg=read(HERE/'release-regression.json');assert reg['exitCode']==0 and reg['probeDllAbsent']
assert sha(Path(reg['log']))==reg['logSha256'] and sha(Path(reg['report']))==reg['reportSha256']
d=read(Path(reg['report']));assert d['pass'] and d['skinnedProbe'] and d['renderCallbacks']==315
release=Path(reg['exe']).parent
assert not list(release.rglob('HoGIGpuProbe.dll'))
evidence['releaseRegression']={'pass':d['pass'],'requests':d['renderCallbacks'],'probeDllAbsent':True,'reportSha256':reg['reportSha256']}
evidence['pluginImporterMetaSha256']=sha(Path(build['dll']+'.meta'))
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified seven real GPU runs, fenced/ordered timestamps, known-load qualification, and 315-request Release without probe DLL; filtered quality still unqualified')
