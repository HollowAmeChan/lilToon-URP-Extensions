"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, stream, STAGES, CAPTURES, SEEDS, vec
import numpy as np
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



archives=read(HERE/'capture-archives.json')
for name,item in archives.items():
    packed=(HERE/item['path']).read_bytes();value=gzip.decompress(packed)
    assert sha(packed)==item['archiveSha256'] and len(packed)==item['archiveBytes'] and sha(value)==item['rawSha256'] and len(value)==item['rawBytes']
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag'
inputs=read(HERE/'inputs.json');reference=read(HERE.parent/'LocalReference/results/reference.json')['denseReference'];truth=reference['points'];assert reference['allPointsConverged'] and len(truth)==18
for label,seeds,warm,measured in [('qualification',[0,104729],16,16),('matrix',SEEDS,64,256)]:
    run=read(HERE/f'{label}-run.json');assert run['step']==28 and run['activeProcess'] is None and run['debugLayerRequested']
    e=run['entries'][0];assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['localFunctional'] and e['copyBitExact']
    assert e['segments']==len(seeds) and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];log=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,log,re.I)
    b=raw(HERE/f'results/{label}.json');assert sha(b)==e['reportSha256'];d=json.loads(b.decode('utf-8-sig'));r=d['localStageAudit']
    assert d['pass'] and not d['compatibility'] and d['frames']==32+len(seeds)*(warm+measured)+2
    assert r['complete'] and r['seeds']==seeds and r['stageIds']==STAGES and r['width']==r['height']==256 and r['warmup']==warm and r['measured']==measured
    assert r['referenceArchiveSha256']==inputs['reference']['archiveSha256'] and r['referenceRawSha256']==inputs['reference']['rawSha256']
    precision=r['precision'];assert precision['pass'] and precision['copyBitExact'] and precision['copiedComponents']==64 and precision['mode']=='production' and precision['signalScale']==64 and precision['changedPools']==4
    assert precision['signalFormat']=='R16G16B16A16_SFloat' and precision['outputFormat']=='R32G32B32A32_SFloat'
    assert len(r['segments'])==len(e['streams'])==len(seeds)
    for s,seed in zip(r['segments'],seeds):
        assert s['seed']==seed and s['warmup']==warm and s['measured']==measured and s['actualTemporal'] and s['maxPrimaryError']<=.0005
        assert s['lightCount']==18 and s['instances']==15 and s['cutouts']==12 and s['firstShaderFrame']==seed+warm and s['lastShaderFrame']==seed+warm+measured-1
        a=stream(label,s);assert np.array_equal(a['shader'],np.arange(seed+warm,seed+warm+measured))
        assert np.all(np.diff(a['application'])>=1) and a['application'][0]==s['firstApplicationFrame'] and a['application'][-1]==s['lastApplicationFrame']
        assert np.array_equal(a['writes'],np.tile([1,1,1,1,1,4],(measured,1)))
        recorded=next(x for x in e['streams'] if x['path']==s['stream']);assert recorded['sha256']==s['streamSha256'] and recorded['bytes']==208+1976*measured
        average=a['rgb'].astype(np.float64).mean(0);negative=(a['rgb']<0).sum((0,3))
        assert [(p['x'],p['y']) for p in s['points']]==[(p['x'],p['y']) for p in truth]
        for i,p in enumerate(s['points']):
            assert [v['pipeline'] for v in p['stages']]==STAGES
            for j,v in enumerate(p['stages']):
                assert np.allclose(average[j,i],vec(v['rgb']),rtol=2e-7,atol=1e-12) and v['negativeComponents']==negative[j,i]
            assert p['stages'][4]['meaning']=='Conditional HistoryFix / fast RGB'
        for c,id in zip(s['captures'],CAPTURES):
            assert c['pipeline']==id and c['format']=='R32G32B32A32_SFloat' and c['uav'] and c['width']==c['height']==256
            bindings=[v for v in s['bindings'] if v['pipeline']==id];assert len(bindings)==(4 if id==10 else 1)
            assert c['writes']==sum(a['writes'][:,CAPTURES.index(id)])==len(bindings)*measured
            assert all(v['descriptorType']==1 and v['binding']==('gOut_Diff_Variance' if id in [9,10] else 'gOut_Diff') and v['resourceType'] in ['PERMANENT_POOL','TRANSIENT_POOL','OUT_DIFF_RADIANCE_HITDIST'] for v in bindings)
        q=s['spatial'];assert q['complete'] and q['frames']==measured and q['full']['pixels']==65536 and q['full']['samples']==65536*measured and len(q['phases'])==16
        assert all(p['pixels']==4096 and p['samples']==4096*measured for p in q['phases'])
        assert len(q['frameMeans'])==measured
for source in read(HERE/'matrix-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
for item in [inputs['productionReport']]+inputs['productionMaps']:assert sha(raw(HERE/item['path']))==item['rawSha256']
packed=(HERE/inputs['reference']['path']).read_bytes();assert sha(packed)==inputs['reference']['archiveSha256'] and sha(gzip.decompress(packed))==inputs['reference']['rawSha256']
code=read(HERE/'code-references.json')
for item in code['files']:assert sha((HERE/item['path']).read_bytes())==item['sha256'] and (HERE/item['path']).stat().st_size==item['bytes']
assert (HERE/code['files'][0]['path']).read_bytes().replace(b'\r\n',b'\n')==(HERE/code['files'][3]['path']).read_bytes().replace(b'\r\n',b'\n')
assert 'consts->gStepSize = 1 << i;' in (HERE/code['files'][2]['path']).read_text(encoding='utf-8-sig')
expected=summarize();assert read(HERE/'analysis.json')==expected and not expected['productionChanged'] and not expected['qualityRepaired']
assert all(all(v for k,v in row.items() if k!='seed') for row in expected['observerChecks'])
assert not expected['full']['originalPooledGatePass'] and [p['phaseFailures'] for p in expected['perSeed']]==[7,11,11,7]
print('PASS: two actual processes, 64-component exact copy, real writer bindings/counts, all local stage samples, unchanged full/local observer data and original quality failures')
