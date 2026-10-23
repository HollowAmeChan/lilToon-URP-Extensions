"""Qualify sparse observer against actual037 publications and existing full stage copies."""
from pathlib import Path
import json,gzip,hashlib,struct,argparse
import numpy as np
HERE=Path(__file__).resolve().parent
PREV=HERE.parent/'DiffusePolicy'
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def stages(label):
 d=read(HERE/f'results/{label}.json')['localStageAudit'];r=d['shared']['paired'];out=[]
 for seg in r['stageSegments']:
  b=raw(HERE/'results'/label/seg['stream']);assert hashlib.sha256(b).hexdigest().upper()==seg['streamSha256']
  assert b[:4]==b'HGB1';w,h,seed,branches,ns,npix=struct.unpack('<6i',b[4:28]);assert (w,h,seed,branches,ns,npix)==(256,256,seg['seed'],2,9,28)
  ids=np.frombuffer(b,dtype='<i4',offset=28,count=ns);coords=np.frombuffer(b,dtype='<i4',offset=28+ns*4,count=npix*2).reshape(npix,2)
  dtype=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(2,6)),('rgba','<f4',(2,9,28,4))]);frames=np.frombuffer(b,dtype=dtype,offset=28+ns*4+npix*8)
  assert len(frames)==seg['frames'] and np.isfinite(frames['rgba']).all();assert np.all(frames['writes']==np.array([1,1,1,1,1,4])[None,None,:])
  out.append((seg,ids,coords,frames))
 return d,r,out
def compare(label):
 d,r,streams=stages(label);old=read(PREV/f'results/{label}.json')['localStageAudit'];pr=old['shared']['policy'];checks=[]
 for current,previous,pc,oc,stream in zip(d['segments'],old['segments'],r['segments'],pr['segments'],streams):
  seg,ids,coords,frames=stream;assert current['seed']==previous['seed']==pc['seed']==oc['seed']==seg['seed']
  base=raw(HERE/'results'/label/current['publicationMap']);ref=raw(PREV/'results'/label/previous['publicationMap'])
  candidate=raw(HERE/'results'/label/pc['map']);candidateRef=raw(PREV/'results'/label/oc['map'])
  b=raw(HERE/'results'/label/pc['stream']);oldb=raw(PREV/'results'/label/oc['stream']);assert b[:236]==oldb[:236]
  ct=np.dtype([('shader','<i4'),('application','<i4'),('rgb','<f4',(27,3))]);a=np.frombuffer(b,dtype=ct,offset=236);o=np.frombuffer(oldb,dtype=ct,offset=236)
  hp=raw(HERE/'results'/label/current['stream']);assert hp[:4]==b'HGP1';pt=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(12,27,3))]);points=np.frombuffer(hp,dtype=pt,offset=292)
  refs=d['referencePoints'];albedo=np.array([[round(p['albedo'][c]*255) for c in 'xyz'] for p in refs],dtype=np.float32)/np.float32(255);factor=np.float32(64)*albedo
  hids=[-2,-1,3,4,5,6,9,101,102,103,104,100];expected=np.array([points['rgb'][:,hids.index(int(i))] for i in ids]).transpose(1,0,2,3)
  remod=frames['rgba'][:,0,:,:27,:3]*factor[None,None,:,:]
  check={'seed':seg['seed'],'stockFullMeanMapExact037':base==ref,'candidateFullMeanMapExact037':candidate==candidateRef,'candidate27PerFrameExact037':bool(np.array_equal(a['shader'],o['shader']) and np.array_equal(a['rgb'],o['rgb'])),'gatherStockAll9StagesMatchesMainCopy27':bool(np.array_equal(remod,expected)),'gatherMaxRemodulationDifference':float(np.max(np.abs(remod-expected))),'addedGuideMaxError':max(seg['firstGuide']['maxError'],seg['lastGuide']['maxError'])}
  checks.append(check)
 passed=all(all(v for k,v in c.items() if k not in ['seed','gatherMaxRemodulationDifference','addedGuideMaxError']) and c['addedGuideMaxError']<=.0005 for c in checks) and r['gatherBitExact'] and r['gatherComparedComponents']==112 and r['calibrationMismatches']==0
 return {'scope':'Observer invariance and sparse stage-source qualification; not policy acceptance. Extra guide receiver was added without changing actual publications.','qualified':passed,'frames':r['calibratedFrames'],'checks':checks}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--mode',default='qualification',choices=['qualification','matrix']);args=p.parse_args();r=compare(args.mode);(HERE/(args.mode+'-observer.json')).write_text(json.dumps(r,indent=2)+'\n',encoding='utf-8');print(json.dumps(r,indent=2));assert r['qualified']
