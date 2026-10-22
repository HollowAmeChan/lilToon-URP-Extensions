"""Current stock/no-op reproducibility, with original failures kept separate."""
import gzip
import hashlib
import json
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def report(label):return read(HERE/f'results/{label}.json')['localStageAudit']
def summarize():
    a=report('stock-a');b=report('stock-b');c=report('noop');baseline=read(HERE.parent/'ProductionSeeds/results/matrix.json')['seedAudit'];comparisons=[]
    for i,s in enumerate(a['segments']):
        before=next(v for v in baseline['segments'] if v['seed']==s['seed']);reference=raw(HERE.parent/'ProductionSeeds/results/matrix'/before['publicationMap']);rows=[]
        for label,r in [('stock-a',a),('stock-b',b),('noop',c)]:
            t=r['segments'][i];value=raw(HERE/'results'/label/t['publicationMap']);array=np.frombuffer(value,dtype='<f4',offset=16).reshape(256,256,6);old=np.frombuffer(reference,dtype='<f4',offset=16).reshape(256,256,6)
            frames=np.array([vec(f['raw']) for f in t['spatial']['frameMeans']]);oldFrames=np.array([vec(f['raw']) for f in before['frameMeans']]);states=[]
            for name in ['firstInputs','lastInputs']:
                x=s[name];y=t[name];states.append({'endpoint':name,'lightsEqual':x['lights']==y['lights'],'skyEqual':all(x[k]==y[k] for k in ['skyParameters','skyTint','skyConstant']),
                    'risEqual':x['ris']==y['ris'],'selectedPrimaryEqual':x['selectedPrimary']==y['selectedPrimary'],'selectedSecondaryEqual':x['selectedSecondary']==y['selectedSecondary'],
                    'fullHashesEqual':all(x[k]==y[k] for k in ['lightsSha256','risSha256','secondarySha256','primarySha256'])})
            rows.append({'label':label,'entireMeanMapEqualsOldBaseline':value==reference,'rawMeanMapEqualsOld':np.array_equal(array[:,:,:3],old[:,:,:3]),'rawFrameMeansEqualOld':np.array_equal(frames,oldFrames),'endpointStates':states})
        comparisons.append({'seed':s['seed'],'runs':rows})
    initial=read(HERE/'initial-numeric-omission/stock-a/report.json')['localStageAudit'];diffs=[]
    for x,y in zip(initial['segments'],a['segments']):
        row={'seed':x['seed'],'endpoints':[]}
        for name in ['firstInputs','lastInputs']:
            p=x[name];q=y[name];changes={};maxHit=0.;maxMiss=0.;maxNormal=0;maxRadius=0.;pairs=0
            for i,(u,v) in enumerate(zip(p['selectedSecondary'],q['selectedSecondary'])):
                for key in u:
                    if u[key]!=v[key]:changes[key]=changes.get(key,0)+1
                delta=float(np.linalg.norm(vec(u['position'])-vec(v['position'])));n=max(abs((u['normal']&65535)-(v['normal']&65535)),abs((u['normal']>>16)-(v['normal']>>16)));maxNormal=max(maxNormal,n)
                if u['flags']==1:maxHit=max(maxHit,delta)
                else:
                    origin=vec(p['selectedPrimaryProbe'][i*6+1]);d1=vec(u['position'])-origin;d2=vec(v['position'])-origin
                    maxMiss=max(maxMiss,float(np.linalg.norm(d1/np.linalg.norm(d1)-d2/np.linalg.norm(d2))));maxRadius=max(maxRadius,abs(np.linalg.norm(d1)-100000),abs(np.linalg.norm(d2)-100000))
                pairs+=1
            row['endpoints'].append({'endpoint':name,'lightsSkyRisPrimaryHashesEqual':all(p[k]==q[k] for k in ['lightsSha256','risSha256','primarySha256']) and all(p[k]==q[k] for k in ['skyParameters','skyTint','skyConstant']),
                'secondaryWholeHashEqual':p['secondarySha256']==q['secondarySha256'],'selectedDifferingFieldCounts':changes,'selectedPairs':pairs,
                'maxHitPositionError':maxHit,'maxMissUnitDirectionError':maxMiss,'maxMissRadiusError':maxRadius,'maxNormalOct16Steps':maxNormal,
                'withinOriginal019EndpointPrecision':maxHit<=.0005 and maxMiss<=1e-6 and maxRadius<=8*1.192092896e-7*100000 and maxNormal<=1 and all(k in ['position','normal'] for k in changes)})
        diffs.append(row)
    return {'scope':'Current stock A/B and literal-math no-op shader10 clone. Endpoint snapshots are first/last only; hashes and selected fields do not identify every input at every frame. Host recovery and historical mismatches are not erased.',
        'productionChanged':False,'currentStrictReproducibilityPassed':all(all(v for k,v in r.items() if k not in ['label','endpointStates']) and all(all(v for k,v in e.items() if k!='endpoint') for e in r['endpointStates']) for s in comparisons for r in s['runs']),
        'comparisons':comparisons,'historicalInitialStockComparison':diffs,
        'historicalRawMismatchCause':'Unproven. Initial stock run also differed, while current A/B/no-op are exact. Selected secondary position/normal differences correlate with the initial cohort but do not prove the cause of raw divergence or device failure.',
        'decision':'Use fresh stock pairing and per-field input checks for future work. Historical034 remains unqualified; do not reaccept its nominal result. Never auto-queue after failed native/Python/scene qualification.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({'currentStrictReproducibilityPassed':s['currentStrictReproducibilityPassed'],'historicalSelectedWithinOldPrecision':all(e['withinOriginal019EndpointPrecision'] for r in s['historicalInitialStockComparison'] for e in r['endpoints'])},indent=2))
