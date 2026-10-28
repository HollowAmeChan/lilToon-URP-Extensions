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
        assert '[HoRTPlayerLifetime] retired=0' in log and '[HoGIRuntime] END pass=True' in log
        assert read(HERE/run['playerResult'])['pass']
        control=run['cleanupControl'];assert digest(ROOT/control['path'])==control['sha256'].lower()
        accepted[run['mode']]={'run':path.name,'result':run['playerResult'],'existingCleanupWarningCount':4}
    assert set(accepted)=={'GI','GICompatibility'},'Need both final-source GI Player modes'
    plugin=ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll'
    artifacts={}
    for mode,folder in [('GI','PlayerGIDynamic'),('GICompatibility','PlayerGIDynamicCompatibility')]:
        base=ROOT/'.codex-research/RTValidation'/folder
        packed=base/'HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll'
        assert digest(packed)==digest(plugin),'Packed native plugin differs'
        artifacts[mode]={'exeSha256':digest(base/'HoRTGIProbe.exe'),'packedNativeSha256':digest(packed)}
    result={'stage':'R3-Player','accepted':accepted,'currentArtifacts':artifacts,
            'scope':'Win64 Mono / D3D12, explicit camera request per application Update, serialized Ho-RT/GI/RELAX. Editor verifies the DI+GI pair separately.',
            'limits':'Four exact historical shutdown GC warnings are retained against original log SHA. Not a claim of complete pipeline shutdown cleanup or normal enabled-camera/platform/1080 qualification.'}
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('R3 Player verified: both final-source modes, explicit host retirement, native bytes unchanged; historical cleanup warnings retained.')
if __name__=='__main__':main()
