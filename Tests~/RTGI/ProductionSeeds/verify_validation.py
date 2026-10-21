"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import load, raw, summarize, SEEDS
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

archives=read(HERE/'capture-archives.json')
for name,item in archives.items():
    packed=(HERE/item['path']).read_bytes();value=gzip.decompress(packed)
    assert sha(packed)==item['archiveSha256'] and len(packed)==item['archiveBytes']
    assert sha(value)==item['rawSha256'] and len(value)==item['rawBytes']
def capture(name):return gzip.decompress((HERE/archives[name]['path']).read_bytes())
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag'
for runfile,reportfile,n,warm,measured in [('pre-utf8-qualification/run.json','pre-utf8-qualification/report.json',2,16,16),('matrix-run.json','results/matrix.json',8,64,256),('qualification-run.json','results/qualification.json',2,16,16)]:
    r=read(HERE/runfile);assert r['activeProcess'] is None and r['step']==26 and r['debugLayerRequested']
    e=r['entries'][0];assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['seedFunctional']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    assert logs[e['log']]['sha256']==e['logSha256'];log=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert not re.search(bad,log,re.I)
    for source in r['sources']:assert source['sha256'] in index
    value=capture(reportfile);assert sha(value)==e['reportSha256'];d=json.loads(value.decode('utf-8-sig'));a=d['seedAudit']
    assert d['pass'] and d['frames']==32+n*(warm+measured)+2 and d['width']==d['height']==256
    assert a['functionalPass'] and a['terminal'] and a['testedModes']==['temporal-spatial'] and len(a['segments'])==n
    assert a['seedCount']==n and a['warmup']==warm and a['measured']==measured and sum(p['converged'] for p in a['referencePoints'])==15
    for inp in a['referenceInputs']:assert sha((ROOT/inp['path']).read_bytes())==inp['sha256']
    for i,seg in enumerate(a['segments']):
        assert seg['seed']==SEEDS[i] and seg['mode']=='temporal-spatial' and seg['temporal'] and seg['spatial'] and seg['actualTemporal']
        assert seg['environment'] and seg['alphaVisibility'] and seg['lightCount']==18 and seg['instances']==15 and seg['cutouts']==12
        assert seg['maxPrimaryError']<.0005 and seg['firstShaderFrame']==SEEDS[i]+warm and seg['lastShaderFrame']==SEEDS[i]+warm+measured-1
        assert seg['lastApplicationFrame']-seg['firstApplicationFrame']>=measured-1 and len(seg['frameMeans'])==measured
        assert len(seg['phases'])==16 and seg['full']['pixels']==65536 and seg['full']['samples']==65536*measured
        assert all(p['pixels']==4096 and p['samples']==4096*measured for p in seg['phases'])
        folder='pre-utf8-qualification/maps' if runfile.startswith('pre-') else 'results/'+e['label']
        value=capture(folder+'/'+seg['publicationMap']);assert sha(value)==seg['publicationMapSha256'] and len(value)==16+65536*24
    if not runfile.startswith('pre-'):load(e['label'])
repair=read(HERE/'encoding-repair.json');assert repair['unicodeTextUnchanged'] and repair['qualificationAfterUtf8ActualExit']==0
before=index[repair['beforeSha256']];after=index[repair['afterSha256']]
assert (HERE/before['path']).read_bytes().decode('gbk')==(HERE/after['path']).read_bytes().decode('utf-8')
for item in repair['mapComparison']:
    assert item['publicationMapBitwiseEqual'] and item['beforeSha256']==item['afterSha256']
    name='publication-'+str(item['seed'])+'.bin';assert capture('pre-utf8-qualification/maps/'+name)==capture('results/qualification/'+name)
latest=read(HERE/'qualification-run.json')
reporting=read(HERE/'reporting-review.json')
for source in latest['sources']:
    current=(ROOT/source['path']).read_bytes()
    if source['path']==reporting['path']:
        assert source['sha256']==reporting['beforeSha256'] and sha(current)==reporting['afterSha256']
        original=(HERE/index[source['sha256']]['path']).read_bytes()
        assert original.count(reporting['exactBefore'].encode())==1
        assert original.replace(reporting['exactBefore'].encode(),reporting['exactAfter'].encode())==current
    else:assert sha(current)==source['sha256']
expected=summarize();assert read(HERE/'analysis.json')==expected
assert not expected['full']['pooledOriginalGatePass'] and expected['full']['passingSeedSegments']==3
assert len(expected['all16Phases'])==16 and len(expected['all64FixedTiles'])==64 and len(expected['discoveryTop12Holdout'])==12
assert sum(not p['pooledOriginalGatePass'] for p in expected['all16Phases'])==9
assert sum(not p['pooledOriginalGatePass'] for p in expected['all64FixedTiles'])==32
# There is no quality-fix claim: data distinguish marginal aggregate acceptance
# from persistent local structures, and retain all original per-seed gates.
print('PASS: 3 actual processes, exact sources/maps, encoding qualification, all phases/tiles and held-out local accounting; original pooled quality gate remains FAILED')
