"""Verify immutable inputs, actual factor reconstruction and bounded replay claims."""
import gzip
import hashlib
import json
import sys
from pathlib import Path
import numpy as np

sys.dont_write_bytecode=True
from analyze import HERE, SOURCE, load, read, raw, summarize, LUMA, CENTER

def sha(b):return hashlib.sha256(b).hexdigest().upper()
inputs=read(HERE/'inputs.json')
for item in inputs['data']:
    path=HERE/item['path'];packed=path.with_suffix(path.suffix+'.gz').read_bytes();assert sha(packed)==item['archiveSha256'] and sha(gzip.decompress(packed))==item['rawSha256']
for item in inputs['code']:
    b=(HERE/item['path']).read_bytes();assert sha(b)==item['sha256'] and len(b)==item['bytes']
report,v=load();assert v.shape==(4,9,4,27,37,4) and np.isfinite(v).all()
center=v[:,:,:,:,1];donors=v[:,:,:,:,6::4];taps=v[:,:,:,:,5::4];f=v[:,:,:,:,7::4];pre=f[...,0]*f[...,1]*f[...,2];delta=np.abs(donors[...,:3]@LUMA-(center[...,:3]@LUMA)[...,None])
forward=np.exp(-delta/np.maximum(.0001,2*np.sqrt(np.maximum(center[...,3],0)))[...,None]);valid=pre>.0001
assert np.max(np.abs(forward[valid]-f[...,3][valid]))<4e-7
assert np.allclose(pre*f[...,3],taps[...,2],rtol=2e-6,atol=1e-9)
sumW=v[:,:,:,:,3,2];prediction=(center[...,:3]*CENTER+(donors[...,:3]*taps[...,2,None]).sum(-2))/sumW[...,None]
assert np.max(np.abs(prediction-v[:,:,:,:,4,:3]))<1e-9
s=summarize();assert read(HERE/'analysis.json')==s and not s['productionChanged'] and not s['newGpuRun']
assert s['geometry']['fitRank']==4 and s['geometry']['maxFitWorldError']<.00001 and s['geometry']['safeComparedPairs']==31104 and s['geometry']['safeMismatches']==0
assert len(s['models'])==2 and s['models'][0]['model']=='symmetricLumaGeometricMean' and s['models'][1]['model']=='symmetricExistingFactor2Support'
for model in s['models']:
    assert [g['group'] for g in model['groups']]==['new18','counterexample']
    for group in model['groups']:
        assert [r['step'] for r in group['rounds']]==[2,4,8,16]
        assert all(len(r['perSeed'])==4 and np.isfinite(r['deltaL1']) for r in group['rounds'])
pair=s['models'][0];assert abs(pair['groups'][0]['rounds'][-1]['deltaL1'])<.0001
contrast=s['models'][1]
assert all(r['deltaL1']<-.015 for r in contrast['groups'][0]['rounds'])
assert contrast['groups'][1]['rounds'][0]['deltaL1']>0
assert all(abs(r['deltaL1'])<.000001 for r in contrast['groups'][1]['rounds'][2:])
print('PASS: immutable031 inputs, forward weights/output reconstructed, 31104 geometry decisions agree; frozen replay models remain offline hypotheses, not GPU/quality acceptance')
