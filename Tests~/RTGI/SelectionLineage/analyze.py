"""Independently decode all real stage packets and immutable initial metadata."""
from pathlib import Path
import json,gzip,lzma,struct,hashlib
import numpy as np
HERE=Path(__file__).resolve().parent
P=np.dtype([('position','<f4',(3,)),('misc','<u4'),('radiance','<u4'),('weight','<f4'),('normal','<u4'),('unused','<u4')])
def raw(p):
 if p.exists():return p.read_bytes()
 gz=p.with_suffix(p.suffix+'.gz')
 return gzip.decompress(gz.read_bytes()) if gz.exists() else lzma.decompress(p.with_suffix(p.suffix+'.xz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def summarize(label='matrix'):
 d=read(HERE/f'results/{label}.json');r=d['selectionLineage'];path=HERE/'results'/label/r['stream'];blob=raw(path);assert hashlib.sha256(blob).hexdigest().upper()==r['streamSha256']
 assert blob[:4]==b'HGL1';w,h,nf=struct.unpack('<3i',blob[4:16]);assert (w,h)==(64,64);n=w*h
 dtype=np.dtype([('mode','<i4'),('shader','<i4'),('application','<i4'),('packet',P,(5,n)),('meta','<u4',(5,n,4)),('origin','<u4',(5,n,4)),('control',P,(3,n))]);frames=np.frombuffer(blob,dtype=dtype,offset=16);assert len(frames)==4*nf==len(r['frames'])
 y,x=np.indices((h,w));addresses=((y//16)*(w//16)*256+(x//16)*256+(y%16)*16+x%16).reshape(-1)
 rows=[]
 for mode in range(4):
  part=frames[mode*nf:(mode+1)*nf];seed=(mode+1)*1000;assert np.array_equal(part['mode'],np.full(nf,mode)) and np.array_equal(part['shader'],np.arange(seed,seed+nf))
  initialMeta=np.array(part['meta'][:,0]);initialPacket=np.array(part['packet'][:,0]);checks=previous=spatial=0;normalDrift=radianceDrift=0;kinds=set();validLights=envLights=0
  for fi,f in enumerate(part):
   packets=f['packet'];metas=f['meta'];origins=f['origin'];pw=packets.view('<u4').reshape(5,n,8);cw=f['control'].view('<u4').reshape(3,n,8)
   assert np.array_equal(np.array([pw[3],pw[2],pw[1]]),cw[:,addresses,:])
   initial=origins[0];valid=initial[:,2]!=0;assert np.all(initial[valid,0]==seed+fi) and np.array_equal(initial[valid,1],np.arange(n,dtype=np.uint32)[valid]);assert np.all(initial[valid,3]==(seed+fi+1)*n+initial[valid,1]+1)
   kinds.update(map(int,initial[valid,2]));hasLight=(metas[0,:,0]&0x80000000)!=0;validLights+=int(hasLight.sum());envLights+=int(((metas[0,:,0]&0x7fffffff)==0x7ffffffe).sum())
   for stage in range(1,5):
    valid=(packets[stage]['misc']&255)!=0;o=origins[stage,valid];ix=o[:,0].astype(np.int64)-seed;pixel=o[:,1].astype(np.int64);assert np.all(o[:,2]!=0) and np.all((ix>=0)&(ix<=fi)&(pixel<n))
    assert np.array_equal(metas[stage,valid],initialMeta[ix,pixel]);assert np.array_equal(packets[stage]['position'][valid].view('<u4'),initialPacket[ix,pixel]['position'].copy().view('<u4'))
    assert np.all(((packets[stage]['misc'][valid]>>8)&255)==fi-ix)
    assert np.all(metas[stage,~valid]==0) and np.all(origins[stage,~valid]==0);checks+=int(valid.sum())
    normalDrift+=int(np.count_nonzero(packets[stage]['normal'][valid]!=initialPacket[ix,pixel]['normal']));radianceDrift+=int(np.count_nonzero(packets[stage]['radiance'][valid]!=initialPacket[ix,pixel]['radiance']))
   expected=1 if mode&2 else 2
   assert np.array_equal(pw[3],pw[expected]) and np.array_equal(metas[3],metas[expected]) and np.array_equal(origins[3],origins[expected])
   assert np.array_equal(pw[4],pw[2]) and np.array_equal(metas[4],metas[2]) and np.array_equal(origins[4],origins[2])
   previous+=int(np.count_nonzero((packets[1]['misc']&255!=0)&(origins[1,:,0]<seed+fi)))
   spatial+=int(np.count_nonzero(np.any(origins[2,:,:2]!=origins[1,:,:2],axis=1)))
  rows.append({'mode':mode,'frames':nf,'wholeControlTracePacketMismatches':0,'immutableInitialSourceChecks':checks,'selectedPreviousFrame':previous,'spatialDifferentOrigin':spatial,'initialSampleKinds':sorted(kinds),'validSecondaryDiChoices':validLights,'environmentChoices':envLights,'selectedNormalWordDifferentFromInitial':normalDrift,'selectedLogLuvWordDifferentFromInitial':radianceDrift})
 for f in r['frames']:assert f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha']
 return {'scope':'Actual64²dual-featureGI original/math-identical lineage path, four T/S modes. Static camera/legal scene, no dynamic confidence/transport replay. Global original packet and physical publication exact equality; metadata checked against immutable initial stream, not float search.','productionChanged':False,'frames':len(frames),'originalPacketsCompared':len(frames)*n*3,'logicalBufferBytes':r['logicalBufferBytes'],'rows':rows,'sourceChecks':sum(v['immutableInitialSourceChecks'] for v in rows),'allOriginalPublicationHashesExact':True,'coverageLimit':'Ordinary temporal previous/current selection, first-frame reset and spatial/S-off/history/lastShade verified. Not claimed exhaustive age-limit/Jacobian/disocclusion/camera-motion branch coverage; no alpha/light/geometry transition oracle yet.'}
if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser();p.add_argument('--mode',default='matrix',choices=['qualification','matrix','compat']);args=p.parse_args();r=summarize(args.mode);(HERE/(args.mode+'-analysis.json')).write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8');print(json.dumps(r,indent=2))
