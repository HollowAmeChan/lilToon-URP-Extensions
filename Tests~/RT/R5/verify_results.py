"""Verify independent SIGMA GPU/ABI evidence and the preserved RELAX path."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent;BASE='93312fda'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def before(p):return subprocess.check_output(['git','show',f'{BASE}:{p}'],cwd=ROOT).decode('utf-8-sig').replace('\r\n','\n')
def main():
    accepted={};rejected=[]
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
            if entry['name']=='sigma-independent':
                result=read(HERE/f"report-sigma-independent-{path.stem[4:]}.json")
                assert result['pass'] and result['dispatches']>0 and result['levels']>8
                assert result['decodeError']==0 and result['filteredMse']<result['rawMse']
                assert 'Actual dispatch list includes stabilization exactly when requested' in result['checks']
                assert 'Wrong settings family is rejected before entering native ABI' in result['checks']
    assert set(accepted)=={'sigma-independent','di-relax-rg','di-relax-compat','gi-relax-rg','gi-relax-compat'}
    generation=read(HERE/'generation.json')
    assert generation['version']==[4,17,3] and len(generation['identifiers'])==9
    assert digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')==generation['nativeDllSha256']
    for source in generation['imported']:assert digest(ROOT/source['path'])==source['sha256']==source['sourceSha256']
    for name,hashes in generation['matchingSdkSources'].items():
        assert digest(ROOT/'Runtime/RTDI/ThirdParty/NRD'/name)==hashes['pinned']==hashes['source']
    # Added formats and typed settings do not change the old dispatch executor.
    file='Runtime/NRD/HoNrdContext.cs';old=before(file);current=(ROOT/file).read_text(encoding='utf-8-sig').replace('\r\n','\n')
    marker='            HoNrdNative.Check(HoNrdNative.GetComputeDispatches('
    assert old[old.index(marker):]==current[current.index(marker):],'Shared dispatch/resource/constant execution body changed'
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI'],cwd=ROOT).decode().splitlines()
    data=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout
    cursor=0
    for name in paths:
        end=data.index(b'\n',cursor);size=int(data[cursor:end].split()[2]);cursor=end+1
        original=data[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compilation=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compilation and not re.search(r'error CS\d+',compilation)
    review={'stage':'R5 independent SIGMA prerequisite; production and NPR still incomplete','accepted':accepted,'retainedFailures':rejected,
            'metrics':{k:result[k] for k in ['dispatches','levels','rawMse','filteredMse','decodeError']},'unchangedDiGiFiles':len(paths),
            'sourceAudit':'Native DLL and existing pinned SDK/RELAX sources unchanged; 14 original SIGMA source files imported with exact hashes. Nine identifiers/resources generated from actual instance descriptors and original conditional resource headers. Shared GPU dispatch execution body unchanged.',
            'benefit':'Real SIGMA passes produce officially decoded continuous visibility; exact 20-byte settings, R32_UINT/RG8_UNORM pool support and rejection of wrong settings family before native ABI. Actual stabilization pipeline exists only in requested preview frames; fixed-seed reset with maxStabilizedFrameNum=0 repeats exactly.',
            'costAndLimits':'Independent 128x64 static-plane synthetic stochastic frontend only. It proves integration/encoding/dispatch behavior and a narrow MSE improvement, not character quality, NPR ramps, moving-surface reprojection, feature integration, unsupported APIs, Player or multilight outputs. Existing RELAX two-mode DI/GI regression passes; no frame-time claim.',
            'next':'Connect real RTShadow single-sample nearest blocker, primary guides and official SIGMA frontend/decoder, with per-camera/light state and reset rules. R4 actual NPR reception failure remains open.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('R5 independent SIGMA verified: 5 GPU cases, typed ABI and actual schedule; production/NPR still pending.')
if __name__=='__main__':main()
