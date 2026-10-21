"""Seed-level uncertainty and predeclared local holdout accounting, no new gate."""
import gzip
import hashlib
import json
import struct
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
SEEDS=[0,104729,1153305,2201881,3250457,4299033,5347609,6396185]
def raw(path): return path.read_bytes() if path.exists() else gzip.decompress(path.with_suffix(path.suffix+'.gz').read_bytes())
def read(path): return json.loads(raw(path).decode('utf-8-sig'))
def vector(v): return np.array([v[c] for c in 'xyz'],dtype=np.float64)
def gate(a,b): return bool(np.all(np.abs(b-a)<np.maximum(.000005,np.abs(a)*.05)))
def stats(a,b):
    # Each row is one independent predeclared seed mean, not one correlated frame/pixel.
    a=np.asarray(a); b=np.asarray(b); n=len(a); mean=a.mean(0); delta=b-a
    se=delta.std(0,ddof=1)/np.sqrt(n)
    return {'raw':mean.tolist(),'filtered':b.mean(0).tolist(),
        'relativeErrorPercent':((b.mean(0)/np.maximum(mean,1e-30)-1)*100).tolist(),
        'pairedDifferenceSeedSe':se.tolist(),'pairedDifferenceSeedSePercent':(se/np.maximum(mean,1e-30)*100).tolist(),
        'pooledOriginalGatePass':gate(mean,b.mean(0))}

def load(label):
    report=read(HERE/f'results/{label}.json'); segments=report['seedAudit']['segments']; maps=[]
    for s in segments:
        value=raw(HERE/'results'/label/s['publicationMap'])
        assert hashlib.sha256(value).hexdigest().upper()==s['publicationMapSha256']
        assert value[:4]==b'HGI1'; w,h,n=struct.unpack('<iii',value[4:16]); assert w==h==256 and n==s['measured']
        assert len(value)==16+w*h*24
        m=np.frombuffer(value,dtype='<f4',offset=16).reshape(h,w,6).astype(np.float64); assert np.isfinite(m).all() and (m>=0).all()
        for record,part in [(s['full'],m)]+[(p,m[p['phaseY']::4,p['phaseX']::4]) for p in s['phases']]:
            assert np.allclose(part[:,:,:3].mean((0,1)),vector(record['raw']),rtol=2e-7,atol=1e-13)
            assert np.allclose(part[:,:,3:].mean((0,1)),vector(record['filtered']),rtol=2e-7,atol=1e-13)
            assert gate(vector(record['raw']),vector(record['filtered']))==record['energyGatePass']
        maps.append(m)
    return report,segments,np.array(maps)

def summarize():
    report,segments,m=load('matrix'); audit=report['seedAudit']
    assert [s['seed'] for s in segments]==SEEDS and len(segments)==8 and audit['testedModes']==['temporal-spatial']
    assert all(s['mode']=='temporal-spatial' and s['warmup']==64 and s['measured']==256 for s in segments)
    a=m[:,:,:,:3]; b=m[:,:,:,3:]; full=stats(a.mean((1,2)),b.mean((1,2)))
    full['passingSeedSegments']=sum(s['full']['energyGatePass'] for s in segments)
    full['phaseFailuresPerSeed']=[sum(not p['energyGatePass'] for p in s['phases']) for s in segments]
    full['rawVarianceMean']=sum(s['full']['rawVariance'] for s in segments)/8
    full['filteredVarianceMean']=sum(s['full']['filteredVariance'] for s in segments)/8
    phases=[]
    for y in range(4):
        for x in range(4): phases.append({'x':x,'y':y,**stats(a[:,y::4,x::4].mean((1,2)),b[:,y::4,x::4].mean((1,2)))})
    tiles=[]
    for y in range(8):
        for x in range(8):
            box=(slice(None),slice(y*32,(y+1)*32),slice(x*32,(x+1)*32))
            tiles.append({'x':x*32,'y':y*32,'size':32,**stats(a[box].mean((1,2)),b[box].mean((1,2)))})
    luminance=np.array([.2126,.7152,.0722]); discovery=(a[:4]-b[:4]).mean(0)@luminance
    selected=np.argsort(-discovery.ravel(),kind='stable')[:12]; holdout=[]
    for at in selected:
        y,x=divmod(int(at),256); v=stats(a[4:,y,x],b[4:,y,x]); delta=(a[4:,y,x]-b[4:,y,x])@luminance
        holdout.append({'x':x,'y':y,'discoveryLuminanceDeficit':float(discovery[y,x]),
            'holdoutLuminanceDeficit':float(delta.mean()),'holdoutLuminanceDeficitSeedSe':float(delta.std(ddof=1)/2),**v})
    return {'protocol':'Current production TS only; original 5%/absolute floor unchanged; empirical seed SE, no iid-frame confidence claim. Fixed 8x8 tiles all reported. Top12 selected on first4 seeds, measured on held-out last4; not absolute truth.',
        'seeds':SEEDS,'full':full,'all16Phases':phases,'all64FixedTiles':tiles,'discoveryTop12Holdout':holdout,
        'referenceLimit':'Legacy 15/16 same-model reference points have post-selection bias; not used as a full-image truth or to select acceptance.'}

if __name__=='__main__':
    result=summarize();(HERE/'analysis.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result['full'],indent=2));print('All16 phase and all64 fixed tile statistics, discovery/holdout locals written without changing acceptance')
