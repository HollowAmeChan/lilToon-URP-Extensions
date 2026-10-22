"""Same actual inputs/common/settings, independent native NRD instances."""
import gzip
import hashlib
import json
from pathlib import Path
import numpy as np

HERE=Path(__file__).resolve().parent
def raw(p):return p.read_bytes() if p.exists() else gzip.decompress(p.with_suffix(p.suffix+'.gz').read_bytes())
def read(p):return json.loads(raw(p).decode('utf-8-sig'))
def summarize():
    small=read(HERE/'results/small.json')['shared'];d=read(HERE/'results/matrix.json');r=d['localStageAudit'];shared=r['shared'];old=read(HERE.parent/'ProductionSeeds/results/matrix.json')['seedAudit'];observers=[]
    for s in r['segments']:
        b=next(t for t in old['segments'] if t['seed']==s['seed']);current=raw(HERE/'results/matrix'/s['publicationMap']);before=raw(HERE.parent/'ProductionSeeds/results/matrix'/b['publicationMap']);a=np.array([[v['raw'][c] for c in 'xyz'] for v in s['spatial']['frameMeans']]);o=np.array([[v['raw'][c] for c in 'xyz'] for v in b['frameMeans']]);observers.append({'seed':s['seed'],'wholeRawFilteredMeanMapBitwiseEqual':current==before,'rawFrameMeansBitwiseEqual':np.array_equal(a,o)})
    return {'scope':'Two independent RELAX_DIFFUSE cores consume the exact source input identifiers and value-copied common/settings before the ordinary baseline Record; only writable external outputs and private pools/constants differ. Diagnostic-only no-op/equality qualification, not quality or product GPU-budget improvement.',
        'productionBehaviorChanged':False,'small':{'frames':len(small['frames']),'baselineVsBranch0AllBitwiseEqual':all(f['mismatches0']==0 for f in small['frames']),
            'branch1EqualBeforeOwnReset':all(f['mismatches1']==0 for f in small['frames'][:24]),'minimumMismatchAfterOwnReset':min(f['mismatches1'] for f in small['frames'][24:48]),
            'branch1EqualAfterUnifiedReset':all(f['mismatches1']==0 for f in small['frames'][48:]),'restoredHook':small['restoredHook'],'released':small['released']},
        'scene':{'recordedIncludingWarmup':shared['recorded'],'comparedFrames':len(shared['frames']),'rgbaComponentsPerFrame':256*256*4,
            'baselineVsBranch0Mismatches':sum(f['mismatches0'] for f in shared['frames']),'baselineVsNoopBranch1Mismatches':sum(f['mismatches1'] for f in shared['frames']),
            'independentPoolsAndConstants':shared['independentPools'],'sharedInputBindings':shared['sharedInputBindings'],'restoredHook':shared['restoredHook'],'released':shared['released']},
        'observerChecks':observers,'originalFailuresPreserved':'First empty-scene attempt failed before any NRD frame because URP RTHandle pool was uninitialized; wrapper then failed before saving actual child exit. Actual wrapper1/child unknown kept separately, not inferred from source.',
        'next':'Read DI DiffuseExchange mature variance/retained-kernel policy against GI source; test only one scoped private reconstruction change on branch1 after all matched-input invariants remain, with original/new reference controls, full/phase/noise and later qualification. Do not automatically adopt DI presets.'}

if __name__=='__main__':
    s=summarize();(HERE/'analysis.json').write_text(json.dumps(s,indent=2)+'\n',encoding='utf-8');print(json.dumps({'small':s['small'],'scene':s['scene'],'observerChecks':s['observerChecks']},indent=2))
