"""Controlled input/NRD candidate qualification; no independent dynamic quality acceptance."""
from pathlib import Path
import json,hashlib,struct,importlib.util
import numpy as np
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('endpoint_oracle',HERE.parent/'EndpointReplay/analyze.py');endpoint=importlib.util.module_from_spec(spec);spec.loader.exec_module(endpoint);endpoint.HERE=HERE
raw=endpoint.raw;read=endpoint.read

def sha(b):return hashlib.sha256(b).hexdigest().upper()
def quantize(rgb):
 # Independent float64 reconstruction of pinned SDK LogLuv (including gamut clamp).
 forward=np.array([[.4123907992659595,.3575843393838780,.1804807884018343],[.2126390058715104,.7151686787677559,.0721923153607337],[.0193308187155918,.1191947797946259,.9505321522496608]])
 inverse=np.array([[3.240969941904522,-1.537383177570094,-.4986107602930032],[-.9692436362808803,1.875967501507721,.04155505740717569],[.05563007969699373,-.2039769588889765,1.056971514242878]])
 xyz=rgb@forward.T
 with np.errstate(divide='ignore',invalid='ignore'):
  le=np.clip(409.6*(np.log2(xyz[:,1])+20),0,16383).astype(np.uint32);den=xyz[:,0]+15*xyz[:,1]+3*xyz[:,2];uv=np.divide(xyz[:,:2]*np.array([4,9]),den[:,None],out=np.zeros((len(rgb),2)),where=den[:,None]!=0);uve=np.clip(820*uv,0,511).astype(np.uint32);y=np.exp2((le.astype(float)+.5)/409.6-20);uv=(uve.astype(float)+.5)/820;div=6*uv[:,0]-16*uv[:,1]+12;xy=uv*np.array([9,4])/div[:,None];scale=y/xy[:,1];decoded=np.stack([scale*xy[:,0],y,scale*(1-xy[:,0]-xy[:,1])],axis=1);out=np.maximum(decoded@inverse.T,0);out[le==0]=0
 return out

def summarize(label):
 e=endpoint.summarize(label);d=read(HERE/f'results/{label}/confidence-trial.json');b=raw(HERE/f'results/{label}/confidence-images.bin');assert b[:4]==b'HGC1' and struct.unpack('<3i',b[4:16])==(64,64,5) and sha(b)==d['streamSha256'];images=np.frombuffer(b,dtype='<f4',offset=16).reshape(-1,5,4096,4);nf=len(images);assert nf==len(d['frames']) and d['restored'] and d['released'] and d['independentPools'] and d['externalBytes']==278528
 r=read(HERE/f'results/{label}.json')['selectionLineage'];b=raw(HERE/f'results/{label}/lineage.bin');n=4096;dt=np.dtype([('mode','<i4'),('shader','<i4'),('app','<i4'),('phase','<i4'),('packet',endpoint.P,(5,n)),('meta','<u4',(5,n,4)),('origin','<u4',(5,n,4)),('control',endpoint.P,(3,n)),('currentMaterial','<u4',(n,4,4)),('query','<u4',(n,18,4))]);f=np.frombuffer(b,dtype=dt,offset=16);assert len(f)==nf;tol=read(HERE/'review-contract.json')['confidenceAbsTolerance'];weights=np.array([.2126,.7152,.0722]);maxError=0.;zeroToBright=brightToZero=invalid=static=0
 for fi,(entry,image,frame) in enumerate(zip(d['frames'],images,f)):
  phase=fi//(nf//8);assert entry['phase']==phase and entry['index']==fi and entry['hardReset']==(fi==0 or (fi%(nf//8)==0 and phase>=5)) and entry['confidenceEnabled']==(phase!=0)
  for i,key in enumerate(['rawSha','baselineSha','keepSha','confidenceSha','confidenceInputSha']):assert sha(image[i].tobytes())==entry[key]
  if phase==0:assert np.array_equal(image[1].view('<u4'),image[2].view('<u4')) and np.array_equal(image[1].view('<u4'),image[3].view('<u4'))
  u=frame['query'];v=u.view('<f4');same=np.all(u[:,16,:3]==f[fi-1]['query'][:,16,:3],axis=1) if fi else np.zeros(n,dtype=bool);valid=(u[:,0,3]==1)&(v[:,5,3]>0)&same
  oldLum=(v[:,12,:3].astype(float)@weights)*v[:,9,3];newLum=(quantize(v[:,11,:3].astype(float))@weights)*v[:,9,1];gradient=np.abs(newLum-oldLum)/np.maximum(np.maximum(oldLum,newLum),1e-10);expected=np.where(valid,np.clip(1-gradient,0,1)**8,0);observed=image[4,:,0].astype(float);error=np.abs(expected-observed);maxError=max(maxError,float(error.max()));assert np.all(error<=tol);assert np.all(observed[~valid]==0) and np.all((observed>=0)&(observed<=1))
  invalid+=int((~valid).sum());z=valid&(oldLum==0)&(newLum>1e-6);a=valid&(oldLum>1e-6)&(newLum==0);zeroToBright+=int(z.sum());brightToZero+=int(a.sum());assert np.all(observed[z|a]==0);static+=int(np.count_nonzero(valid&(oldLum>1e-6)&(np.abs(newLum-oldLum)<1e-9)&(observed>.99)))
 rows=[]
 for phase in range(8):
  at=images[phase*(nf//8):(phase+1)*(nf//8),:4,:,:3].astype(float);at[:,1:4]*=64;tail=at[-max(1,nf//16):];means=tail.mean(axis=(0,2));noise=np.mean(np.abs(np.diff(tail,axis=0)),axis=(0,2,3))/np.maximum(np.mean(tail,axis=(0,2,3)),1e-8) if len(tail)>1 else np.zeros(4)
  rows.append({'phase':phase,'tailMeanRgb':means.tolist(),'tailRelativeFrameL1':noise.tolist(),'confidenceMean':float(images[phase*(nf//8):(phase+1)*(nf//8),4,:,0].mean())})
 assert invalid>0 and static>0 and zeroToBright>0 and brightToZero>0
 return {'scope':'Controlled static-primary shared-input NRD trial. Independent plane/alpha/BRDF and confidence input oracle qualified, not independent dynamic MC/lag/energy or product acceptance.','productionChanged':False,'frames':nf,'endpoint':e,'confidenceMaxAbsErrorVsIndependentCodecCpu':maxError,'confidenceAbsTolerance':tol,'invalidEndpointOrPrimaryOrKindRejected':invalid,'zeroToBright':zeroToBright,'brightToZero':brightToZero,'unchangedPositiveConfidenceWitness':static,'meanUnits':'Physical RGB; raw unchanged, NRD RGB multiplied by original SignalScale64','externalTextureBytes':d['externalBytes'],'additionalNrdPoolsAndRaysExcluded':True,'rows':rows}
if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser();p.add_argument('--mode',default='qualification');a=p.parse_args();v=summarize(a.mode);(HERE/(a.mode+'-analysis.json')).write_text(json.dumps(v,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:v[k] for k in ['frames','confidenceMaxAbsErrorVsIndependentCodecCpu','invalidEndpointOrPrimaryOrKindRejected','zeroToBright','brightToZero','unchangedPositiveConfidenceWitness']},indent=2))
