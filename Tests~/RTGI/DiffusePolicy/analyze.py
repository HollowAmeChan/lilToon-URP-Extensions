"""Score simultaneous stock and DI-derived diffuse outputs. No historical pairing assumption."""
import gzip,hashlib,json,struct
from pathlib import Path
import numpy as np
HERE=Path(__file__).resolve().parent
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def gate(a,b):return bool(np.all(np.abs(b-a)<np.maximum(.000005,np.abs(a)*.05)))
def loadmap(name,sha):
 b=raw(HERE/'results/matrix'/name);assert hashlib.sha256(b).hexdigest().upper()==sha
 assert b[:4]==b'HGI1' and struct.unpack('<3i',b[4:16])==(256,256,256) and len(b)==16+65536*24
 return np.frombuffer(b,dtype='<f4',offset=16).reshape(256,256,6)
def summarize():
 doc=read(HERE/'results/matrix.json');r=doc['localStageAudit'];shared=r['shared'];policy=shared['policy'];cs=policy['segments'];bs=r['segments']
 assert [s['seed'] for s in bs]==[0,104729,5347609,6396185]==[s['seed'] for s in cs]
 old=np.array([loadmap(s['publicationMap'],s['publicationMapSha256']) for s in bs]);new=np.array([loadmap(s['map'],s['mapSha256']) for s in cs])
 legacy=read(HERE.parent/'DenseReference/results/fixed-reference.json')['denseReference']['points'];fix=read(HERE.parent/'DenseReference/results/fixed-controls.json')['denseReference']['points'];controls={(p['x'],p['y']):p for p in fix};legacy=[controls.get((p['x'],p['y']),p) for p in legacy]
 refs=[('legacy',p) for p in legacy]+[('new',p) for p in read(HERE.parent/'LocalReference/results/reference.json')['denseReference']['points']]
 points=[];hard=[]
 for group,p in refs:
  x,y=p['x'],p['y'];truth=vec(p['referenceMean']);a=old[:,y,x,3:].astype(np.float64);b=new[:,y,x,3:].astype(np.float64);ma=a.mean(0);mb=b.mean(0)
  delta=np.abs(mb-truth)-np.abs(ma-truth);perSeed=np.abs(b-truth)-np.abs(a-truth);se=perSeed.std(0,ddof=1)/np.sqrt(4)
  limit=np.maximum(np.maximum(.000005,np.abs(truth)*.05),2*np.sqrt(se*se+vec(p['batchMeanStandardError'])**2));bad=np.flatnonzero(delta>limit) if p['converged'] else []
  points.append({'group':group,'x':x,'y':y,'referenceQualified':p['converged'],'referenceMean':truth.tolist(),'referenceSe':vec(p['batchMeanStandardError']).tolist(),'baselineMean':ma.tolist(),'candidateMean':mb.tolist(),'baselineOverReference':(ma/np.maximum(truth,1e-30)).tolist(),'candidateOverReference':(mb/np.maximum(truth,1e-30)).tolist(),'errorIncrease':delta.tolist(),'pairedSeedErrorIncreaseEmpiricalSe':se.tolist(),'hardRegressionLimit':limit.tolist(),'hardRegressionChannels':[int(c) for c in bad] if p['converged'] else None})
  for c in bad:hard.append({'group':group,'x':x,'y':y,'channel':int(c),'increase':float(delta[c]),'limit':float(limit[c])})
 groups={}
 for group in ['legacy','new']:
  rows=[p for p in points if p['group']==group and p['referenceQualified']];truth=np.array([p['referenceMean'] for p in rows]);a=np.array([p['baselineMean'] for p in rows]);b=np.array([p['candidateMean'] for p in rows]);l1a=float(np.abs(a-truth).sum()/truth.sum());l1b=float(np.abs(b-truth).sum()/truth.sum());groups[group]={'qualifiedPoints':len(rows),'baselineNormalizedL1':l1a,'candidateNormalizedL1':l1b,'relativeL1Reduction':1-l1b/l1a}
 perSeed=[];phaseReg=[];checks=[]
 production=read(HERE.parent/'ProductionSeeds/results/matrix.json')['seedAudit']['segments']
 for i,(base,candidate) in enumerate(zip(bs,cs)):
  a=base['spatial'];b=candidate['spatial'];historical=next(s for s in production if s['seed']==base['seed'])
  historicalMap=raw(HERE.parent/'ProductionSeeds/results/matrix'/historical['publicationMap']);baselineBytes=raw(HERE/'results/matrix'/base['publicationMap'])
  checks.append({'seed':base['seed'],'sameActualRawMeanMapBitwise':bool(np.array_equal(old[i,:,:,:3],new[i,:,:,:3])),'stockMeanMapExactlyProduction026':historicalMap==baselineBytes,'sameActualRawFrameMeans':all(np.array_equal(vec(x['raw']),vec(y['raw'])) for x,y in zip(a['frameMeans'],b['frameMeans']))})
  perSeed.append({'seed':base['seed'],'baselineFullGatePass':a['full']['energyGatePass'],'candidateFullGatePass':b['full']['energyGatePass'],'baselinePhaseFailures':sum(not p['energyGatePass'] for p in a['phases']),'candidatePhaseFailures':sum(not p['energyGatePass'] for p in b['phases']),'baselineRelativeErrorPercent':list(vec(a['full']['relativeErrorPercent'])),'candidateRelativeErrorPercent':list(vec(b['full']['relativeErrorPercent'])),'filteredVarianceRatioToBaseline':b['full']['filteredVariance']/a['full']['filteredVariance'],'baselineFilteredToRawVariance':a['full']['filteredVariance']/a['full']['rawVariance'],'candidateFilteredToRawVariance':b['full']['filteredVariance']/b['full']['rawVariance']})
  for x,y in zip(a['phases'],b['phases']):
   if x['energyGatePass'] and not y['energyGatePass']:phaseReg.append([base['seed'],x['phaseX'],x['phaseY']])
 pooledA=np.mean([vec(s['spatial']['full']['raw']) for s in cs],0);pooledB=np.mean([vec(s['spatial']['full']['filtered']) for s in cs],0);phases=[]
 for k in range(16):
  a=np.mean([vec(s['spatial']['phases'][k]['raw']) for s in cs],0);b=np.mean([vec(s['spatial']['phases'][k]['filtered']) for s in cs],0);ba=np.mean([vec(s['spatial']['phases'][k]['raw']) for s in bs],0);bb=np.mean([vec(s['spatial']['phases'][k]['filtered']) for s in bs],0)
  phases.append({'x':k%4,'y':k//4,'baselineGatePass':gate(ba,bb),'candidateGatePass':gate(a,b),'candidateRelativeErrorPercent':((b/a-1)*100).tolist()})
 exact=all(f['mismatches0']==0 for f in shared['frames']) and policy['calibrationMismatches']==0 and policy['calibratedFrames']==1024 and all(all(v for k,v in c.items() if k!='seed') for c in checks)
 eligible=exact and groups['new']['relativeL1Reduction']>=.20 and not hard and not phaseReg and all(not s['baselineFullGatePass'] or s['candidateFullGatePass'] for s in perSeed) and all(s['candidateFilteredToRawVariance']<.15 for s in perSeed) and all(not s['baselineGatePass'] or s['candidateGatePass'] for s in phases)
 return {'scope':'Four simultaneous shared-input seeds, stock branch0 and fixed DI-derived diffuse branch1, five passes. Reference SE and paired four-seed SE are empirical, not formal statistical guarantees. Same diffuse reference model, not independent truth/full-image reference.','productionChanged':False,'comparisonQualified':exact,'eligibleForFurtherEightSeedDynamicHdrPlayerGpuQualification':bool(eligible),'groups':groups,'all34ReferencePoints':points,'hardRegressions':hard,'pairChecks':checks,'perSeed':perSeed,'phasePassToFailPairs':phaseReg,'full':{'candidatePooledGatePass':gate(pooledA,pooledB),'rgbDifferencePercent':((pooledB/pooledA-1)*100).tolist(),'candidatePassingSeeds':sum(s['candidateFullGatePass'] for s in perSeed),'phases':phases,'pooledPhaseFailures':sum(not p['candidateGatePass'] for p in phases)}}
if __name__=='__main__':
 s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({k:s[k] for k in ['comparisonQualified','eligibleForFurtherEightSeedDynamicHdrPlayerGpuQualification','groups','hardRegressions','perSeed','phasePassToFailPairs']},indent=2))
