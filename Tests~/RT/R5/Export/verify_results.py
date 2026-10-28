"""Recompute no-history quality from saved linear visibility and actual ramp colors."""
from pathlib import Path
from array import array
from collections import Counter
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent;BASE='e84577c1'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def pixels(p):
    a=array('f');a.frombytes(p.read_bytes());assert len(a)==64*64*4
    return [a[i:i+4] for i in range(0,len(a),4)]
def main():
    accepted={};metrics={};signals={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
        if run['activeProcess'] is not None:continue
        if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            assert entry['pass'] and entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if not entry['name'].startswith('export-'):continue
            report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
            assert report['pass'] and report['samples']==16 and report['arithmeticMaxError']==0
            assert len(report['checks'])==11
            prefix=f"{entry['name']}-{path.stem[4:]}-export-"
            signals[entry['name']]={}
            data={}
            for name in ['reference','single','multi']:
                data[name]={}
                for channel in ['visibility','beauty']:
                    filename=prefix+name+'-'+channel+'.rgba32';data[name][channel]=pixels(HERE/filename)
                    signals[entry['name']][name+'-'+channel]={'file':filename,'sha256':digest(HERE/filename)}
            owner=Counter(int(p[3]) for p in data['reference']['visibility'] if p[2]==1).most_common(1)[0][0]
            measured={}
            for channel in ['visibility','beauty']:
                indices=[i for i,p in enumerate(data['reference']['visibility']) if p[2]==1 and int(p[3])==owner]
                for name in ['single','multi']:
                    value=sum((data[name][channel][i][0]-data['reference'][channel][i][0])**2 for i in indices)/len(indices)
                    key=name+('RampMse' if channel=='beauty' else 'Mse');measured[key]=value
                    assert abs(value-report[key])<1e-7,(key,value,report[key])
                assert measured['multi'+('RampMse' if channel=='beauty' else 'Mse')]<measured['single'+('RampMse' if channel=='beauty' else 'Mse')]
            metrics[entry['name']]=measured
    assert set(accepted)=={'export-rg','export-compat','npr-rg','npr-compat','shadow-sigma-rg','shadow-raw-rg'},accepted
    for key in signals['export-rg']:assert signals['export-rg'][key]['sha256']==signals['export-compat'][key]['sha256']
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout;cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1;original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compile_text=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compile_text and not re.search(r'error CS\d+',compile_text)
    review={'stage':'R5 same-request no-history quality','accepted':accepted,'metrics':metrics,'signals':signals,'referenceCommit':BASE,'unchangedDiGiNrdCharacterShadowFiles':len(paths),
            'benefit':'Each independent real single light sample is reconstructed by original SIGMA, officially decoded and averaged as linear visibility before NPR ramp. Four-estimate same-request result equals separately reconstructed samples exactly. 16 estimates improve both V and actual 0.01-width ramp against 4096-spp reference; RG/compat signals match exactly.',
            'costAndLimits':'1-64 full trace/prepare/SIGMA/decode executions in one request; linear GPU/CPU scheduling cost. One 32F sum adds 4 B/pixel only when estimates>1. Default 1; static preview always one estimate. Raw product retains final single sample and nearest distance, not an unbiased multi-sample raw product. Bias from SIGMA filtering is not eliminated by averaging. This 64x64 two-plane test does not qualify thin character cards, animation, coverage AA, Player or multilight.',
            'next':'Qualify thin/skinned/cutout geometry and micro-motion against per-request reference, then Player and R6. Full goal stays active.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Same-request export verified: six GPU cases, exact arithmetic, saved V/ramp MSE and identical two-mode signals; full goal active.')
if __name__=='__main__':main()
