"""Decode real branch events and crosscheck original GI/provenance at each frame."""
from pathlib import Path
import json,gzip,lzma,struct,hashlib
import numpy as np
HERE=Path(__file__).resolve().parent
P=np.dtype([('position','<f4',(3,)),('misc','<u4'),('radiance','<u4'),('weight','<f4'),('normal','<u4'),('unused','<u4')])
FLAGS={1:'entry',2:'eligible',4:'projectionValid',8:'sdkEntered',16:'previousSurfaceInvalid',32:'neighborRejected',64:'materialRejected',128:'previousReservoirInvalid',256:'matched',512:'jacobianRejected',1024:'ageRejected',2048:'selectedPrevious',4096:'previousNotSelected',8192:'noMatch',16384:'temporalDisabled',32768:'primaryInvalid',65536:'projectionInvalid'}
def raw(p):
 if p.exists():return p.read_bytes()
 gz=p.with_suffix(p.suffix+'.gz');return gzip.decompress(gz.read_bytes()) if gz.exists() else lzma.decompress(p.with_suffix(p.suffix+'.xz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def summarize(label='matrix'):
 doc=read(HERE/f'results/{label}.json');r=doc['selectionLineage'];b=raw(HERE/'results'/label/r['stream']);assert hashlib.sha256(b).hexdigest().upper()==r['streamSha256'] and b[:4]==b'HGE1';w,h,nf=struct.unpack('<3i',b[4:16]);assert (w,h)==(64,64);n=w*h
 dtype=np.dtype([('mode','<i4'),('shader','<i4'),('app','<i4'),('generation','<i4'),('packet',P,(5,n)),('meta','<u4',(5,n,4)),('origin','<u4',(5,n,4)),('control',P,(3,n)),('event','<u4',(n,4))]);frames=np.frombuffer(b,dtype=dtype,offset=16);assert len(frames)==5*nf==len(r['frames'])
 y,x=np.indices((h,w));address=((y//16)*(w//16)*256+(x//16)*256+(y%16)*16+x%16).reshape(-1);rows=[]
 for case,mode in enumerate([7,11,15,19,23]):
  part=frames[case*nf:(case+1)*nf];seed=(case+1)*1000;assert np.all(part['mode']==mode) and np.array_equal(part['shader'],np.arange(seed,seed+nf));initialMeta=np.array(part['meta'][:,0]);initialPacket=np.array(part['packet'][:,0]);events={v:0 for v in FLAGS.values()};checks=invalid=black=0
  for fi,f in enumerate(part):
   ps=f['packet'];ms=f['meta'];os=f['origin'];pw=ps.view('<u4').reshape(5,n,8);cw=f['control'].view('<u4').reshape(3,n,8);e=f['event'];mask=e[:,0]
   assert np.array_equal(np.array([pw[3],pw[2],pw[1]]),cw[:,address,:])
   assert np.all(mask&1) and np.all(((mask&8)==0)|((mask&6)==6))
   jacReject=(mask&512)!=0;ageReject=(mask&1024)!=0;jac=e[:,3].copy().view('<f4');assert np.all(~np.isfinite(jac[jacReject])|(jac[jacReject]>.1*100)|(jac[jacReject]<.1));assert np.all(e[ageReject,1]+1>e[ageReject,2])
   assert not np.any(((mask&2048)!=0)&((mask&(512|1024|8192))!=0))
   for flag,name in FLAGS.items():events[name]+=int(np.count_nonzero(mask&flag))
   invalid+=int(np.count_nonzero(os[0,:,2]==0));black+=int(np.count_nonzero(os[0,:,2]==3))
   expectedEmpty=case==2 and nf//4<=fi<nf//2
   if expectedEmpty:assert np.all(ps['misc']&255==0) and np.all(ms==0) and np.all(os==0) and np.all(mask&32768)
   for stage in range(1,5):
    valid=(ps[stage]['misc']&255)!=0;o=os[stage,valid];si=o[:,0].astype(np.int64)-seed;pix=o[:,1].astype(np.int64);assert np.all(o[:,2]!=0) and np.all((si>=0)&(si<=fi)&(pix<n))
    assert np.array_equal(ms[stage,valid],initialMeta[si,pix]) and np.array_equal(ps[stage]['position'][valid].copy().view('<u4'),initialPacket[si,pix]['position'].copy().view('<u4'))
    assert np.all(((ps[stage]['misc'][valid]>>8)&255)==fi-si) and np.all(o[:,3]==(o[:,0]+1)*n+o[:,1]+1)
    assert np.all(ms[stage,~valid]==0) and np.all(os[stage,~valid]==0);checks+=int(valid.sum())
    if case==4 and fi>=nf//2:assert np.all(si>=nf//2)
   assert np.array_equal(pw[3],pw[1]) and np.array_equal(ms[3],ms[1]) and np.array_equal(os[3],os[1]);assert np.array_equal(pw[4],pw[2]) and np.array_equal(ms[4],ms[2]) and np.array_equal(os[4],os[2])
   saved=r['frames'][case*nf+fi];assert saved['generation']==f['generation'] and saved['expectedEmpty']==expectedEmpty and saved['eventCounts']==[int(np.count_nonzero(mask&(1<<bit))) for bit in range(17)]
  rows.append({'case':case,'mode':mode,'frames':nf,'sourceChecks':checks,'invalidInitial':invalid,'blackInitial':black,'generations':list(map(int,np.unique(part['generation']))),'events':events})
 for f in r['frames']:assert f['controlSha']==f['traceSha'] and f['rawControlSha']==f['rawTraceSha'] and f['filteredControlSha']==f['filteredTraceSha']
 assert rows[0]['events']['ageRejected']>0 and rows[1]['events']['jacobianRejected']>0 and rows[1]['events']['noMatch']>0 and rows[1]['events']['projectionInvalid']>0
 assert rows[2]['invalidInitial']>0 and rows[2]['events']['previousSurfaceInvalid']>0 and rows[3]['blackInitial']>0 and len(rows[4]['generations'])==2
 return {'scope':'Actual fixed diagnostic fixtures, original/private dualGI exact packet/raw/NRD equality. Source-event predicates verified; no sampling rule change, production history change or transport confidence qualification.','productionChanged':False,'frames':len(frames),'originalPacketsCompared':len(frames)*n*3,'sourceChecks':sum(x['sourceChecks'] for x in rows),'logicalBufferBytes':r['logicalBufferBytes'],'contextGenerations':r['contextGenerations'],'rows':rows,'unreachableOptions':'Production zero-initialized enableFallbackSampling/permutation and activeCheckerboardField0 unchanged; these optional SDK paths were not enabled for coverage.','remaining':'Previous-reservoir-invalid was not executed; current producer invariant documented separately, not universal/fault-injection proof. No source-material replay/currentTLAS confidence, dynamic oracle or GPU budget proven.'}
if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser();p.add_argument('--mode',default='matrix',choices=['qualification','matrix','compat']);args=p.parse_args();r=summarize(args.mode);(HERE/(args.mode+'-analysis.json')).write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8');print(json.dumps(r,indent=2))
