"""Current same-input DI reference/operator variant scope; stock main is separate."""
from pathlib import Path
import json,gzip,hashlib,struct,argparse
import numpy as np
HERE=Path(__file__).resolve().parent;PREV=HERE.parent/'PairedStages'
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def stages(label):
 d=read(HERE/f'results/{label}.json')['localStageAudit'];r=d['shared']['exchange'];out=[]
 for seg in r['stageSegments']:
  b=raw(HERE/'results'/label/seg['stream']);assert hashlib.sha256(b).hexdigest().upper()==seg['streamSha256'] and b[:4]==b'HGB1'
  w,h,seed,nb,ns,npix=struct.unpack('<6i',b[4:28]);assert (w,h,seed,nb,ns,npix)==(256,256,seg['seed'],2,9,28)
  ids=np.frombuffer(b,dtype='<i4',offset=28,count=9);coords=np.frombuffer(b,dtype='<i4',offset=64,count=56).reshape(28,2)
  dtype=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(2,6)),('rgba','<f4',(2,9,28,4))]);frames=np.frombuffer(b,dtype=dtype,offset=288)
  assert len(frames)==seg['frames'] and np.isfinite(frames['rgba']).all() and np.all(frames['writes']==np.array([1,1,1,1,1,4])[None,None,:]);out.append((seg,ids,coords,frames))
 return d,r,out
def compare(label):
 oldLabel='qualification' if label=='initial-local-qualification' else label
 d,r,streams=stages(label);old=read(PREV/f'results/{oldLabel}.json')['localStageAudit'];prior=old['shared']['paired'];checks=[]
 for s,os,c,oc,current,previous in zip(d['segments'],old['segments'],r['segments'],prior['segments'],streams,prior['stageSegments']):
  seg,ids,coords,frames=current;assert s['seed']==os['seed']==c['seed']==oc['seed']==seg['seed']==previous['seed']
  main=raw(HERE/'results'/label/s['publicationMap']);oldMain=raw(PREV/'results'/oldLabel/os['publicationMap']);reference=raw(HERE/'results'/label/c['referenceMap']);oldReference=raw(PREV/'results'/oldLabel/oc['map'])
  b=raw(PREV/'results'/oldLabel/previous['stream']);oldFrames=np.frombuffer(b,dtype=frames.dtype,offset=288)
  checks.append({'seed':seg['seed'],'mainMeanMapExact038':main==oldMain,'referenceMeanMapExact038DI':reference==oldReference,'referenceAll9StagesExact038DI':bool(np.array_equal(frames['rgba'][:,0],oldFrames['rgba'][:,1])),'sameFirstFiveStageSampledRGBA':bool(np.array_equal(frames['rgba'][:,0,:5],frames['rgba'][:,1,:5])),'changedFirstLaterRgbComponents':int(np.count_nonzero(frames['rgba'][:,0,5,:,:3]!=frames['rgba'][:,1,5,:,:3])),'addedGuideMaxError':max(seg['firstGuide']['maxError'],seg['lastGuide']['maxError'])})
 currentFrames=d['shared']['frames'];oldFrames=old['shared']['frames'];assert len(currentFrames)==len(oldFrames) and len(d['segments'])==len(old['segments'])==len(r['segments'])==len(prior['segments']);sameMain=all(c['shaderFrame']==o['shaderFrame'] and c['baselineSha256']==o['baselineSha256'] for c,o in zip(currentFrames,oldFrames));sameReference=all(c['shaderFrame']==o['shaderFrame'] and c['branch0Sha256']==o['branch1Sha256'] for c,o in zip(currentFrames,oldFrames))
 gen=HERE/'generated'/label;a=(gen/'HoRTGIDiffusePolicyAtrous.hlsl').read_text(encoding='utf-8-sig');b=(gen/'ExchangeVariant.hlsl').read_text(encoding='utf-8-sig');start=a.index('    // Use fixed pair exchange coefficients');end=a.index('\n    #if( NRD_MODE == SH )',start);stock='    float4 filteredDiffuseIlluminationAndVariance = float4(sumDiffuseIlluminationAndVariance / float4(sumWDiffuse.xxx, sumWDiffuse * sumWDiffuse));'
 sourceOnly=b==a[:start]+stock+a[end:];wrapperOnly=(gen/'ExchangeVariant.compute').read_text(encoding='utf-8-sig')==(gen/'HoRTGIDiffusePolicyAtrous.compute').read_text(encoding='utf-8-sig').replace('HoRTGIDiffusePolicyAtrous.hlsl','ExchangeVariant.hlsl')
 fullFirst=(r.get('firstStageComparedFrames',0)==len(currentFrames) and all(f['referenceSha256']==f['variantSha256'] and f['components']==262144 for f in r.get('firstInputFrames',[])))
 local=all(c['mainMeanMapExact038'] and c['referenceMeanMapExact038DI'] and c['referenceAll9StagesExact038DI'] and c['sameFirstFiveStageSampledRGBA'] and c['changedFirstLaterRgbComponents']>0 and c['addedGuideMaxError']<=.0005 for c in checks)
 passed=local and sameMain and sameReference and sourceOnly and wrapperOnly and r['calibrationMismatches']==0 and (fullFirst or label=='initial-local-qualification')
 return {'scope':'Same-input mainstock/fixed037 DI reference/single normalization operator variant. Initial local-only qualification is not whole-image first-input proof.','qualified':passed,'wholeFirstImageQualified':fullFirst,'mainFullNrdOutputFramesExact038':sameMain,'referenceFullNrdOutputFramesExact038DI':sameReference,'onlyNormalizationBlockChanged':sourceOnly,'onlyWrapperBodyNameChanged':wrapperOnly,'frames':len(currentFrames),'checks':checks}
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--mode',default='qualification',choices=['initial-local-qualification','qualification','matrix']);args=p.parse_args();s=compare(args.mode);(HERE/(args.mode+'-scope.json')).write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps(s,indent=2));assert s['qualified']
