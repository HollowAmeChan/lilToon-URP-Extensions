"""Verify actual serialized Player runs and unchanged current product bytes."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def main():
    accepted={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        if not run.get('pass') or not run.get('cleanupControl') or run.get('activeProcess') is not None:continue
        try:
            for source in read(HERE/run['sourcesFile']):assert digest(ROOT/source['path'])==source['sha256'].lower()
        except AssertionError:continue
        assert run['buildExit']==run['playerExit']==0 and not run['buildDiagnostics'] and not run['playerDiagnostics'],run
        assert digest(HERE/run['buildLog'])==run['buildLogSha256'].lower()
        assert digest(HERE/run['playerLog'])==run['playerLogSha256'].lower()
        log=(HERE/run['playerLog']).read_text(encoding='utf-8-sig')
        marker='[HoRTDIHostPlayer] PASS' if run['mode'].startswith('DISmoke') else '[HoGIRuntime] END pass=True'
        assert '[HoRTPlayerLifetime] retired=0' in log and marker in log
        assert read(HERE/run['playerResult'])['pass']
        control=run['cleanupControl'];assert digest(ROOT/control['path'])==control['sha256'].lower()
        accepted[run['mode']]={'run':path.name,'result':run['playerResult'],'existingCleanupWarningCount':4}
    assert set(accepted)=={'GI','GICompatibility','DISmoke','DISmokeCompatibility'},'Need both final-source DI bounded and GI Player modes'
    plugin=ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll'
    artifacts={}
    for mode,folder in [('GI','PlayerGIDynamic'),('GICompatibility','PlayerGIDynamicCompatibility'),('DISmoke','PlayerBuild'),('DISmokeCompatibility','PlayerBuildCompatibility')]:
        base=ROOT/'.codex-research/RTValidation'/folder
        exe='HoRTDIProbe' if mode.startswith('DI') else 'HoRTGIProbe'
        packed=base/(exe+'_Data/Plugins/x86_64/HoRTDINRD.dll')
        assert digest(packed)==digest(plugin),'Packed native plugin differs'
        artifacts[mode]={'exeSha256':digest(base/(exe+'.exe')),'packedNativeSha256':digest(packed)}
    controls=[]
    failure='Player mixed diffuse/specular proposal energy mismatch; temporal=False'
    for path in HERE.glob('baseline-DI-*.json'):
        control=read(path);run=read(HERE/control['controlRun'])
        assert control['referenceCommit']=='66a2bb80' and control['sameMixedProposalFailure']
        assert run['buildExit']==0 and run['playerExit']==1 and not run['pass']
        assert digest(HERE/run['playerLog'])==control['playerLogSha256'].lower()
        assert failure in (HERE/run['playerLog']).read_text(encoding='utf-8-sig')
        controls.append(path.name)
    assert controls,'Must retain the pre-migration full DI failure control'
    assert any(not read(p).get('pass') and failure in (HERE/read(p)['playerLog']).read_text(encoding='utf-8-sig')
               for p in HERE.glob('run-DI-*.json') if read(p)['sourceCommit'].startswith('6fbc934b')),'Current full DI failure missing'
    result={'stage':'R3-Player','accepted':accepted,'currentArtifacts':artifacts,
            'fullDiRejectedControls':controls,
            'scope':'Win64 Mono / D3D12, explicit camera requests, serialized Ho-RT with separate DI/GI and actual RELAX. DI bounded scope: original analytic/specular, skinned/cutout tests, two-light RELAX energy/dispatch and host dependency. Full mixed DI proposal oracle fails before and after migration. Editor verifies the DI+GI pair separately.',
            'limits':'Four exact historical shutdown GC warnings are retained against original log SHA. Not a claim of complete pipeline shutdown cleanup or normal enabled-camera/platform/1080 qualification.'}
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('R3 Player verified: four final-source bounded modes; full DI failure retained against pre-migration control; native bytes unchanged.')
if __name__=='__main__':main()
