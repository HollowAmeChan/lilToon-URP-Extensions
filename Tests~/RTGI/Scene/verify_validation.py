"""Verify actual scene integration and explicitly retain rejected quality gates."""
from pathlib import Path
import hashlib
import json

HERE=Path(__file__).parent
ROOT=HERE.parents[2]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
deps=read(HERE/'dependencies.json');run=read(HERE/'run.json')
assert run['activeProcess'] is None and len(run['entries'])==4
assert sha(ROOT/'Editor/RTGI/HoRTGICastleValidation.cs')==run['builderSha256']
assert sha(HERE/'dependencies.json')==run['dependenciesSha256']
for source in deps['copiedFiles']:
    for root in ('source','target'):assert sha(Path(deps[root])/source['path'])==source['sha256'],(root,source['path'])
for file,digest in deps['gltfPackageFiles'].items():assert sha(Path(deps['target'])/'Packages/org.khronos.unitygltf'/file)==digest
summary={'functionalSceneRuns':[],'qualityQualified':False,'rejectedQualityRuns':[],'dependencyFiles':len(deps['copiedFiles'])}
for entry in run['entries']:
    assert entry['exitCode']==0 and entry['marker'] and entry['fresh'] and entry['pass'] and entry['shaderCsDiagnostics']==0
    assert sha(Path(entry['log']))==entry['logSha256'];report=HERE/'results'/(entry['label']+'.json');assert sha(report)==entry['reportSha256'];d=read(report)
    assert d['pass'] and d['instances']==15 and d['cutouts']==12 and d['skinnedSnapshots']==1 and d['stableBakes']==0
    assert d['valid']==d['width']*d['height'] and d['lit']>0 and d['changedPixels']>0
    summary['functionalSceneRuns'].append({'label':entry['label'],'valid':d['valid'],'lit':d['lit'],'changedPixels':d['changedPixels'],'rawMaxChannelMean':d['rawMean'],'filteredMaxChannelMean':d['filteredMean']})
for entry in read(HERE/'rejected-runs.json'):
    assert entry['exitCode']==1 and sha(Path(entry['log']))==entry['logSha256']
    report=HERE/'results'/entry['report'];assert sha(report)==entry['reportSha256'];d=read(report)
    assert not d['pass'] and not d['energyGatePass']
    summary['rejectedQualityRuns'].append({'label':entry['label'],'rgbRelativeError':entry['rgbRelativeError'],'rawVariance':d['rawVariance'],'filteredVariance':d['filteredVariance']})
assert 'fastSigma=2' in (ROOT/'Runtime/RTGI/NRD/HoRTGINrdContext.cs').read_text(encoding='utf-8')
(HERE/'evidence.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print('Verified 4 actual scene integration runs, source unchanged, and 5 rejected quality runs; qualityQualified remains FALSE')
