"""Check real automatic events, output transitions, exact sources and failures."""
import hashlib
import json
import math
import re
import subprocess
import gzip
import sys
sys.dont_write_bytecode=True
from analyze import raw, summarize, read
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
for label in ['small','qualification','matrix','filtered']:
    run=read(HERE/f'{label}-run.json');e=run['entries'][0];assert run['activeProcess'] is None and run['step']==37 and run['debugLayerRequested']
    assert e['exitCode']==0 and e['abortReason'] is None and e['pass'] and e['marker'] and e['fresh'] and e['sharedReleased'] and e['restoredHook'] and e['debugErrors']==e['shaderCsDiagnostics']==e['sourceDrift']==0
    for source in run['sources']:assert source['sha256'] in index
    assert logs[e['log']]['sha256']==e['logSha256'];assert not re.search(bad,(HERE/logs[e['log']]['path']).read_text(encoding='utf-8-sig'),re.I)
    value=raw(HERE/f'results/{label}.json');assert sha(value)==e['reportSha256'];d=json.loads(value.decode('utf-8-sig'));r=d['shared'] if label=='small' else d['localStageAudit']['shared']
    assert d['pass'] and r['independentPools'] and r['sharedInputBindings'] and r['restoredHook'] and r['released']
    n=64 if label=='small' else 1024 if label=='matrix' else 32
    assert len(r['frames'])==e['comparedFrames']==n and r['recorded']==(1280 if label=='matrix' else 64)
    components=(64 if label=='small' else 256)**2*4
    for f in r['frames']:
        assert f['comparedComponents']==components and f['mismatches0']==0 and f['maxError0']==0 and f['baselineSha256']==f['branch0Sha256']
        assert len(f['commonSha256'])==len(f['settingsSha256'])==64
        if label=='filtered':assert f['mismatches1']==0 and f['maxError1']==0 and f['baselineSha256']==f['branch1Sha256']
        else:assert f['mismatches1']>0
    if label!='filtered':
        policy=r['policy'];assert policy['restored'] and policy['released'] and policy['calibrationMismatches']==0 and policy['maxCalibrationError']==0
        assert policy['calibratedFrames']==(0 if label=='small' else n)
        assert policy['firstWrites']==r['recorded'] and policy['laterWrites']==4*r['recorded']
        sources={x['path']:x['sha256'] for x in run['sources']}
        for item in policy['sources']:
            for kind in ['body','wrapper']:
                relative=item[kind+'Path'].replace('Packages/jp.lilxyzw.liltoon.urp.extensions/','');assert sources[relative]==item[kind+'Sha256']
    else:assert r['noop']['noopClone'] and r['noop']['restoredOriginal']
    if label!='small':
        local=d['localStageAudit'];assert local['complete'] and local['precision']['copyBitExact'] and local['precision']['copiedComponents']==64
        assert d['frames']==(1314 if label=='matrix' else 98)
        for seg in local['segments']:
            assert seg['actualTemporal'] and seg['maxPrimaryError']<=.0005
            for field in ['stream','weightStream','publicationMap']:
                v=raw(HERE/'results'/label/seg[field]);assert sha(v)==seg[field+'Sha256']
            assert all(c['writes']==seg['measured']*(4 if c['pipeline']==10 else 1) for c in seg['captures'])
        if label!='filtered':
            for seg in r['policy']['segments']:
                for field in ['map','stream']:assert sha(raw(HERE/'results'/label/seg[field]))==seg[field+'Sha256']
                b=raw(HERE/'results'/label/seg['stream']);assert b[:4]==b'HGC1'
                width,height,seed,count=__import__('struct').unpack('<4i',b[4:20]);assert (width,height,seed,count)==(256,256,seg['seed'],27)
                coords=np.frombuffer(b,dtype='<i4',offset=20,count=count*2).reshape(count,2)
                dtype=np.dtype([('shader','<i4'),('application','<i4'),('rgb','<f4',(27,3))]);values=np.frombuffer(b,dtype=dtype,offset=20+count*8)
                assert len(values)==seg['frames'] and np.isfinite(values['rgb']).all() and (values['rgb']>=0).all()
                warm=16 if label=='qualification' else 64;assert np.array_equal(values['shader'],np.arange(seed+warm,seed+warm+seg['frames']))
                means=values['rgb'].astype(np.float64).mean(0).astype(np.float32);m=np.frombuffer(raw(HERE/'results'/label/seg['map']),dtype='<f4',offset=16).reshape(256,256,6)
                assert np.array_equal(means,np.array([m[y,x,3:] for x,y in coords]))
# Only filtered-run sources are current. Never rewrite history to the new source.
for source in read(HERE/'filtered-run.json')['sources']:assert sha((ROOT/source['path']).read_bytes())==source['sha256']
for relative,item in read(HERE/'inputs.json').items():assert sha((ROOT/relative).read_bytes())==item['sha256']
assert not (ROOT/'Editor/RTGI/HoRTGIDiffusePolicyAudit.cs').exists()
assert not list((ROOT/'Editor/RTGI/Shaders').glob('HoRTGIDiffusePolicy*'))
assert not (ROOT/'Editor/RTGI/Shaders/HoRTGISharedResolve.compute').exists()
assert 'diffusePolicy' not in (ROOT/'Editor/RTGI/HoRTGICastleValidation.cs').read_text(encoding='utf-8-sig')
# Rebuild in ignored evidence subfolder, never reinstall rejected product shaders.
subprocess.run([sys.executable,str(HERE/'build_policy.py')],cwd=ROOT,check=True)
for row in read(HERE/'policy-source.json'):
    name='HoRTGIDiffusePolicy'+row['stage']
    for ext,kind in [('.hlsl','body'),('.compute','wrapper')]:assert sha((HERE/'reconstructed'/(name+ext)).read_bytes())==row[kind+'Sha256'].upper()
s=summarize();assert read(HERE/'analysis.json')==s and s['comparisonQualified'] and not s['productionChanged']
assert not s['eligibleForFurtherEightSeedDynamicHdrPlayerGpuQualification'] and len(s['hardRegressions'])==3
assert {(p['x'],p['y'],p['channel']) for p in s['hardRegressions']}=={(196,141,2),(141,157,1),(141,157,2)}
assert not s['phasePassToFailPairs'] and s['full']['candidatePassingSeeds']==4 and s['groups']['new']['relativeL1Reduction']>.42
assert s['groups']['legacy']['qualifiedPoints']==15 and s['groups']['new']['qualifiedPoints']==18
print('PASS: exact shared-input comparison, 1024 physical publication calibrations, five actual passes, all34 reference/noise/domain gates; three hard regressions retained and rejected policy removed; filtered stock32 frames bitexact')
