"""Qualify serialized actual Players while preserving every rejected build/run."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def main():
    accepted={};failures=[]
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for prefix in ['build','player']:
            if prefix+'Log' in run:assert digest(HERE/run[prefix+'Log'])==run[prefix+'LogSha256'].lower()
        if run['activeProcess'] is not None:continue
        if not run.get('pass'):failures.append({'run':path.name,'buildDiagnostics':run.get('buildDiagnostics',[]),'playerDiagnostics':run.get('playerDiagnostics',[])});continue
        assert run['buildExit']==run['playerExit']==0 and not run['buildDiagnostics'] and not run['playerDiagnostics']
        assert not run['reviewedExistingCleanupWarnings'],'Final Player needs no GC warning allowance'
        for source in read(HERE/run['sourcesFile']):assert digest(ROOT/source['path'])==source['sha256'].lower(),source['path']
        assert digest(HERE/'HoRTShadowPlayerProbe.cs.txt')==run['probeSha256'].lower()
        assert digest(HERE/'BuildShadowPlayer.cs.txt')==run['buildSourceSha256'].lower()
        report=read(HERE/run['playerReport']);assert report['pass'] and report['updates']==21 and report['mse']<.02 and len(report['checks'])==121
        text=(HERE/run['playerLog']).read_text(encoding='utf-8-sig')
        assert '[HoRTShadowMultiPlayer] retiredDomains=0 retiredResources=0' in text
        assert '[HoRTShadowMultiPlayer] private OB teardown released=4' in text
        assert 'GarbageCollector disposing of GraphicsBuffer' not in text
        assert '[HoRTShadowMultiPlayer] END pass=True' in text
        folder='PlayerRTShadowMulti'+('Compatibility' if run['mode']=='compat' else '')
        plugin=ROOT/'.codex-research/RTValidation'/folder/'HoRTShadowProbe_Data/Plugins/x86_64/HoRTDINRD.dll'
        assert digest(plugin)==digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')==run['packedNativeSha256'].lower()
        accepted[run['mode']]={'run':path.name,'updates':21,'checks':len(report['checks']),'currentPoseMse':report['mse'],'packedNativeSha256':digest(plugin)}
    assert set(accepted)=={'rg','compat'}
    compile_text=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compile_text and not re.search(r'error CS\d+',compile_text)
    review={'stage':'R6 bounded serialized multi-light Win64 Mono Players','accepted':accepted,'rejectedEvidence':failures,
            'benefit':'Four actual current light products are read in each packed Player. Stable LightId and actual reconstruction metadata, finite local domains, independent directional reconstruction, original NPR main alias, skinned alpha/bone updates, no-history export and actual retirement execute together. Both paths have 21 actual Update calls / 121 checks, same current-pose V MSE and no GC allowance.',
            'limits':'64/96-square explicit requests, Mono Win64/D3D12. Missing GB fallback/resolve warning remains bounded to unused fallback writers and disabled MSAA. Directional finite angle and delta point/spot only. Not automatic-camera/backbuffer/XR/IL2CPP or arbitrary production characters. Spatial final-color AA still needs its own acceptance.',
            'next':'Complete final image sampling/AA qualification and requirement-by-requirement R0-R6 audit. Keep full goal active until the audit proves completion.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Multilight Players verified: both modes, 21 Updates / 121 checks, exact native bytes and zero GC warning allowance. Final sampling/audit remain.')
if __name__=='__main__':main()
