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
def load(label):
    d=read(HERE/f'results/{label}.json');r=d['localStageAudit'];arrays=[];weights=[]
    for s in r['segments']:
        b=raw(HERE/'results'/label/s['stream']);assert hashlib.sha256(b).hexdigest().upper()==s['streamSha256'] and b[:4]==b'HGP1'
        assert struct.unpack('<6i',b[4:28])==(256,256,27,12,s['measured'],s['seed']) and list(struct.unpack('<12i',b[28:76]))==IDS
        assert np.frombuffer(b,dtype='<i4',offset=76,count=54).reshape(27,2).tolist()==[[p['x'],p['y']] for p in s['points']]
        a=np.frombuffer(b,dtype=RGB_DTYPE,offset=292);assert len(a)==s['measured'] and np.isfinite(a['rgb']).all();arrays.append(a)
        b=raw(HERE/'results'/label/s['weightStream']);assert hashlib.sha256(b).hexdigest().upper()==s['weightStreamSha256'] and b[:4]==b'HGW1'
        assert struct.unpack('<4i',b[4:20])==(27,4,37,s['seed']);w=np.frombuffer(b,dtype=WEIGHT_DTYPE,offset=20);assert len(w)==s['weightFrames'] and np.isfinite(w['v']).all();weights.append(w)
    return d,r,arrays,weights
def summarize():
    _,stock,sa,_=load('control-matrix');_,debug,da,wa=load('matrix');assert stock['seeds']==debug['seeds']==[0,104729,5347609,6396185]
    old=read(HERE.parent/'ProductionSeeds/results/matrix.json')['seedAudit'];observers=[];sm=[];dm=[];reconstruction=[];weightValues=[]
    for ss,ds,s,d,w in zip(stock['segments'],debug['segments'],sa,da,wa):
        previous=next(x for x in old['segments'] if x['seed']==ss['seed']);b=np.frombuffer(raw(HERE.parent/'ProductionSeeds/results/matrix'/previous['publicationMap']),dtype='<f4',offset=16)
        a=np.frombuffer(raw(HERE/'results/control-matrix'/ss['publicationMap']),dtype='<f4',offset=16)
        dbg=np.frombuffer(raw(HERE/'results/matrix'/ds['publicationMap']),dtype='<f4',offset=16)
        observers.append({'seed':ss['seed'],'stockEntireMeanMapBitwiseEqual':np.array_equal(a,b),
            'debugEntireMeanMapBitwiseEqual':np.array_equal(dbg,b),'debugMeanMapMaxAbsoluteDifference':float(np.abs(dbg-b).max()),
            'rawLocalAllSamplesBitwiseEqual':np.array_equal(s['rgb'][:,0],d['rgb'][:,0]),
            'debugVsStockPerStageMaxAbsoluteDifference':np.max(np.abs(s['rgb'].astype(float)-d['rgb'].astype(float)),axis=(0,2,3)).tolist()})
        sm.append(s['rgb'].astype(float).mean(0));dm.append(d['rgb'].astype(float).mean(0));v=w['v'].astype(float);weightValues.append(v)
        tw=v[:,:,:,5::4,2];donors=v[:,:,:,6::4,:3];sumW=v[:,:,:,3,2]
        prediction=(v[:,:,:,1,:3]*CENTER+(donors*tw[...,None]).sum(3))/sumW[...,None]
        reconstruction.append({'seed':ss['seed'],'sampledObservedFrames':w['observed'].tolist(),
            'maxWeightSumError':float(np.abs(CENTER+tw.sum(3)-sumW).max()),
            'maxReconstructedOutputError':float(np.abs(prediction-v[:,:,:,4,:3]).max())})
    sm=np.array(sm).mean(0);dm=np.array(dm).mean(0);refs=stock['referencePoints'];rows=[]
    for group,indices in [('new18',list(range(18))),('counterexample',[18])]:
        truth=np.array([vec(refs[i]['referenceMean']) for i in indices]);rows.append({'group':group,'indices':indices,
            'stageNormalizedL1':[float(np.abs(v[indices]-truth).sum()/truth.sum()) for v in sm],
            'stageRgbOverReference':[(v[indices].sum(0)/truth.sum(0)).tolist() for v in sm]})
    v=np.concatenate(weightValues,axis=0);tw=v[:,:,:,5::4,2];donors=v[:,:,:,6::4,:3];center=v[:,:,:,1,:3];sumW=v[:,:,:,3,2]
    ratio=(donors@LUMA)/np.maximum((center@LUMA)[...,None],1e-30);factor=np.array([vec(p['albedo']) for p in refs])*64
    contribution=tw[...,None]*(donors-center[:,:,:,None,:])/sumW[:,:,:,None,None]*factor[None,None,:,None,:]
    groups=[]
    for name,indices in [('newBrightCenters',[i for i in range(18) if refs[i]['x']==86]),('counterexample',[18])]:
        rounds=[]
        for j,step in enumerate([2,4,8,16]):
            w=tw[:,j,indices];denom=sumW[:,j,indices];rat=ratio[:,j,indices];c=contribution[:,j,indices]
            z=np.take(v[:,j],indices,axis=1);sigma=2*np.sqrt(np.maximum(z[:,:,1,3],0));centerLum=z[:,:,1,:3]@LUMA;donorLum=z[:,:,6::4,:3]@LUMA;lumWeight=z[:,:,7::4,3];difference=np.abs(donorLum-centerLum[...,None]);mask=(lumWeight>1e-4)&(lumWeight<.9999)&(difference>1e-9)
            inferred=-np.log(lumWeight[mask])/difference[mask]
            rounds.append({'step':step,'phiTimesSigmaMedian':float(np.median(sigma)),'fractionAtPinned1e4Floor':float(np.mean(sigma<1e-4)),
                'inferredLuminanceInverseMedian':float(np.median(inferred)),'inferredInverseNear10000Fraction':float(np.mean(np.abs(inferred-10000)<10)),
                'centerWeightFraction':float((CENTER/denom).mean()),
                'donorBelowHalfCenterWeightFraction':float(((w*(rat<.5)).sum(-1)/denom).mean()),
                'donorAboveTwiceCenterWeightFraction':float(((w*(rat>2)).sum(-1)/denom).mean()),
                'meanPhysicalRgbChangeFromDonors':c.sum(-2).mean((0,1)).tolist(),
                'meanPhysicalLuminanceChangeFromDonors':float((c.sum(-2)@LUMA).mean())})
        groups.append({'group':name,'rounds':rounds})
    return {'scope':'Default5 unchanged. Stock shader per-round outputs are the exact observation record; separate debug-UAV weights explain propagation with measured numerical differences, not quality acceptance. Eight neighbors lack MC truth and are geometry/input controls only.',
        'productionChanged':False,'qualityRepaired':False,'stageIds':IDS,'stockReferenceGroups':rows,'observerChecks':observers,
        'debugWeightReconstruction':reconstruction,'sampledWeightGroups':groups,'all27ReferenceOrGuidePoints':refs,
        'decision':'Use stock per-round results and separate bounded weight evidence to compare the bright-edge loss and counterexample gain; no global iteration cap or parameter scan.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'stockL1':[{p['group']:p['stageNormalizedL1']} for p in s['stockReferenceGroups']],'weightReconstructionMax':max(p['maxReconstructedOutputError'] for p in s['debugWeightReconstruction']),'stockObserverExact':all(p['stockEntireMeanMapBitwiseEqual'] for p in s['observerChecks'])},indent=2))
