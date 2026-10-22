"""Eight matched seeds, complete spatial domains and all old/new reference controls."""
import gzip
import hashlib
import json
import struct
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
SEEDS=[0,104729,1153305,2201881,3250457,4299033,5347609,6396185]
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def gate(a,b):return bool(np.all(np.abs(b-a)<np.maximum(.000005,np.abs(a)*.05)))
def load(folder,label='matrix'):
    d=read(folder/f'results/{label}.json');r=d['seedAudit'];maps=[]
    for s in r['segments']:
        b=raw(folder/'results'/label/s['publicationMap']);assert hashlib.sha256(b).hexdigest().upper()==s['publicationMapSha256']
        assert b[:4]==b'HGI1' and struct.unpack('<3i',b[4:16])==(256,256,s['measured']) and len(b)==16+65536*24
        maps.append(np.frombuffer(b,dtype='<f4',offset=16).reshape(256,256,6))
    return d,r,np.array(maps)
def summarize():
    _,r,new=load(HERE);_,br,old=load(HERE.parent/'ProductionSeeds');assert [s['seed'] for s in r['segments']]==SEEDS
    refs=[('legacy',p) for p in r['referencePoints']]+[('new',p) for p in read(HERE.parent/'LocalReference/results/reference.json')['denseReference']['points']]
    points=[];hard=[]
    for group,p in refs:
        x,y=p['x'],p['y'];reference=vec(p['referenceMean']);a=old[:,y,x,3:].astype(np.float64);b=new[:,y,x,3:].astype(np.float64);ma=a.mean(0);mb=b.mean(0)
        delta=np.abs(mb-reference)-np.abs(ma-reference);perSeedDelta=np.abs(b-reference)-np.abs(a-reference);se=perSeedDelta.std(0,ddof=1)/np.sqrt(8)
        limit=np.maximum(np.maximum(.000005,reference*.05),2*np.sqrt(se*se+vec(p['batchMeanStandardError'])**2));bad=np.flatnonzero(delta>limit) if p['converged'] else np.array([],dtype=int)
        item={'group':group,'x':x,'y':y,'referenceQualified':p['converged'],'referenceMean':reference.tolist(),'referenceSe':vec(p['batchMeanStandardError']).tolist(),
            'baselineMean':ma.tolist(),'candidateMean':mb.tolist(),'candidateSeedSe':(b.std(0,ddof=1)/np.sqrt(8)).tolist(),
            'baselineOverReference':(ma/np.maximum(reference,1e-30)).tolist(),'candidateOverReference':(mb/np.maximum(reference,1e-30)).tolist(),
            'errorIncrease':delta.tolist(),'pairedErrorIncreaseEmpiricalSe':se.tolist(),'hardRegressionLimit':limit.tolist(),'hardRegressionChannels':bad.tolist() if p['converged'] else None}
        points.append(item)
        for c in bad:hard.append({'group':group,'x':x,'y':y,'channel':int(c),'increase':float(delta[c]),'limit':float(limit[c])})
    groups={}
    for group in ['legacy','new']:
        rows=[p for p in points if p['group']==group and p['referenceQualified']];truth=np.array([p['referenceMean'] for p in rows]);a=np.array([p['baselineMean'] for p in rows]);b=np.array([p['candidateMean'] for p in rows]);l1a=float(np.abs(a-truth).sum()/truth.sum());l1b=float(np.abs(b-truth).sum()/truth.sum())
        groups[group]={'qualifiedPoints':len(rows),'baselineNormalizedL1':l1a,'candidateNormalizedL1':l1b,'relativeL1Reduction':1-l1b/l1a}
    perSeed=[];observers=[];phaseReg=[]
    for i,(s,base) in enumerate(zip(r['segments'],br['segments'])):
        a=np.array([vec(f['raw']) for f in base['frameMeans']]);b=np.array([vec(f['raw']) for f in s['frameMeans']])
        observers.append({'seed':s['seed'],'fullRawMapBitwiseEqual':np.array_equal(old[i,:,:,:3],new[i,:,:,:3]),'rawFrameMeansBitwiseEqual':np.array_equal(a,b)})
        perSeed.append({'seed':s['seed'],'baselineFullGatePass':base['full']['energyGatePass'],'candidateFullGatePass':s['full']['energyGatePass'],
            'baselinePhaseFailures':sum(not p['energyGatePass'] for p in base['phases']),'candidatePhaseFailures':sum(not p['energyGatePass'] for p in s['phases']),
            'filteredVarianceRatioToBaseline':s['full']['filteredVariance']/base['full']['filteredVariance'],'candidateFilteredToRawVariance':s['full']['filteredVariance']/s['full']['rawVariance']})
        for a,b in zip(base['phases'],s['phases']):
            if a['energyGatePass'] and not b['energyGatePass']:phaseReg.append([s['seed'],a['phaseX'],a['phaseY']])
    fullA=np.array([vec(s['full']['raw']) for s in r['segments']]).mean(0);fullB=np.array([vec(s['full']['filtered']) for s in r['segments']]).mean(0);phases=[]
    for i in range(16):
        a=np.array([vec(s['phases'][i]['raw']) for s in r['segments']]).mean(0);b=np.array([vec(s['phases'][i]['filtered']) for s in r['segments']]).mean(0)
        ba=np.array([vec(s['phases'][i]['raw']) for s in br['segments']]).mean(0);bb=np.array([vec(s['phases'][i]['filtered']) for s in br['segments']]).mean(0)
        phases.append({'x':i%4,'y':i//4,'baselineGatePass':gate(ba,bb),'candidateGatePass':gate(a,b),'rgbDifferencePercent':((b/a-1)*100).tolist()})
    previous=read(HERE.parent/'AtrousCandidate/results/matrix.json')['localStageAudit'];cross=[]
    dtype=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(9,18,3))])
    for s in previous['segments']:
        a=np.frombuffer(raw(HERE.parent/'AtrousCandidate/results/matrix'/s['stream']),dtype=dtype,offset=208)['rgb'].astype(np.float64).mean(0)[-1].astype(np.float32)
        i=SEEDS.index(s['seed']);b=np.array([new[i,p['y'],p['x'],3:] for p in s['points']]);cross.append({'seed':s['seed'],'new18MeansBitwiseEqualToFourSeedStageTrial':np.array_equal(a,b)})
    contract=read(HERE/'review-contract.json');eligible=not hard and not phaseReg and groups['new']['relativeL1Reduction']>=contract['minimumWeightedLocalL1Reduction']
    eligible=eligible and all(all(v for k,v in o.items() if k!='seed') for o in observers+cross) and all(not p['baselineFullGatePass'] or p['candidateFullGatePass'] for p in perSeed)
    eligible=eligible and all(not p['baselineGatePass'] or p['candidateGatePass'] for p in phases) and all(p['candidateFilteredToRawVariance']<contract['noiseScreenFilteredToRawVarianceRatio'] for p in perSeed)
    return {'scope':'Same fixed atrous3 Editor candidate, eight predeclared seeds; old16(15 qualified, one unresolved)+new18, all domains reported. No production acceptance or formal confidence guarantee.',
        'productionChanged':False,'eligibleForDynamicHdrPlayerGpuQualification':bool(eligible),'decision':'Continue qualification before production' if eligible else 'Shelve fixed global atrous3, remove Editor override and keep default5; do not scan2/4',
        'groups':groups,'all34ReferencePoints':points,'hardRegressions':hard,'observerChecks':observers,'crossChecks':cross,'perSeed':perSeed,'phasePassToFailPairs':phaseReg,
        'full':{'candidatePooledGatePass':gate(fullA,fullB),'rgbDifferencePercent':((fullB/fullA-1)*100).tolist(),'candidatePassingSeeds':sum(p['candidateFullGatePass'] for p in perSeed),'phases':phases,'pooledPhaseFailures':sum(not p['candidateGatePass'] for p in phases)}}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'groups':s['groups'],'hardRegressions':s['hardRegressions'],'eligible':s['eligibleForDynamicHdrPlayerGpuQualification'],'full':{k:v for k,v in s['full'].items() if k!='phases'}},indent=2))
