"""Review real Cast SIGMA with saved float signals, not just a PASS string."""
from pathlib import Path
from array import array
from collections import Counter
import hashlib,json,math,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent;BASE='a6731f12'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def pixels(p):
    result=array('f');result.frombytes(p.read_bytes());assert len(result)==64*64*4
    return [tuple(result[i:i+4]) for i in range(0,len(result),4)]
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
            if not entry['name'].startswith('shadow-sigma-'):continue
            report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
            assert report['pass'] and report['sigma'] and report['rawOnly'] and report['requests']==34
            for required in ['No-history production output repeats without waiting for temporal convergence','Static preview actually retains temporal frames','Different cameras own separate SIGMA resources/history','NPR back-lit domain preserves actual raw visibility outside SIGMA support','SIGMA consumer resources retire after actual completion']:
                assert required in report['checks'],required
            prefix=f"{entry['name']}-{path.stem[4:]}-"
            data={name:pixels(HERE/(prefix+name+'.rgba32')) for name in ['reference','raw','sigma']}
            owner=Counter(int(p[3]) for p in data['raw'] if p[2]==1).most_common(1)[0][0]
            count=0;raw_error=0;filtered_error=0;levels=set()
            for ref,raw,filtered in zip(data['reference'],data['raw'],data['sigma']):
                assert raw[1:]==filtered[1:],'Decoder changed surface/status/distance payload'
                if raw[2]!=1:continue
                assert raw[0] in (0,1) and math.isfinite(filtered[0]) and 0<=filtered[0]<=1
                if 0<filtered[0]<1:levels.add(filtered[0])
                if int(raw[3])!=owner:continue
                raw_error+=(raw[0]-ref[0])**2;filtered_error+=(filtered[0]-ref[0])**2;count+=1
            a,b=raw_error/count,filtered_error/count
            assert b<a and abs(a-report['rawMse'])<1e-7 and abs(b-report['filteredMse'])<1e-7
            assert len(levels)==report['distinctLevels'] and len(levels)>8
            metrics[entry['name']]={'requests':report['requests'],'checks':len(report['checks']),'referenceSamples':4096,'rawMse':a,'filteredMse':b,'levels':len(levels),'receiverPixels':count}
            signals[entry['name']]={name:{'float':prefix+name+'.rgba32','sha256':digest(HERE/(prefix+name+'.rgba32')),'preview':prefix+name+'.png'} for name in data}
    assert set(accepted)=={'shadow-sigma-rg','shadow-sigma-compat','shadow-raw-rg','shadow-raw-compat','sigma-independent'}
    for name in ['reference','raw','sigma']:assert signals['shadow-sigma-rg'][name]['sha256']==signals['shadow-sigma-compat'][name]['sha256'],'RG/compat signal mismatch'
    unchanged_paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in unchanged_paths).encode(),stdout=subprocess.PIPE,check=True).stdout
    cursor=0
    for name in unchanged_paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1
        original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compile_text=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compile_text and not re.search(r'error CS\d+',compile_text)
    review={'stage':'R5 bounded real Cast integration; full goal incomplete','accepted':accepted,'metrics':metrics,'signals':signals,'referenceCommit':BASE,'unchangedDiGiNrdCharacterShadowFiles':len(unchanged_paths),
            'benefit':'Real single-sample closest blocker goes through official SIGMA frontend, primary guides and square decoder. Published full-resolution float visibility preserves raw status/owner/distance. Static preview has independent per-view/light history; export is reset and reproducible without stabilization waits.',
            'costAndLimits':'64x64 two-part static geometry with controlled changes. Added SIGMA external textures are 40 B/pixel plus the existing 16 B/pixel raw, SDK pools and constants. Camera/scene/material changes reset instead of claiming object-motion reprojection. NPR back-lit domain explicitly uses its raw visibility. Not character hair/rig, coplanar owner-edge, final ramp, Player or multilight quality acceptance.',
            'next':'Resolve actual NPR main Cast reception with authoritative shader layout/parameter evidence, then qualify ramp quality and extend requested light outputs. R4 material gate and R6 remain open.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('R5 real Cast verified: 5 cases, independent saved-signal MSE and bit-identical RG/compat; NPR/full scope still pending.')
if __name__=='__main__':main()
