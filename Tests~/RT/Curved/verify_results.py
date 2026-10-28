"""Qualify true-plane origin guards and explicit smooth-geometry terminator policy."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent;BASE='c4840135'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def current(folder,run):return all(digest(ROOT/s['path'])==s['sha256'].lower() for s in read(folder/run['sourcesFile']))

def main():
    accepted={};rejected=[];signals={};curved={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None or not current(HERE,run):continue
        for entry in run['entries']:
            if not entry['pass']:continue
            assert entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            name=entry['name'];stamp=path.stem[4:];accepted[name]=path.name
            if name.startswith('curved-'):
                report=read(HERE/f'{name}-{stamp}-curved-report.json')
                assert report['pass'] and report['valid']==78152 and report['falseShadows']==0
                assert report['contactBlocked']==report['contactOccluded']==8448 and report['contactVisible']==0
                assert 0<report['contactMinimum']<=report['contactMaximum']<.002
                assert report['terminatorSamples']==report['geometricBlocked']==3936 and report['correctedBlocked']==0
                curved[name]=report
                for kind in ['ortho','perspective','contact','cylinder-geometry','cylinder-corrected']:
                    signal=HERE/f'{name}-{stamp}-curved-{kind}.rgba32';assert signal.stat().st_size==128*128*16;signals[(name,kind)]=digest(signal)
            elif name.startswith('self-'):
                report=read(HERE/f'{name}-{stamp}-self-report.json');assert report['pass'] and report['falseShadowPixels']==report['jitterFlips']==report['skinnedFalse']==0 and report['contactBlocked']==8448
            elif name.startswith('native-query-'):
                report=read(HERE/f'report-{name}-{stamp}.json');assert report['pass'] and report['requests']==13 and len(report['checks'])==122
            elif name.startswith('multi-'):
                report=read(HERE/f'report-{name}-{stamp}.json');assert report['pass'] and report['checks']==54
            elif name=='export-rg':
                report=read(HERE/f'report-{name}-{stamp}.json');assert report['pass'] and report['multiMse']<report['singleMse'] and report['multiRampMse']<report['singleRampMse'] and report['arithmeticMaxError']<1e-6
            elif name=='thin-rg':
                report=read(HERE/f'report-{name}-{stamp}.json');assert report['pass'] and report['retainedPixels']>report['shadowPixels']*.85
    assert set(accepted)=={'curved-rg','curved-compat','self-rg','self-compat','native-query-rg','native-query-compat','multi-rg','multi-compat','export-rg','thin-rg'}
    assert len(rejected)==3
    assert any(read(p).get('falseShadows')==32516 for p in HERE.glob('*curved-report.json')),'Retain previous production failure'
    for kind in ['ortho','perspective','contact','cylinder-geometry','cylinder-corrected']:assert signals[('curved-rg',kind)]==signals[('curved-compat',kind)]
    players={};folder=HERE/'Player'
    for path in sorted(folder.glob('run-*.json')):
        run=read(path)
        if not run.get('pass') or run['activeProcess'] is not None or not current(folder,run):continue
        assert run['buildExit']==run['playerExit']==0 and not run['buildDiagnostics'] and not run['playerDiagnostics'] and not run['reviewedExistingCleanupWarnings']
        for prefix in ['build','player']:assert digest(folder/run[prefix+'Log'])==run[prefix+'LogSha256'].lower()
        assert run['probeSha256'].lower()==digest(folder/'HoRTShadowPlayerProbe.cs.txt') and run['buildSourceSha256'].lower()==digest(folder/'BuildShadowPlayer.cs.txt')
        assert run['packedNativeSha256'].lower()==digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')
        report=read(folder/run['playerReport']);assert report['pass'] and report['updates']==21 and len(report['checks'])==165
        assert sum('no spurious strips' in text for text in report['checks'])==8
        assert any('public product reports actual geometry policy' in text for text in report['checks'])
        players[run['mode']]=path.name
    assert set(players)=={'rg','compat'}
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    paths+=['Runtime/RT/HoRTSkinnedMesh.cs','Runtime/RT/HoRTGeometryCache.cs','Runtime/RT/HoRTRendererFeature.cs','Runtime/RT/Shaders/HoRTNativeVisibility.hlsl']
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout;cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1;original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compiled=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compiled and not re.search(r'error CS\d+',compiled)
    review={'stage':'Smooth-normal strips and polygon shadow-terminator geometry correction','referenceCommit':BASE,'accepted':accepted,'curvedReports':curved,'serializedPlayers':players,'retainedFailures':rejected,'unchangedDiGiNrdCharacterShadowAndHostPoseFiles':len(paths),
            'benefit':'A short native primary segment matches current SB owner to an AS triangle and obtains its actual flat normal. This removes 32516/78152 incorrect smooth-normal self-hits. Separately, vertex tangent-plane origin correction removes 3936/3936 undesired polygon terminator shadows across 16 light orientations on an eight-sided limb. A bridge visibility query limits displacement before nearby geometry: all 8448 independent 2mm blocker samples remain occluded.',
            'policy':'terminatorCorrection is explicit 0..1, default 1 for NPR; 0 retains original triangle geometry with numeric origin protection. Feature and per-camera Volume control it, product metadata reports actual strength, and history signature tracks it. Core V remains a real light-sample query at the declared approximate geometry, without baking N.L, albedo or ramp into it.',
            'cost':'One local primary query per receiver pixel per estimate, plus a bridge query when smooth displacement is nontrivial. No new screen texture or AS. Each light owns a 4-byte-per-instance-capacity owner buffer, retained by the existing output/host fence; common native hit payload carries an additional float3 smooth displacement. No NRD native ABI or DI/GI algorithm changes.',
            'limits':'Tangent-plane displacement approximates vertex-normal smooth geometry; it is not true subdivision, arbitrary displacement or normal-map shadowing. Local depth-window/owner association is not a unique raster primitive ID, so unresolved coplanar/layer ambiguity remains bounded. Sharp split normals, concavities and extreme low tessellation may still need model subdivision or per-scene strength. Real-world user scene remains unobserved by UI automation.',
            'retainedFailuresExplanation':'Original smooth-normal error retained. Two intermediate contact checks assumed blocker distance was measured from the unchanged raster point; actual V stayed 0 in all 8448 samples, but corrected-origin distances were 0.175-0.199mm. New acceptance explicitly checks unchanged physical blocker placement, valid V=0 and positive actual-ray distance, without hiding either failed run.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Curved verified: ten GPU cases, exact cross-path signals, real rotating-limb terminator and nearby blocker preservation; two 165-check serialized Players.')
if __name__=='__main__':main()
