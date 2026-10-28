"""Validate skinned thin-shadow retention and counterfactual reconstruction bias."""
from pathlib import Path
from array import array
from collections import Counter
import hashlib,json,math,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent;BASE='360a98ea'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def pixels(p,size):
    a=array('f');a.frombytes(p.read_bytes());assert len(a)==size*size*4
    return [a[i:i+4] for i in range(0,len(a),4)]
def main():
    accepted={};rejected=[];metrics={};signals={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None:continue
        if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            assert entry['pass'] and entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if entry['name']=='export-rg':
                report=read(HERE/f"report-export-rg-{path.stem[4:]}.json")
                assert report['pass'] and report['arithmeticMaxError']<1e-6 and report['multiMse']<report['singleMse'] and report['multiRampMse']<report['singleRampMse']
                continue
            if not entry['name'].startswith('thin-'):continue
            report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
            assert report['pass'] and report['estimates']==256 and report['referenceSpp']==1024 and len(report['checks'])==13
            prefix=f"{entry['name']}-{path.stem[4:]}-thin-";signals[entry['name']]={};measured={}
            for pose,key in [('rest','mse'),('bone','movedMse'),('blend','blendMse')]:
                ref=pixels(HERE/(prefix+pose+'-reference-visibility.rgba32'),128);out=pixels(HERE/(prefix+pose+'-sigma-visibility.rgba32'),128)
                owner=Counter(int(p[3]) for p in ref if p[2]==1).most_common(1)[0][0];indices=[i for i,p in enumerate(ref) if p[2]==1 and int(p[3])==owner]
                value=sum((ref[i][0]-out[i][0])**2 for i in indices)/len(indices);assert abs(value-report[key])<1e-7
                assert all(math.isfinite(out[i][0]) and 0<=out[i][0]<=1 for i in indices)
                measured[pose+'Mse']=value
                for suffix in [pose+'-reference-visibility.rgba32',pose+'-sigma-visibility.rgba32',pose+'-reference-beauty.rgba32',pose+'-sigma-beauty.rgba32']:
                    signals[entry['name']][suffix]={'file':prefix+suffix,'sha256':digest(HERE/(prefix+suffix))}
                if pose=='rest':
                    shadows=[i for i in indices if ref[i][0]<.5];retained=sum(out[i][0]<.5 for i in shadows)
                    raw=pixels(HERE/(prefix+'rest-unbounded-visibility.rgba32'),128)
                    unbounded=sum((ref[i][0]-raw[i][0])**2 for i in indices)/len(indices)
                    assert len(shadows)==report['shadowPixels'] and retained==report['retainedPixels'] and retained/len(shadows)>.85
                    assert abs(unbounded-report['unboundedMse'])<1e-7 and value<unbounded
                    assert sum(raw[i][0]<.5 for i in shadows)==report['unboundedRetainedPixels']<retained
                    beauty_ref=pixels(HERE/(prefix+'rest-reference-beauty.rgba32'),128);beauty_out=pixels(HERE/(prefix+'rest-sigma-beauty.rgba32'),128)
                    ramp=sum((beauty_ref[i][0]-beauty_out[i][0])**2 for i in indices)/len(indices);assert abs(ramp-report['rampMse'])<1e-7
                    measured.update(referenceShadowPixels=len(shadows),retained=retained,retention=retained/len(shadows),unboundedMse=unbounded,unboundedRetained=report['unboundedRetainedPixels'],rampMse=ramp)
                    signals[entry['name']]['rest-unbounded-visibility.rgba32']={'file':prefix+'rest-unbounded-visibility.rgba32','sha256':digest(HERE/(prefix+'rest-unbounded-visibility.rgba32'))}
            metrics[entry['name']]=measured
    assert set(accepted)=={'thin-rg','thin-compat','export-rg','gi-host-rg'}
    for key in signals['thin-rg']:assert signals['thin-rg'][key]['sha256']==signals['thin-compat'][key]['sha256']
    assert len(rejected)>=3,'Retain the compile error and rejected 16/64-budget candidates'
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout;cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1;original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compilation=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compilation and not re.search(r'error CS\d+',compilation)
    review={'stage':'R5 bounded rigged thin Cutout quality','accepted':accepted,'rejectedCandidates':rejected,'metrics':metrics,'signals':signals,'unchangedDiGiNrdCharacterShadowFiles':len(paths),
            'benefit':'At identical 256 independent SIGMA estimates, binomial-inspired raw-sample bounds reduce thin-detail reconstruction bias. Bone and BlendShape micro-motion match independently traced current-pose reference, exact pose return reproduces, alpha changes apply immediately and static/moving preview history rules remain.',
            'costAndLimits':'256 full trace/prepare/SIGMA/decode estimates per single-frame request. Sum changes R32F to RG32F (+4 B/pixel versus plain averaging), total 8 B/pixel sum for estimates>1. Default 1 and preview unchanged. Wilson-score-shaped bounds are a detail constraint, not a statistical confidence guarantee for correlated sample sequences. Shadow retention is ~88%, not perfect; 128-square twelve-card fixture is not arbitrary production characters, final subpixel coverage AA, Player or multilight.',
            'rejectedHistory':'Initial loose mechanical gate passed at 16 but visual quality was rejected (833/1341 retained). Stronger >85% gate rejects 16 and 64 bound candidates. One shader compile mistake retained. No old result/hash/threshold was rewritten; final source and matched-budget counterfactual are independently verified.',
            'next':'Player and coverage/character acceptance remain, then R6 requested stable per-light outputs. Keep full goal active.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Thin Cutout quality verified: four GPU cases; saved current-pose MSE, same-budget counterfactual and bit-identical modes. Goal incomplete.')
if __name__=='__main__':main()
