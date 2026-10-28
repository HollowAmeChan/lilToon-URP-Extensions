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
        report=read(HERE/run['playerReport']);assert report['pass'] and report['updates']==21 and report['mse']<.02 and len(report['checks'])==34
        text=(HERE/run['playerLog']).read_text(encoding='utf-8-sig')
        assert '[HoRTShadowPlayer] retiredDomains=0 retiredResources=0' in text
        assert '[HoRTShadowPlayer] private OB teardown released=4' in text
        assert 'GarbageCollector disposing of GraphicsBuffer' not in text
        assert '[HoRTShadowPlayer] END pass=True' in text
        folder='PlayerRTShadow'+('Compatibility' if run['mode']=='compat' else '')
        plugin=ROOT/'.codex-research/RTValidation'/folder/'HoRTShadowProbe_Data/Plugins/x86_64/HoRTDINRD.dll'
        assert digest(plugin)==digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')==run['packedNativeSha256'].lower()
        accepted[run['mode']]={'run':path.name,'updates':21,'checks':len(report['checks']),'currentPoseMse':report['mse'],'packedNativeSha256':digest(plugin)}
    assert set(accepted)=={'rg','compat'}
    preflight=(HERE/'preflight-single-return.log').read_text(encoding='utf-8-sig')
    assert '[HoRTShadowPlayerPreflight] PASS 8 actual Forward fragment variants' in preflight
    legacy=(HERE/'filter-final.log').read_text(encoding='utf-8-sig')
    assert '[HoRTShadowPlayerFilter] PASS' in legacy and '[Ho-CS Filtering] PASS' in legacy
    assert not re.search(r'^.*(?:Shader warning|Shader error|Exception:).*$',legacy,re.M)
    assert 'levels=18601, maximumAdjacentJump=0.020452, subtexelMotionDelta=0.001452' in (HERE/'filter-metrics.txt').read_text()
    # Preserve original valid-input arithmetic: only initializers, safe sqrt
    # and a single return replace the old early-return control-flow.
    current=(ROOT/'Runtime/CharacterShadow/Shaders/HoCharacterShadowSampling.hlsl').read_text(encoding='utf-8-sig')
    assert 'sqrt(max(pair + 0.5, 0.0)' in current
    assert 'float4 weightsX = 0, weightsY = 0, offsetsX = 0, offsetsY = 0;' in current
    compile_text=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compile_text and not re.search(r'error CS\d+',compile_text)
    review={'stage':'R5 bounded serialized Win64 Mono Players','accepted':accepted,'rejectedEvidence':failures,
            'benefit':'Serialized host/GB/SB/RTShadow, nine SIGMA pipelines and exact native DLL execute actual Cast and original NPR reception in Player. One request per Update checks export reproducibility, continuous V, native skinned Cutout, alpha/bone updates, independent preview history, missing host/receivers, resize/Create and actual retirement.',
            'compilerFix':'Existing CS helper square-root folding and early-return false initialization diagnostics blocked strict build. Nonnegative sqrt, explicit local initialization and single-return equivalent finite-domain branches eliminate warnings. Eight exact Forward variants and original GPU filter validation pass. Failed candidate repairs and rejected builds retained; no new warning whitelist.',
            'cleanup':'Initial probe passed functional checks but eight OB-owned GraphicsBuffer GC warnings caused rejection. Private probe teardown disables its OB declaration, releases its actual table and four placeholders after readback; final logs have zero such warnings and both retirement counts zero. This does not claim production-wide OB application shutdown is fixed.',
            'limits':'64/96-square explicit camera render requests in 21 real Update calls, Mono Win64/D3D12 only. Existing GB fallback/MSAA resolve shaders absent in packed fixture; actual lilToon GB writer used and MSAA disabled. Not qualification of fallback writers, normal enabled-camera/backbuffer/XR/IL2CPP, arbitrary production characters or multilight outputs.',
            'next':'R6 stable per-light requested products and analytic point/spot support. Full objective remains active.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('RTShadow Players verified: both modes, 21 Updates each, actual SIGMA/NPR, exact native bytes and no GC warning allowance. R6 remains.')
if __name__=='__main__':main()
