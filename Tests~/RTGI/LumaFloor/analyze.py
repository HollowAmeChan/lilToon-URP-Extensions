"""Separate exact stock per-iteration observations from bounded diagnostic UAV traces."""
import gzip
import hashlib
import json
import struct
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
IDS=[-2,-1,3,4,5,6,9,101,102,103,104,100]
RGB_DTYPE=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(12,27,3))])
WEIGHT_DTYPE=np.dtype([('observed','<i4'),('shader','<i4'),('application','<i4'),('v','<f4',(4,27,37,4))])
LUMA=np.array([.2126,.7152,.0722]);CENTER=float(np.float32(.44198)*np.float32(.44198))
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def load(label,root=HERE):
    d=read(root/f'results/{label}.json');r=d['localStageAudit'];arrays=[];weights=[]
    for s in r['segments']:
        b=raw(root/'results'/label/s['stream']);assert hashlib.sha256(b).hexdigest().upper()==s['streamSha256'] and b[:4]==b'HGP1'
        assert struct.unpack('<6i',b[4:28])==(256,256,27,12,s['measured'],s['seed']) and list(struct.unpack('<12i',b[28:76]))==IDS
        assert np.frombuffer(b,dtype='<i4',offset=76,count=54).reshape(27,2).tolist()==[[p['x'],p['y']] for p in s['points']]
        a=np.frombuffer(b,dtype=RGB_DTYPE,offset=292);assert len(a)==s['measured'] and np.isfinite(a['rgb']).all();arrays.append(a)
        b=raw(root/'results'/label/s['weightStream']);assert hashlib.sha256(b).hexdigest().upper()==s['weightStreamSha256'] and b[:4]==b'HGW1'
        assert struct.unpack('<4i',b[4:20])==(27,4,37,s['seed']);w=np.frombuffer(b,dtype=WEIGHT_DTYPE,offset=20);assert len(w)==s['weightFrames'] and np.isfinite(w['v']).all();weights.append(w)
    return d,r,arrays,weights

def summarize():
    _,r,new,_=load('matrix');_,br,old,_=load('control-matrix',HERE.parent/'AtrousPropagation');n=np.array([x['rgb'].astype(float) for x in new]);o=np.array([x['rgb'].astype(float) for x in old]);refs=r['referencePoints'];truth=np.array([vec(p['referenceMean']) for p in refs[:19]]);a=o[:,:,-1,:19].mean(1);b=n[:,:,-1,:19].mean(1);ma=a.mean(0);mb=b.mean(0);points=[];hard=[];perSeed=[];reg=[]
    for i,p in enumerate(refs[:19]):
        delta=np.abs(mb[i]-truth[i])-np.abs(ma[i]-truth[i]);paired=np.abs(b[:,i]-truth[i])-np.abs(a[:,i]-truth[i]);se=paired.std(0,ddof=1)/2;limit=np.maximum(np.maximum(.000005,truth[i]*.05),2*np.sqrt(se*se+vec(p['batchMeanStandardError'])**2));bad=np.flatnonzero(delta>limit)
        points.append({'x':p['x'],'y':p['y'],'baselineOverReference':(ma[i]/truth[i]).tolist(),'candidateOverReference':(mb[i]/truth[i]).tolist(),'errorIncrease':delta.tolist(),'hardLimit':limit.tolist(),'hardRegressionChannels':bad.tolist()})
        for c in bad:hard.append([p['x'],p['y'],int(c)])
    for i,(s,t) in enumerate(zip(r['segments'],br['segments'])):
        perSeed.append({'seed':s['seed'],'rawAllLocalSamplesBitwiseEqual':np.array_equal(n[i,:,0],o[i,:,0]),'preAtrousStagesBitwiseEqual':np.array_equal(n[i,:,:6],o[i,:,:6]),'baselineFullGatePass':t['spatial']['full']['energyGatePass'],'candidateFullGatePass':s['spatial']['full']['energyGatePass'],'baselinePhaseFailures':sum(not x['energyGatePass'] for x in t['spatial']['phases']),'candidatePhaseFailures':sum(not x['energyGatePass'] for x in s['spatial']['phases']),'filteredVarianceRatio':s['spatial']['full']['filteredVariance']/t['spatial']['full']['filteredVariance']})
        for p,q in zip(t['spatial']['phases'],s['spatial']['phases']):
            if p['energyGatePass'] and not q['energyGatePass']:reg.append([s['seed'],p['phaseX'],p['phaseY']])
    fa=np.mean([vec(s['spatial']['full']['raw']) for s in r['segments']],0);fb=np.mean([vec(s['spatial']['full']['filtered']) for s in r['segments']],0)
    l1a=float(np.abs(ma[:18]-truth[:18]).sum()/truth[:18].sum());l1b=float(np.abs(mb[:18]-truth[:18]).sum()/truth[:18].sum())
    return {'productionChanged':False,'eligible':False,'decision':'Shelve this exact diffuse denominator-floor1e-4/64 candidate after insufficient local benefit and original full/phase regressions; remove its Editor hooks, no epsilon/unit/iteration scan.',
        'baselineNew18L1':l1a,'candidateNew18L1':l1b,'relativeL1Reduction':1-l1b/l1a,'all19ReferencePointComparisons':points,'localHardRegressions':hard,'perSeed':perSeed,'phasePassToFailPairs':reg,'pooledFullRgbDifferencePercent':((fb/fa-1)*100).tolist(),'candidatePassingFullSeeds':sum(s['candidateFullGatePass'] for s in perSeed),'baselinePassingFullSeeds':sum(s['baselineFullGatePass'] for s in perSeed),'referenceLimit':'8 neighbors are input/geometry controls without MC truth. Qualified references remain same-model/selected and do not prove full-image quality.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:v for k,v in s.items() if k not in ['all19ReferencePointComparisons','perSeed','referenceLimit']},indent=2))
