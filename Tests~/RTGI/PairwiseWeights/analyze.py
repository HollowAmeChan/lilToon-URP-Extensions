"""Frozen-input audit only: actual geometry/luma consistency and pairwise replay."""
import gzip
import hashlib
import json
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
SOURCE=HERE.parent/'AtrousPropagation'
LUMA=np.array([.2126,.7152,.0722]);CENTER=float(np.float32(.44198)*np.float32(.44198))
DTYPE=np.dtype([('observed','<i4'),('shader','<i4'),('application','<i4'),('v','<f4',(4,27,37,4))])
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def vec(p):return np.array([p[c] for c in 'xyz'],dtype=np.float32).astype(np.float64)
def load():
    report=read(SOURCE/'results/matrix.json')['localStageAudit'];arrays=[]
    for s in report['segments']:
        value=raw(SOURCE/'results/matrix'/s['weightStream']);assert hashlib.sha256(value).hexdigest().upper()==s['weightStreamSha256']
        w=np.frombuffer(value,dtype=DTYPE,offset=20);assert w['observed'].tolist()==[0,32,64,96,128,160,192,224,255];arrays.append(w['v'].astype(np.float64))
    return report,np.array(arrays)
def summarize():
    r,v=load();refs=r['referencePoints'];c=v[:,:,:,:,1];donor=v[:,:,:,:,6::4];factors=v[:,:,:,:,7::4];weights=v[:,:,:,:,5::4,2]
    pre=factors[...,0]*factors[...,1]*factors[...,2];delta=np.abs(donor[...,:3]@LUMA-(c[...,:3]@LUMA)[...,None]);denom=np.maximum(1e-4,2*np.sqrt(np.maximum(c[...,3],0)))[...,None]
    forward=np.exp(-delta/denom);reverse=np.exp(-delta/np.maximum(1e-4,2*np.sqrt(np.maximum(donor[...,3],0))));valid=pre>1e-4
    forwardError=float(np.abs(forward[valid]-factors[...,3][valid]).max());sumW=v[:,:,:,:,3,2]
    reconstructed=(c[...,:3]*CENTER+(donor[...,:3]*weights[...,None]).sum(-2))/sumW[...,None]
    observed=v[:,:,:,:,4,:3];outputError=float(np.abs(reconstructed-observed).max())
    # Fit true raster/primary world coordinates, not the native frustum constants.
    xy=np.array([[p['x'],p['y']] for p in refs]);z=np.array([p['viewDepth'] for p in refs]);world=np.array([vec(p['primaryWorld']) for p in refs]);clip=(xy+.5)/256*2-1
    x=np.stack([np.ones(27),z,z*clip[:,0],-z*clip[:,1]],1);coef=np.linalg.lstsq(x,world,rcond=None)[0];fitError=np.linalg.norm(x@coef-world,axis=1)
    cp=v[:,:,:,:,0,:2];cz=v[:,:,:,:,2,3];normal=v[:,:,:,:,2,:3];sp=v[:,:,:,:,5::4,:2];sz=v[:,:,:,:,8::4,3];inside=v[:,:,:,:,5::4,3]>0
    cxy=(cp+.5)/256*2-1;sxy=(sp+.5)/256*2-1
    cw=coef[0]+cz[...,None]*coef[1]+(cz*cxy[...,0])[...,None]*coef[2]-(cz*cxy[...,1])[...,None]*coef[3]
    sw=coef[0]+sz[...,None]*coef[1]+(sz*sxy[...,0])[...,None]*coef[2]-(sz*sxy[...,1])[...,None]*coef[3]
    distance=np.abs(((sw-cw[...,None,:])*normal[...,None,:]).sum(-1));threshold=cz[...,None]*.003;pred=(distance<threshold)&inside;actual=factors[...,0]>0
    safe=np.abs(distance-threshold)>.0005
    factor=np.array([vec(p['albedo']) for p in refs])*64;truth=np.array([vec(p['referenceMean']) for p in refs]);baseline=observed*factor
    ratio=(donor[...,:3]@LUMA)/np.maximum((c[...,:3]@LUMA)[...,None],1e-30)
    models={'symmetricLumaGeometricMean':np.where(valid,pre*np.sqrt(forward*reverse),0),
        'symmetricExistingFactor2Support':weights*((ratio>=.5)&(ratio<=2))}
    results=[]
    for name,w in models.items():
        out=(c[...,:3]*CENTER+(donor[...,:3]*w[...,None]).sum(-2))/(CENTER+w.sum(-1))[...,None]*factor;groups=[]
        for group,ids in [('new18',list(range(18))),('counterexample',[18])]:
            rows=[]
            for j,step in enumerate([2,4,8,16]):
                t=truth[ids];oldMean=np.take(baseline[:,:,j],ids,axis=2).mean((0,1));newMean=np.take(out[:,:,j],ids,axis=2).mean((0,1))
                oldL1=float(np.abs(oldMean-t).sum()/t.sum());newL1=float(np.abs(newMean-t).sum()/t.sum());perSeed=[]
                for i,seed in enumerate(r['seeds']):
                    a=np.take(baseline[i,:,j],ids,axis=1).mean(0);b=np.take(out[i,:,j],ids,axis=1).mean(0)
                    perSeed.append({'seed':seed,'baselineL1':float(np.abs(a-t).sum()/t.sum()),'replayL1':float(np.abs(b-t).sum()/t.sum())})
                rows.append({'step':step,'baselineL1':oldL1,'replayL1':newL1,'deltaL1':newL1-oldL1,'perSeed':perSeed})
            groups.append({'group':group,'rounds':rows})
        results.append({'model':name,'groups':groups,'all27MeanPhysicalRgbChanges':(out-baseline).mean((0,1)).tolist()})
    return {'scope':'Offline frozen inputs from031; only luminance-factor reversal, not actual reverse geometry/jitter or a full symmetric transition matrix. Each iteration replays its original inputs independently; no recurrence, motion, full-image or GPU quality claim.',
        'productionChanged':False,'newGpuRun':False,'geometry':{'fitRank':int(np.linalg.matrix_rank(x)),'fitCondition':float(np.linalg.cond(x)),'maxFitWorldError':float(fitError.max()),'coefficients':coef.tolist(),'safeComparedPairs':int(safe.sum()),'safeMismatches':int(((pred!=actual)&safe).sum())},
        'reconstruction':{'maxForwardLuminanceFactorError':forwardError,'maxWeightedOutputError':outputError},'models':results,
        'decision':'Luma-only variance symmetrization predicts only tiny late-iteration changes, so no GPU trial for that model. Existing factor2 support separates bright-edge dark donors from the counterexample at steps8/16 in this sample; at most motivates one GI-private late-step symmetric contrast-support trial, never an acceptance claim. Preserve all old controls and original gates.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({'geometry':s['geometry'],'reconstruction':s['reconstruction'],'deltas':[{x['model']:[{g['group']:[r['deltaL1'] for r in g['rounds']]} for g in x['groups']]} for x in s['models']]},indent=2))
