"""Qualify raster-origin self-Cast without weakening the original failure or nearby-blocker checks."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent;BASE='bcd83abf'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def current(folder,run):return all(digest(ROOT/s['path'])==s['sha256'].lower() for s in read(folder/run['sourcesFile']))

def main():
    accepted={};rejected=[];reports={};signals={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None or not current(HERE,run):continue
        for entry in run['entries']:
            if not entry['pass']:continue
            assert entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            name=entry['name'];stamp=path.stem[4:]
            accepted[name]=path.name
            if name.startswith('self-'):
                report=read(HERE/f'{name}-{stamp}-self-report.json')
                assert report['pass'] and report['validPixels']==143131 and report['falseShadowPixels']==report['jitterFlips']==0
                assert report['stableCompared']==124889 and report['contactBlocked']==8448
                assert report['skinnedValid']==33792 and report['skinnedFalse']==0
                reports[name]=report
                for kind in ['ortho','perspective']:signals[(name,kind)]=digest(HERE/f'{name}-{stamp}-self-{kind}.rgba32')
            elif name.startswith('multi-'):
                report=read(HERE/f'report-{name}-{stamp}.json');assert report['pass'] and report['checks']==54
            elif name.startswith('export-'):
                report=read(HERE/f'report-{name}-{stamp}.json')
                assert report['pass'] and report['multiMse']<report['singleMse'] and report['multiRampMse']<report['singleRampMse']
    assert set(accepted)=={'self-rg','self-compat','multi-rg','multi-compat','export-rg'}
    assert len(rejected)==3,'Preserve original self-shadow failures and the contact-card fixture error'
    controls=[]
    for path in HERE.glob('self-*-self-report.json'):
        report=read(path)
        if report['falseShadowPixels']==64233 and report['jitterFlips']==3096 and report['contactBlocked']==8448:controls.append(path.name)
    assert controls,'Exact original production reference failure retained after fixing independent contact fixture'
    for kind in ['ortho','perspective']:assert signals[('self-rg',kind)]==signals[('self-compat',kind)]

    players={};folder=HERE/'Player'
    for path in sorted(folder.glob('run-*.json')):
        run=read(path)
        if not run.get('pass') or run['activeProcess'] is not None or not current(folder,run):continue
        assert run['buildExit']==run['playerExit']==0 and not run['buildDiagnostics'] and not run['playerDiagnostics'] and not run['reviewedExistingCleanupWarnings']
        for prefix in ['build','player']:assert digest(folder/run[prefix+'Log'])==run[prefix+'LogSha256'].lower()
        assert run['probeSha256'].lower()==digest(folder/'HoRTShadowPlayerProbe.cs.txt')
        assert run['buildSourceSha256'].lower()==digest(folder/'BuildShadowPlayer.cs.txt')
        assert run['packedNativeSha256'].lower()==digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')
        report=read(folder/run['playerReport']);assert report['pass'] and report['updates']==21 and len(report['checks'])==148
        assert sum('Packed raster-to-AS grazing self-caster' in item for item in report['checks'])==8
        players[run['mode']]=path.name
    assert set(players)=={'rg','compat'}

    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RT','Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout;cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1;original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compiled=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compiled and not re.search(r'error CS\d+',compiled)
    review={'stage':'RTShadow grazing raster-origin self-intersection guard','referenceCommit':BASE,'accepted':accepted,'reports':reports,'serializedPlayers':players,'retainedFailures':rejected,'originalProductionControl':controls,
            'unchangedRuntimeHostDiGiNrdCharacterShadowFiles':len(paths),
            'benefit':'Outgoing mesh-normal ray origin guard eliminates 64233/143131 false self-shadows and 3096 jitter-induced visibility flips in an analytically unoccluded two-sided plane. Four bone poses remain lit; a real 2mm blocker retains 8448 occluded samples. Per-light and current no-history SIGMA regressions remain valid. Both serialized Players verify packed grazing jitter plus original 132 controls.',
            'cost':'Small per-ray origin arithmetic, no new texture, history, trace or AS. Delta local direction and finite endpoint are recomputed from the actual shifted origin.',
            'limits':'Normal clearance uses existing world-metre rayBias and a representable world-position rounding floor; not the full triangle-reconstruction error-bound algorithm. Interpolated mesh normal is not flat primitive normal. Excessive bias can still detach tiny contacts. Explicit projection jitter and bone poses are tested; no desktop/live user scene or complete TAA reprojection qualification. Screenshot matching is an evidence-based cause hypothesis, not proof that every artifact in the user scene is removed.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Self-Cast verified: both paths zero false self-shadow/jitter flips, actual bone poses and retained 2mm contacts; five GPU cases and two 148-check Players.')
if __name__=='__main__':main()
