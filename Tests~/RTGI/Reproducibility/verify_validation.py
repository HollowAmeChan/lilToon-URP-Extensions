"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, report, vec
import struct
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
    b=(HERE/item['path']).read_bytes();v=gzip.decompress(b);assert sha(b)==item['archiveSha256'] and len(b)==item['archiveBytes'] and sha(v)==item['rawSha256'] and len(v)==item['rawBytes']
bad=r'd3d12(?: validation)? (?:error|corruption)|D3D12Fence::Wait|Shader error|Shader warning|error CS'
for label,n,warm,measured in [('qualification',2,16,16),('stock-a',4,64,256),('stock-b',4,64,256),('noop',4,64,256)]:
    run=read(HERE/f'{label}-run.json');assert run['step']==35 and run['activeProcess'] is None and run['debugLayerRequested'];e=run['entries'][0]
    assert e['exitCode']==0 and e['abortReason'] is None and e['marker'] and e['fresh'] and e['pass'] and e['numericSnapshotQualified'] and e['localFunctional'] and e['copyBitExact']
    assert e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    d=read(HERE/f'results/{label}.json');assert sha(raw(HERE/f'results/{label}.json'))==e['reportSha256'];r=d['localStageAudit'];assert d['frames']==32+n*(warm+measured)+2 and r['complete'] and len(r['referencePoints'])==27
    assert r['trace']['restoredOriginal'] and r['trace']['noopClone']==(label=='noop') and r['trace']['replacementActive']==(label=='noop')
    for seg in r['segments']:
        assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005 and seg['measured']==measured and seg['warmup']==warm
        for name in ['firstInputs','lastInputs']:
            x=seg[name];assert x['shaderFrame']==(seg['firstShaderFrame'] if name=='firstInputs' else seg['lastShaderFrame']) and x['temporal'] and x['environment'] and x['alphaVisibility']
            assert x['lightCount']==18 and x['instances']==15 and x['cutouts']==12 and len(x['lights'])==72 and len(x['selectedPrimary'])==135 and len(x['ris'])==16384 and len(x['selectedSecondary'])==27 and len(x['selectedPrimaryProbe'])==162
            b=b''.join(struct.pack('<4f',v['x'],v['y'],v['z'],v['w']) for v in x['lights']);assert sha(b)==x['lightsSha256']
            b=b''.join(struct.pack('<ii',v['x'],v['y']) for v in x['ris']);assert sha(b)==x['risSha256']
        b=raw(HERE/'results'/label/seg['publicationMap']);assert sha(b)==seg['publicationMapSha256'] and len(b)==16+65536*24
    gen=HERE/'generated'/label;body=(gen/'AtrousTrace.hlsl').read_bytes();wrapper=(gen/'AtrousTrace.compute').read_bytes();assert sha(body)==r['trace']['bodySha256'] and sha(wrapper)==r['trace']['wrapperSha256']
    if label=='noop':
        text=body.decode('utf-8-sig').replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/NRD/','').replace('\r\n','\n');original=(ROOT/'Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl').read_text(encoding='utf-8-sig').replace('\r\n','\n');assert text==original
# Keep actual initial success with omitted numeric fields, device failure, and explicit abort.
for label in ['qualification','stock-a']:
    root=HERE/'initial-numeric-omission'/label;run=read(root/'run.json');e=run['entries'][0];assert e['exitCode']==0 and e['pass'] and e['debugErrors']==e['shaderCsDiagnostics']==0
    assert sha(raw(root/'report.json'))==e['reportSha256'];d=read(root/'report.json');assert 'lights' not in d['localStageAudit']['segments'][0]['firstInputs'] and 'selectedPrimary' not in d['localStageAudit']['segments'][0]['firstInputs']
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256']
crash=read(HERE/'device-failure/qualification/run.json');e=crash['entries'][0];assert e['exitCode']==1073741845 and not e['fresh'] and e['debugErrors']>0
log=(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig');assert 'DXGI_ERROR_DEVICE_HUNG' in log and 'Unmap' in log
aborted=read(HERE/'device-failure/stock-a/run.json');assert aborted['entries'][0]['exitCode']==-1 and not aborted['entries'][0]['fresh']
assert read(HERE/'abort-review.json')['qualificationActualExit']==1073741845
for name in ['recovery','serialization']:
    r=read(HERE/f'{name}-run.json');assert not r['active'] and r['exit']==0 and r['abort'] is None and r['diagnostics']==0 and r['fresh']
    assert sha((HERE/f'{name}.log').read_bytes())==r['logSha256'] and sha((HERE/f'{name}-report.json').read_bytes())==r['reportSha256']
    assert read(HERE/f'{name}-report.json')['pass']
gpu=read(HERE/'recovery-report.json');assert len(gpu['samples'])==24 and gpu['longMedian']>gpu['shortMedian']*2 and gpu['longMedian']>gpu['emptyMedian']*3
for x in gpu['samples']:assert x['targetFence']<=x['completedFence'] and x['frequency']>0 and (x['iterations']==0 or x['end']>x['begin'])
serialization=read(HERE/'serialization-report.json');assert serialization['lightVectors']==72 and serialization['primaryWords']==135 and serialization['risRecords']==16384
for item in read(HERE/'support-inputs.json'):assert sha((ROOT/item['path']).read_bytes())==item['sha256']
for source in read(HERE/'noop-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
s=summarize();assert read(HERE/'analysis.json')==s and s['currentStrictReproducibilityPassed'] and not s['productionChanged']
assert all(e['withinOriginal019EndpointPrecision'] and e['lightsSkyRisPrimaryHashesEqual'] and not e['secondaryWholeHashEqual'] for r in s['historicalInitialStockComparison'] for e in r['endpoints'])
print('PASS: current stockA/B/no-op full-map and numeric-state equality, serialization fix, historical omissions/device crash/abort retained, small recovery qualified; historical mismatch cause remains unproven')
