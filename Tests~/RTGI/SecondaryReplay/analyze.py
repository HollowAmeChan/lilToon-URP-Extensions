"""Independent float64 BRDF/transport arithmetic using recorded real shader inputs."""
from pathlib import Path
import json,gzip,lzma,struct,hashlib
import numpy as np
HERE=Path(__file__).resolve().parent
P=np.dtype([('position','<f4',(3,)),('misc','<u4'),('radiance','<u4'),('weight','<f4'),('normal','<u4'),('unused','<u4')])
def raw(p):
 if p.exists():return p.read_bytes()
 q=p.with_suffix(p.suffix+'.gz');return gzip.decompress(q.read_bytes()) if q.exists() else lzma.decompress(p.with_suffix(p.suffix+'.xz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def octdecode(v):
 xy=np.stack((v&65535,v>>16),axis=-1).astype(float)/65535*2-1;n=np.concatenate((xy,(1-np.abs(xy).sum(-1))[...,None]),axis=-1);t=np.clip(-n[...,2],0,1);n[...,:2]+=np.where(n[...,:2]>=0,-t[...,None],t[...,None]);return n/np.linalg.norm(n,axis=-1,keepdims=True)
def rgba(v):return np.stack([v>>(8*c)&255 for c in range(4)],axis=-1).astype(float)/255

def summarize(label='matrix',partial=False):
 d=read(HERE/f'results/{label}.json');r=d['selectionLineage'];b=raw(HERE/'results'/label/'lineage.bin');assert b[:4]==b'HGR1';w,h,nf=struct.unpack('<3i',b[4:16]);n=w*h;dtype=np.dtype([('mode','<i4'),('shader','<i4'),('app','<i4'),('packet',P,(5,n)),('meta','<u4',(5,n,4)),('origin','<u4',(5,n,4)),('control',P,(3,n)),('material','<u4',(n,4,4)),('replay','<u4',(n,8,4))]);frames=np.frombuffer(b,dtype=dtype,offset=16);assert len(frames)==len(r['frames'])
 if not partial:assert len(frames)==4*nf and d['pass'] and r['pass']
 lightRows=np.array([[v[c] for c in 'xyzw'] for v in r.get('analyticLights',[])],dtype=float).reshape(-1,4,4) if r.get('analyticLights') else None
 total=0;exact=quant=visdiff=0;sampleOracleViolations=0;sampleOracleMaxAbs=0.;directionMaxAbs=0.;maxOracle=0.;violations=0;allMeta=0;analytic=env=0;primary=0;lightBrdfError=0.;quantMaxAbs=0.;quantRelative=[]
 for f in frames:
  u=f['replay'];v=u.view('<f4');mat=f['material'];secondary=mat[:,:3].reshape(n,12);view=mat[:,3,:3].copy().view('<f4').astype(float);normal=octdecode(secondary[:,3]);geo=octdecode(secondary[:,10]);albedo=rgba(secondary[:,4])[:,:3];spec=rgba(secondary[:,5]);rough=spec[:,3];emission=secondary[:,6:9].copy().view('<f4').astype(float);flags=secondary[:,11];has=(u[:,0,3]&0x80000000)!=0;L=v[:,4,:3].astype(float);sample=v[:,3,:3].astype(float);pdf=v[:,3,3].astype(float);inv=v[:,5,3].astype(float);visible=v[:,1,3]>0
  assert np.array_equal(secondary[:,:3],f['packet'][2]['position'].copy().view('<u4').reshape(n,3)) and np.array_equal(flags,f['origin'][2,:,2])
  assert np.array_equal(u[:,0,3],f['meta'][2,:,0]) and np.array_equal(u[:,5,3],f['meta'][2,:,2])
  NoL=np.clip(np.sum(normal*L,axis=1),0,1);NoV=np.clip(np.sum(normal*view,axis=1),0,1);H=L+view;hn=np.linalg.norm(H,axis=1);H=np.divide(H,hn[:,None],out=np.zeros_like(H),where=hn[:,None]>0);NoH=np.clip(np.sum(normal*H,axis=1),0,1);VoH=np.clip(np.sum(view*H,axis=1),0,1)
  response=albedo*(NoL/np.pi)[:,None];selected=(rough>=.03)&(NoL>0);a=np.maximum(rough,.03)**2;aa=a*a;den=NoV*np.sqrt(aa+(1-aa)*NoL**2)+NoL*np.sqrt(aa+(1-aa)*NoV**2);G=np.divide(2*NoL,den,out=np.zeros(n),where=den>0);D=aa/(np.pi*(NoH**2*aa+1-NoH**2)**2);F=spec[:,:3]+(1-spec[:,:3])*(1-VoH)[:,None]**5;response+=F*(np.where(selected,D*G/4,0))[:,None];response[np.sum(geo*L,axis=1)<=0]=0
  oracle=emission.copy();oracle+=response*sample*(np.where(has&visible,inv/np.maximum(pdf,1e-12),0))[:,None];error=np.abs(oracle-v[:,1,:3]);limit=np.maximum(5e-6,np.abs(oracle)*.003);maxOracle=max(maxOracle,float(error.max()));violations+=int(np.count_nonzero(error>limit));lightBrdfError=max(lightBrdfError,float(np.max(np.abs(response[has]-v[has,5,:3]))) if np.any(has) else 0)
  total+=n;exact+=int(np.count_nonzero(u[:,0,0]!=u[:,0,1]));quant+=int(np.count_nonzero(u[:,0,0]!=u[:,0,2]));visdiff+=int(np.count_nonzero(v[:,6,0]!=v[:,6,1]));analytic+=int(np.count_nonzero(has&((u[:,0,3]&0x7fffffff)!=0x7ffffffe)));env+=int(np.count_nonzero(has&((u[:,0,3]&0x7fffffff)==0x7ffffffe)))
  if lightRows is not None:
   point=secondary[:,:3].copy().view('<f4').astype(float);index=(u[:,0,3]&0x7fffffff).astype(np.int64);isLocal=has&(index<len(lightRows));localIndex=np.flatnonzero(isLocal)
   if len(localIndex):
    light=lightRows[index[isLocal]];typ=light[:,1,3];radiance=np.zeros((len(light),3));direction=np.zeros_like(radiance);dists=np.zeros(len(light));directional=typ>1.5
    direction[directional]=-light[directional,2,:3];dists[directional]=100000;radiance[directional]=light[directional,1,:3]
    sub=~directional;deltaLight=light[sub,0,:3]-point[isLocal][sub];distance2=np.sum(deltaLight*deltaLight,axis=1);dist=np.sqrt(distance2);direction[sub]=deltaLight/np.maximum(dist[:,None],1e-12);dists[sub]=dist;range2=light[sub,0,3]**2;fade=np.clip(1-(distance2/np.maximum(range2,1e-12))**2,0,1);atten=fade*fade/np.maximum(distance2,1e-12);spot=typ[sub]>.5;cone=np.clip((np.sum(-direction[sub]*light[sub,2,:3],axis=1)-light[sub,2,3])/np.maximum(1e-5,light[sub,3,0]-light[sub,2,3]),0,1);atten*=np.where(spot,cone*cone,1);radiance[sub]=light[sub,1,:3]*atten[:,None]
    err=np.abs(radiance-sample[isLocal]);sampleOracleMaxAbs=max(sampleOracleMaxAbs,float(err.max()));sampleOracleViolations+=int(np.count_nonzero(err>np.maximum(5e-6,np.abs(radiance)*.003)));directionMaxAbs=max(directionMaxAbs,float(np.max(np.abs(direction-L[isLocal]))))
   environment=has&(index==0x7ffffffe);uvWord=f['meta'][2,:,1];uv=np.stack((uvWord&65535,uvWord>>16),axis=1).astype(float)/65535;yy=1-2*uv[:,1];phi=2*np.pi*uv[:,0];rr=np.sqrt(np.maximum(0,1-yy*yy));expectedEnv=np.stack((rr*np.cos(phi),yy,rr*np.sin(phi)),axis=1)
   if np.any(environment):directionMaxAbs=max(directionMaxAbs,float(np.max(np.abs(expectedEnv[environment]-L[environment]))))
  delta=np.abs(v[:,1,:3]-v[:,2,:3]);quantMaxAbs=max(quantMaxAbs,float(delta.max()));valid=np.max(np.abs(v[:,1,:3]),axis=1)>5e-6;quantRelative.extend((np.max(delta[valid],axis=1)/np.maximum(np.max(np.abs(v[valid,1,:3]),axis=1),1e-30)).tolist())
  assert np.all(v[:,6,2]==flags) and np.all(np.isfinite(v[:,1:7]))
 return {'scope':'Real static currentTLAS secondary->chosenLight replay, plus primary->cachedPoint open-interval consistency. Independent doubleLambert/GGX/Schlick/Smith+recombination;sample environmentradiance/visibility are measuredGPUinputs, not independent CPU TLAS/environmenttexture oracle.','partial':partial,'productionChanged':False,'observedFrames':len(frames),'replayedPixels':total,'exactViewPackedLogLuvMismatches':exact,'packedViewPackedLogLuvMismatches':quant,'primaryVisibilityMismatches':visdiff,'validAnalyticChoices':analytic,'validEnvironmentChoices':env,'oracleMaxAbsError':maxOracle,'sampleLightOracleViolations':sampleOracleViolations,'sampleLightOracleMaxAbsError':sampleOracleMaxAbs,'lightDirectionOracleMaxAbsError':directionMaxAbs,'oracleViolatingComponents':violations,'gpuBrdfMaxAbsDifference':lightBrdfError,'packedViewMaxAbsRadianceDifference':quantMaxAbs,'packedViewRelativeErrorP99':float(np.quantile(quantRelative,.99)) if quantRelative else 0,'packedViewRelativeErrorMax':float(max(quantRelative)) if quantRelative else 0,'limitations':'No currentsecondary endpoint reconstruction/normal matching, worldmotion/alpha changes or confidence/history lifecycle accepted. Exact-cache mismatch remains failure under predeclared gate.'}
if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser();p.add_argument('--mode',default='matrix');p.add_argument('--partial',action='store_true');args=p.parse_args();r=summarize(args.mode,args.partial);(HERE/(args.mode+'-analysis.json')).write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8');print(json.dumps(r,indent=2))
