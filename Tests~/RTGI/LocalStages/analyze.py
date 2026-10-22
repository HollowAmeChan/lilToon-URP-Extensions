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
def stream(label,s):
    b=raw(HERE/'results'/label/s['stream']);assert hashlib.sha256(b).hexdigest().upper()==s['streamSha256']
    assert b[:4]==b'HGS1' and struct.unpack('<6i',b[4:28])==(256,256,18,9,s['measured'],s['seed'])
    assert list(struct.unpack('<9i',b[28:64]))==STAGES
    coords=np.frombuffer(b,dtype='<i4',offset=64,count=36).reshape(18,2)
    assert coords.tolist()==[[p['x'],p['y']] for p in s['points']]
    a=np.frombuffer(b,dtype=DTYPE,offset=208);assert len(a)==s['measured'] and np.isfinite(a['rgb']).all()
    return a
def summarize():
    r=read(HERE/'results/matrix.json')['localStageAudit'];assert r['seeds']==SEEDS and r['stageIds']==STAGES
    reference=read(HERE.parent/'LocalReference/results/reference.json')['denseReference']['points']
    truth=np.array([vec(p['referenceMean']) for p in reference]);old=read(HERE.parent/'ProductionSeeds/results/matrix.json')['seedAudit']['segments']
    data=[];perSeed=[];observers=[]
    for s in r['segments']:
        a=stream('matrix',s);m=a['rgb'].astype(np.float64).mean(0);data.append(m)
        previous=next(p for p in old if p['seed']==s['seed'])
        fullBefore=np.array([vec(f['raw']) for f in previous['frameMeans']]);fullAfter=np.array([vec(f['raw']) for f in s['spatial']['frameMeans']])
        beforeFiltered=np.array([vec(f['filtered']) for f in previous['frameMeans']]);afterFiltered=np.array([vec(f['filtered']) for f in s['spatial']['frameMeans']])
        saved=np.frombuffer(raw(HERE.parent/'ProductionSeeds/results/matrix'/previous['publicationMap']),dtype='<f4',offset=16).reshape(256,256,6)
        coords=[(p['x'],p['y']) for p in s['points']]
        rawPoints=np.array([saved[y,x,:3] for x,y in coords]);filteredPoints=np.array([saved[y,x,3:] for x,y in coords])
        observers.append({'seed':s['seed'],'fullRawFrameMeansBitwiseEqual':np.array_equal(fullBefore,fullAfter),
            'fullFilteredFrameMeansBitwiseEqual':np.array_equal(beforeFiltered,afterFiltered),
            'localRawMeansBitwiseEqual':np.array_equal(m[0].astype(np.float32),rawPoints),
            'localFilteredMeansBitwiseEqual':np.array_equal(m[-1].astype(np.float32),filteredPoints)})
        perSeed.append({'seed':s['seed'],'fullOriginalGatePass':s['spatial']['full']['energyGatePass'],
            'phaseFailures':sum(not p['energyGatePass'] for p in s['spatial']['phases']),
            'stageNormalizedL1':[float(np.abs(v-truth).sum()/truth.sum()) for v in m]})
    data=np.array(data);pooled=data.mean(0);stages=[]
    for i,id in enumerate(STAGES):
        values=data[:,i];mean=pooled[i]
        stages.append({'pipeline':id,'meaning':r['segments'][0]['points'][0]['stages'][i]['meaning'],
            'normalizedL1':float(np.abs(mean-truth).sum()/truth.sum()),
            'rgbDifferencePercent':((mean.sum(0)/truth.sum(0)-1)*100).tolist(),
            'localMeanSeedSe':(values.std(0,ddof=1)/2).tolist(),
            'all18Means':mean.tolist()})
    groups={}
    for name,xs in [('centers',[86]),('adjacentControls',[85,87]),('allFixedReceivers',[85,86,87])]:
        ids=[i for i,p in enumerate(reference) if p['x'] in xs];q=truth[ids]
        groups[name]={'points':len(ids),'stageNormalizedL1':[float(np.abs(v[ids]-q).sum()/q.sum()) for v in pooled],
            'stageRgbDifferencePercent':[((v[ids].sum(0)/q.sum(0)-1)*100).tolist() for v in pooled]}
    fullA=np.array([vec(s['spatial']['full']['raw']) for s in r['segments']]);fullB=np.array([vec(s['spatial']['full']['filtered']) for s in r['segments']]);phases=[]
    for i in range(16):
        a=np.array([vec(s['spatial']['phases'][i]['raw']) for s in r['segments']]).mean(0);b=np.array([vec(s['spatial']['phases'][i]['filtered']) for s in r['segments']]).mean(0)
        phases.append({'x':i%4,'y':i//4,'originalGatePass':gate(a,b),'rgbDifferencePercent':((b/a-1)*100).tolist()})
    return {'scope':'Current production, four predeclared seeds; all18 fixed receivers against qualified same-model MC. Not full-image truth or new product timing. Empirical seed statistics only.',
        'productionChanged':False,'qualityRepaired':False,'seeds':SEEDS,'stageIds':STAGES,
        'observerChecks':observers,'perSeed':perSeed,'allStages':stages,'groups':groups,
        'full':{'originalPooledGatePass':gate(fullA.mean(0),fullB.mean(0)),'rgbDifferencePercent':((fullB.mean(0)/fullA.mean(0)-1)*100).tolist(),
            'passingSeedSegments':sum(s['spatial']['full']['energyGatePass'] for s in r['segments']),'phases':phases},
        'decision':'Largest local-reference error increase is after first Atrous through the remaining four Atrous dispatches. Next bounded Editor candidate: retain first3 iterations, remove last2 widest steps; verify local/full/phase quality and noise before any production choice. No gain/sigma/unit/format changes.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'observer':s['observerChecks'],'stages':[{'id':x['pipeline'],'L1':x['normalizedL1']} for x in s['allStages']],'full':{k:v for k,v in s['full'].items() if k!='phases'}},indent=2))
