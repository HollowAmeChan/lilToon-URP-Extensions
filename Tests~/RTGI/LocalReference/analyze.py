"""Compare all fixed local receivers against the original same-model MC protocol."""
import gzip
import hashlib
import json
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
SEEDS=HERE.parent/'ProductionSeeds'
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float64)
def load_seeds():
    d=read(SEEDS/'results/matrix.json');maps=[]
    for s in d['seedAudit']['segments']:
        b=raw(SEEDS/'results/matrix'/s['publicationMap']);assert hashlib.sha256(b).hexdigest().upper()==s['publicationMapSha256']
        maps.append(np.frombuffer(b,dtype='<f4',offset=16).reshape(256,256,6).astype(np.float64))
    return d,np.array(maps)
def comparison(a,b,reference):
    a=np.asarray(a);b=np.asarray(b);n=len(a)
    return {'seedCount':n,'rawMean':a.mean(0).tolist(),'filteredMean':b.mean(0).tolist(),
        'rawSeedMeanSe':(a.std(0,ddof=1)/np.sqrt(n)).tolist(),'filteredSeedMeanSe':(b.std(0,ddof=1)/np.sqrt(n)).tolist(),
        'rawOverReference':(a.mean(0)/reference).tolist(),'filteredOverReference':(b.mean(0)/reference).tolist(),
        'rawNormalizedL1':float(np.abs(a.mean(0)-reference).sum()/reference.sum()),
        'filteredNormalizedL1':float(np.abs(b.mean(0)-reference).sum()/reference.sum())}
def summarize():
    d=read(HERE/'results/reference.json');r=d['denseReference'];_,m=load_seeds();points=[]
    expected=[(x,y) for y in [204,206,226,227,252,255] for x in [85,86,87]]
    assert [(p['x'],p['y']) for p in r['points']]==expected
    for p in r['points']:
        x,y=p['x'],p['y'];a=m[:,y,x,:3];b=m[:,y,x,3:];reference=vec(p['referenceMean']);assert np.all(reference>0)
        points.append({'x':x,'y':y,'group':'selected center' if x==86 else 'adjacent control',
            'referenceConverged':p['converged'],'referenceMean':reference.tolist(),'referenceBatchSe':vec(p['batchMeanStandardError']).tolist(),
            'referenceSplitRelativeDifference':vec(p['splitRelativeDifference']).tolist(),'all8Seeds':comparison(a,b,reference),
            'heldOutLast4Seeds':comparison(a[4:],b[4:],reference)})
    groups={}
    for name,xs in [('centers',[86]),('adjacentControls',[85,87]),('allFixedReceivers',[85,86,87])]:
        selected=[p for p in points if p['x'] in xs and p['referenceConverged']]
        ref=np.array([p['referenceMean'] for p in selected]);a=np.array([p['all8Seeds']['rawMean'] for p in selected]);b=np.array([p['all8Seeds']['filteredMean'] for p in selected])
        groups[name]={'qualifiedPoints':len(selected),'rawNormalizedL1':float(np.abs(a-ref).sum()/ref.sum()),'filteredNormalizedL1':float(np.abs(b-ref).sum()/ref.sum()),
            'rawRgbDifferencePercent':((a.sum(0)/ref.sum(0)-1)*100).tolist(),'filteredRgbDifferencePercent':((b.sum(0)/ref.sum(0)-1)*100).tolist()}
    geometry=[]
    for i in range(0,18,3):
        row=r['points'][i:i+3];center=row[1]
        geometry.append({'y':center['y'],'centerDepth':center['viewDepth'],'neighbors':[{
            'x':p['x'],'worldDistance':float(np.linalg.norm(vec(p['primaryWorld'])-vec(center['primaryWorld']))),
            'depthDifference':p['viewDepth']-center['viewDepth'],
            'normalDifference':float(np.linalg.norm(vec(p['normal'])-vec(center['normal']))),
            'albedo':vec(p['albedo']).tolist()} for p in row]})
    guideStats=[]
    for name in ['firstGuides','lastGuides']:
        g=r[name];guideStats.append({'snapshot':name,'batch':g['batch'],'signalScale':g['signalScale'],
            'maxNormalPackError':max(p['normalPackError'] for p in g['points']),
            'maxDecodedNormalError':max(p['decodedNormalError'] for p in g['points']),
            'maxSignalRestorationError':max(p['signalRestorationError'] for p in g['points']),
            'maxViewZError':max(abs(p['viewZ']-q['viewDepth']) for p,q in zip(g['points'],r['points']))})
    return {'scope':'All18 predeclared local points; 026 production TS means compared to new no-reuse same-model reference, not independent absolute shading truth/full-image acceptance. Empirical SE only, no iid-frame confidence claim.',
        'productionChanged':False,'qualityRepaired':False,'allPointsConverged':r['allPointsConverged'],
        'samplesPerPoint':r['samplesPerPoint']*r['batchCount'],'totalReferenceSamples':r['samplesPerPoint']*r['batchCount']*18,
        'groups':groups,'all18Points':points,'geometryTriplets':geometry,'actualMainGuideSnapshots':guideStats,
        'nextDecision':'NRD publication underestimates the converged same-model reference at the selected structure. Actual guide encoding/depth/input restoration checked; next attribute remaining loss to actual stages before choosing a mechanism. No gain/sigma/units/format scan.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'converged':s['allPointsConverged'],'samplesPerPoint':s['samplesPerPoint'],'groups':s['groups'],'guides':s['actualMainGuideSnapshots']},indent=2))
