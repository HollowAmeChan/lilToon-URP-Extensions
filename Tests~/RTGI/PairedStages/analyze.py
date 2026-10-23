"""Actual paired stage provenance and moment/variance diagnosis, no algorithm changes."""
import json,gzip,struct
from pathlib import Path
import numpy as np
from compare_observer import read,raw,stages,compare,HERE,PREV
LUMA=np.array([.2126,.7152,.0722],dtype=np.float64)
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def stat(a):
 a=np.asarray(a,dtype=np.float64);return {'mean':float(a.mean()),'median':float(np.median(a)),'p10':float(np.quantile(a,.1)),'p90':float(np.quantile(a,.9)),'zeroFraction':float(np.mean(a==0))}
def summarize():
 d,r,streams=stages('matrix');invariance=compare('matrix');assert invariance['qualified'];ids=streams[0][1].tolist();coords=streams[0][2].tolist();allv=np.array([x[3]['rgba'] for x in streams],dtype=np.float64)
 refs=d['referencePoints']+[r['addedReference']];assert len(refs)==28 and [(p['x'],p['y']) for p in refs]==[tuple(p) for p in coords]
 # Match existing C# physical stage accounting, distinct from final F16 publication.
 albedo=np.array([[round(p['albedo'][c]*255) for c in 'xyz'] for p in refs],dtype=np.float32)/np.float32(255);factor=(np.float32(64)*albedo).astype(np.float64)
 rgb=allv[...,:3]*factor[None,None,None,None,:,:];means=rgb.mean(axis=(0,1)) # [branch, stage,point,channel]
 newTruth=np.array([vec(p['referenceMean']) for p in refs[:18]]);groups=[]
 for j,id in enumerate(ids):
  values=[]
  for b in range(2):values.append(float(np.abs(means[b,j,:18]-newTruth).sum()/newTruth.sum()))
  item={'stage':id,'new18BaselineReferenceL1':values[0],'new18DIReferenceL1':values[1]}
  if id in [9,101,102,103]:
   item['baselineActualVariance']=stat(allv[:,:,0,j,:18,3]);item['diActualVariance']=stat(allv[:,:,1,j,:18,3])
  groups.append(item)
 pairs=[]
 for x,y in [(141,157),(196,141)]:
  p=coords.index([x,y]);truth=vec(refs[p]['referenceMean']);rows=[]
  for j,id in enumerate(ids):
   a=means[0,j,p];b=means[1,j,p];row={'stage':id,'baselineMean':a.tolist(),'diMean':b.tolist(),'baselineOverReference':(a/truth).tolist(),'diOverReference':(b/truth).tolist(),'errorIncrease':(np.abs(b-truth)-np.abs(a-truth)).tolist(),'meanDiMinusBaseline':(b-a).tolist()}
   if id in [4,6,9,101,102,103]:
    values=[]
    for branch in range(2):
     a4=allv[:,:,branch,j,p,:];variance=np.maximum(0,a4[...,3]-(a4[...,:3]@LUMA)**2) if id in [4,5,6] else a4[...,3]
     item=stat(variance);item['units']='NRD demodulated radiance squared';item['alphaMeaning']='second moment; variance reconstructed offline approximately' if id in [4,5,6] else 'actual GPU variance';item['phi2NextRangeFloorFraction']=float(np.mean(2*np.sqrt(np.maximum(variance,0))<=1e-4)) if id in [9,101,102,103] else None;values.append(item)
    row['baselineVariance']=values[0];row['diVariance']=values[1]
   elif id==5:row['alphaMeaning']='HistoryFix fast/responsive path alpha zero; NOT a temporal variance estimate';row['baselineAuxAlpha']=stat(allv[:,:,0,j,p,3]);row['diAuxAlpha']=stat(allv[:,:,1,j,p,3])
   elif id==104:row['alphaMeaning']='actual history length, NOT variance';row['baselineHistoryLength']=stat(allv[:,:,0,j,p,3]);row['diHistoryLength']=stat(allv[:,:,1,j,p,3])
   else:row['alphaMeaning']='hit distance, NOT variance'
   rows.append(row)
  pub=[]
  for branch in [0,1]:
   values=[]
   for i,(base,experiment) in enumerate(zip(d['segments'],r['segments'])):
    blob=raw(HERE/'results/matrix'/(base['publicationMap'] if branch==0 else experiment['map']));m=np.frombuffer(blob,dtype='<f4',offset=16).reshape(256,256,6);values.append(m[y,x,3:].astype(np.float64))
   mean=np.mean(values,0);pub.append(mean)
  pairs.append({'x':x,'y':y,'referenceMean':truth.tolist(),'stages':rows,'actualPhysicalBaselinePublication':pub[0].tolist(),'actualPhysicalDiPublication':pub[1].tolist(),'physicalPublicationErrorIncrease':(np.abs(pub[1]-truth)-np.abs(pub[0]-truth)).tolist()})
 upstream=[]
 for j,id in enumerate(ids):
  a=allv[:,:,0,j];b=allv[:,:,1,j];upstream.append({'stage':id,'rgbDifferentComponents':int(np.count_nonzero(a[...,:3]!=b[...,:3])),'alphaDifferentComponents':int(np.count_nonzero(a[...,3]!=b[...,3])),'maxRgbDifferenceNRD':float(np.max(np.abs(a[...,:3]-b[...,:3])))})
 # Actual 3x3 pre-Smem clamped moments at counter141157; no fabricated donor data.
 neighbors=[[x,y] for x in range(140,143) for y in range(156,159)];ni=[coords.index(p) for p in neighbors];k=np.array([.25 if x==141 and y==157 else .125 if x==141 or y==157 else .0625 for x,y in neighbors]);assert k.sum()==1
 blur=[];j=ids.index(6)
 for branch in range(2):
  values=allv[:,:,branch,j,:,:][:,:,ni,:];luma=values[...,:3]@LUMA
  temporal=np.maximum(0,values[...,3]-luma*luma);weightedRgb=(values[...,:3]*k[None,None,:,None]).sum(2);weightedM2=(values[...,3]*k[None,None,:]).sum(2)
  stock=np.maximum(0,weightedM2-(weightedRgb@LUMA)**2);di=(temporal*k[None,None,:]).sum(2)
  blur.append({'inputBranch':branch,'stockSpatialPlusTemporalBlurVariance':stat(stock),'diTemporalOnlyBlurVariance':stat(di),'stockPhi2Bandwidth':stat(np.maximum(1e-4,2*np.sqrt(stock))),'diPhi2Bandwidth':stat(np.maximum(1e-4,2*np.sqrt(di))),'stockFloorFraction':float(np.mean(2*np.sqrt(stock)<=1e-4)),'diFloorFraction':float(np.mean(2*np.sqrt(di)<=1e-4)),'scope':'Offline reconstruction of computeVariance from nine actual Clamp RGBA samples. No geometry/range tap reconstruction or new GPU intervention.'})
 return {'scope':'Four fixed seeds, actual28 receivers, nine stages each branch, stock vs fixed rejected037 policy. Observation only, not new acceptance/ablation. Final alpha is historyLength; Pre alpha hitdistance; Temporal/Clamp alpha moment with approximate offline variance; HistoryFix fast alpha zero excluded. No componentwise albedo conversion of variance.','observer':invariance,'upstreamStageEquality':upstream,'new18StageError':groups,'oldFailureStageTrajectories':pairs,'counter141157NineNeighborVarianceBlur':blur,'productionChanged':False,'policyAccepted':False,'decision':'Keep037 rejected. Use stage/variance evidence to select a future mechanism only; no parameter scan.'}
if __name__=='__main__':
 s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({'upstream':s['upstreamStageEquality'],'new18':s['new18StageError'],'counterBlur':s['counter141157NineNeighborVarianceBlur']},indent=2))
