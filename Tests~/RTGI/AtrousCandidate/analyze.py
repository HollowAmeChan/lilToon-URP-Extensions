"""Read actual HGS1 stage streams, preserve full gates and compare fixed MC inputs."""
import gzip
import hashlib
import json
import struct
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
STAGES=[-2,-1,3,4,5,6,9,10,100]
CAPTURES=[3,4,5,6,9,10]
SEEDS=[0,104729,5347609,6396185]
DTYPE=np.dtype([('shader','<i4'),('application','<i4'),('writes','<i4',(6,)),('rgb','<f4',(9,18,3))])
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def gate(a,b):return bool(np.all(np.abs(b-a)<np.maximum(.000005,np.abs(a)*.05)))
def stream(label,s,root=HERE):
    b=raw(root/'results'/label/s['stream']);assert hashlib.sha256(b).hexdigest().upper()==s['streamSha256']
    assert b[:4]==b'HGS1' and struct.unpack('<6i',b[4:28])==(256,256,18,9,s['measured'],s['seed'])
    assert list(struct.unpack('<9i',b[28:64]))==STAGES
    coords=np.frombuffer(b,dtype='<i4',offset=64,count=36).reshape(18,2)
    assert coords.tolist()==[[p['x'],p['y']] for p in s['points']]
    a=np.frombuffer(b,dtype=DTYPE,offset=208);assert len(a)==s['measured'] and np.isfinite(a['rgb']).all()
    return a

def summarize():
    r=read(HERE/'results/matrix.json')['localStageAudit'];baselineRoot=HERE.parent/'LocalStages';br=read(baselineRoot/'results/matrix.json')['localStageAudit']
    reference=read(HERE.parent/'LocalReference/results/reference.json')['denseReference']['points'];truth=np.array([vec(p['referenceMean']) for p in reference]);refSe=np.array([vec(p['batchMeanStandardError']) for p in reference])
    assert r['seeds']==br['seeds']==SEEDS;new=[];old=[];observers=[];perSeed=[];phaseRegression=[]
    for s,b in zip(r['segments'],br['segments']):
        a=stream('matrix',s);o=stream('matrix',b,baselineRoot);new.append(a['rgb'].astype(np.float64));old.append(o['rgb'].astype(np.float64))
        fullBefore=np.array([vec(f['raw']) for f in b['spatial']['frameMeans']]);fullAfter=np.array([vec(f['raw']) for f in s['spatial']['frameMeans']])
        observers.append({'seed':s['seed'],'rawLocalSamplesBitwiseEqual':np.array_equal(a['rgb'][:,0],o['rgb'][:,0]),'rawFullFrameMeansBitwiseEqual':np.array_equal(fullBefore,fullAfter),
            'preFinalStagesBitwiseEqual':np.array_equal(a['rgb'][:,:7],o['rgb'][:,:7])})
        for bp,np_ in zip(b['spatial']['phases'],s['spatial']['phases']):
            if bp['energyGatePass'] and not np_['energyGatePass']:phaseRegression.append([s['seed'],bp['phaseX'],bp['phaseY']])
        perSeed.append({'seed':s['seed'],'baselineFullGatePass':b['spatial']['full']['energyGatePass'],'candidateFullGatePass':s['spatial']['full']['energyGatePass'],
            'baselinePhaseFailures':sum(not p['energyGatePass'] for p in b['spatial']['phases']),'candidatePhaseFailures':sum(not p['energyGatePass'] for p in s['spatial']['phases']),
            'baselineFilteredVariance':b['spatial']['full']['filteredVariance'],'candidateFilteredVariance':s['spatial']['full']['filteredVariance'],
            'filteredVarianceRatioToBaseline':s['spatial']['full']['filteredVariance']/b['spatial']['full']['filteredVariance'],
            'candidateFilteredVarianceToRaw':s['spatial']['full']['filteredVariance']/s['spatial']['full']['rawVariance']})
    new=np.array(new);old=np.array(old);a=old[:,:,-1].mean(1);b=new[:,:,-1].mean(1);ma=a.mean(0);mb=b.mean(0)
    oldL1=float(np.abs(ma-truth).sum()/truth.sum());newL1=float(np.abs(mb-truth).sum()/truth.sum());contract=read(HERE/'review-contract.json')
    points=[];hard=[]
    for i,p in enumerate(reference):
        delta=np.abs(mb[i]-truth[i])-np.abs(ma[i]-truth[i]);perSeedDelta=np.abs(b[:,i]-truth[i])-np.abs(a[:,i]-truth[i]);se=perSeedDelta.std(0,ddof=1)/2;combined=np.sqrt(se*se+refSe[i]*refSe[i]);limit=np.maximum(np.maximum(.000005,truth[i]*.05),2*combined);bad=np.flatnonzero(delta>limit)
        point={'x':p['x'],'y':p['y'],'referenceMean':truth[i].tolist(),'baselineMean':ma[i].tolist(),'candidateMean':mb[i].tolist(),
            'baselineOverReference':(ma[i]/truth[i]).tolist(),'candidateOverReference':(mb[i]/truth[i]).tolist(),
            'absoluteReferenceErrorIncrease':delta.tolist(),'pairedErrorIncreaseEmpiricalSe':se.tolist(),'hardRegressionScreenLimit':limit.tolist(),'hardRegressionChannels':bad.tolist(),
            'baselineTemporalVariance':old[:,:,-1,i].var(1,ddof=1).mean(0).tolist(),'candidateTemporalVariance':new[:,:,-1,i].var(1,ddof=1).mean(0).tolist()}
        points.append(point)
        for c in bad:hard.append({'x':p['x'],'y':p['y'],'channel':int(c),'errorIncrease':float(delta[c]),'limit':float(limit[c])})
    fullA=np.array([vec(s['spatial']['full']['raw']) for s in r['segments']]);fullB=np.array([vec(s['spatial']['full']['filtered']) for s in r['segments']]);phases=[]
    oldFA=np.array([vec(s['spatial']['full']['raw']) for s in br['segments']]);oldFB=np.array([vec(s['spatial']['full']['filtered']) for s in br['segments']])
    for i in range(16):
        q=np.array([vec(s['spatial']['phases'][i]['raw']) for s in r['segments']]).mean(0);v=np.array([vec(s['spatial']['phases'][i]['filtered']) for s in r['segments']]).mean(0)
        oq=np.array([vec(s['spatial']['phases'][i]['raw']) for s in br['segments']]).mean(0);ov=np.array([vec(s['spatial']['phases'][i]['filtered']) for s in br['segments']]).mean(0)
        phases.append({'x':i%4,'y':i//4,'baselineGatePass':gate(oq,ov),'candidateGatePass':gate(q,v),'candidateRgbDifferencePercent':((v/q-1)*100).tolist()})
    fullRegression=any(x['baselineFullGatePass'] and not x['candidateFullGatePass'] for x in perSeed) or (gate(oldFA.mean(0),oldFB.mean(0)) and not gate(fullA.mean(0),fullB.mean(0)))
    pooledPhaseRegression=any(x['baselineGatePass'] and not x['candidateGatePass'] for x in phases)
    eligible=(1-newL1/oldL1)>=contract['minimumWeightedLocalL1Reduction'] and not hard and not phaseRegression and not fullRegression and not pooledPhaseRegression and all(x['candidateFilteredVarianceToRaw']<contract['noiseScreenFilteredToRawVarianceRatio'] for x in perSeed) and all(all(v for k,v in x.items() if k!='seed') for x in observers)
    return {'scope':'One fixed Editor-only atrous5-to3 experiment; four matched seeds and all18 predeclared reference receivers. Screening is not production/full-goal acceptance or a formal statistical confidence claim.',
        'productionChanged':False,'eligibleForBroaderQualification':bool(eligible),'decision':'Continue broader qualification; do not switch production yet' if eligible else 'Reject or shelve this fixed candidate; do not scan iteration2/4',
        'baselineLocalNormalizedL1':oldL1,'candidateLocalNormalizedL1':newL1,'relativeL1Reduction':1-newL1/oldL1,'all18Points':points,'localHardRegressions':hard,
        'observerChecks':observers,'perSeed':perSeed,'phasePassToFailPairs':phaseRegression,'pooledPhaseRegression':pooledPhaseRegression,
        'full':{'baselinePooledGatePass':gate(oldFA.mean(0),oldFB.mean(0)),'candidatePooledGatePass':gate(fullA.mean(0),fullB.mean(0)),
            'candidateRgbDifferencePercent':((fullB.mean(0)/fullA.mean(0)-1)*100).tolist(),'candidatePassingSeeds':sum(x['candidateFullGatePass'] for x in perSeed),
            'candidatePooledPhaseFailures':sum(not x['candidateGatePass'] for x in phases),'phases':phases},
        'meanFilteredVarianceRatioToBaseline':float(np.mean([x['candidateFilteredVariance'] for x in perSeed])/np.mean([x['baselineFilteredVariance'] for x in perSeed]))}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in s.items() if k not in ['all18Points','observerChecks','perSeed','full']},indent=2));print(json.dumps({k:v for k,v in s['full'].items() if k!='phases'},indent=2))
