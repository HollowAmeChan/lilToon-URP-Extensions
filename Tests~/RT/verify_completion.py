"""Audit R0-R6 delivery against preserved evidence and the current production source."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys

HERE = Path(__file__).parent
ROOT = HERE.parents[1]

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest().lower()

def main():
    subprocess.run([sys.executable,str(HERE/'Spatial/verify_results.py')],cwd=ROOT,check=True)
    proofs = {
        'R0': ['R0/baseline.json','R0/sigma-inventory.json'],
        'R1': ['R1/migration.json','R1/review.json'],
        'R2': ['R2/review.json'],
        'R3': ['R3/DI/migration.json','R3/DI/review.json','R3/GI/migration.json','R3/GI/review.json','R3/Player/review.json'],
        'R4': ['R4/Query/review.json','R4/review.json'],
        'R5': ['R5/review.json','R5/Production/review.json','R5/NPR/review.json','R5/Export/review.json','R5/Thin/review.json','R5/Player/review.json'],
        'R6': ['R6/review.json','R6/Player/review.json'],
        'Controls': ['Controls/review.json'],
        'Spatial': ['Spatial/review.json'],
    }
    evidence = {stage:[{'path':name,'sha256':digest(HERE/name)} for name in names] for stage,names in proofs.items()}
    assert not (ROOT/'Runtime/RayTracing').exists() and not (ROOT/'Editor/RayTracing').exists()
    for path in list((ROOT/'Runtime').rglob('*.cs'))+list((ROOT/'Editor').rglob('*.cs')):
        assert 'lilToon.URP.Extensions.RayTracing' not in path.read_text(encoding='utf-8-sig'),path
    inventory = read(HERE/'R0/sigma-inventory.json')
    assert inventory['version'] == [4,17,3] and inventory['abiSizes']['sigmaSettings'] == 20
    assert digest(ROOT/'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll') == inventory['sources'][0]['sha256'].lower()
    migration = read(HERE/'R1/migration.json')
    metas = [item for item in migration['approvedChanges'] if item['after'].endswith('.meta') and item['before'] != item['after']]
    assert migration['movedMetadataByteIdentical'] == 20 and len(metas) == 20
    for item in metas: assert digest(ROOT/item['after']) == item['workingSha256'].lower()

    # The final qualification changed only Editor validation and documentation.
    # Spatial verifier compares all Runtime and four custom inspectors to the
    # exact production commit used by the serialized Controls Players.
    ui = read(HERE/'Controls/review.json')
    for mode, run_file in ui['serializedPlayers'].items():
        run = read(HERE/'Controls/Player'/run_file)
        assert run['pass'] and not run['playerDiagnostics'] and not run['reviewedExistingCleanupWarnings']
        for source in read(HERE/'Controls/Player'/run['sourcesFile']):
            if source['path'].startswith(('Runtime/','../lilToon/')):
                assert digest(ROOT/source['path']) == source['sha256'].lower(),source['path']
    subprocess.run(['git','merge-base','--is-ancestor','24ae82da7503e3593ca205dd881a73b038c549e5','HEAD'],cwd=ROOT.parent/'lilToon',check=True)

    current_docs = ['Ho-RT.md','Ho-RTDI.md','Ho-RTGI.md','Ho-CharacterShadow.md','Ho-ShadowCast-PCSS.md','Ho-管线总览.md','文档索引.md','RTDI/README.md','RTGI/README.md','RTGI/Ho-RTGI-交接.md','RTDI/技术/Ho-RTDI-SceneSupport.md']
    checked_links = 0
    for name in current_docs:
        path = ROOT/'Documentation~'/name
        content = path.read_text(encoding='utf-8-sig')
        # Historical handoff body keeps its original paths and statements;
        # only its current navigation banner is a current contract.
        if name.endswith('交接.md'): content = content.split('\n\n')[1]
        assert not re.search(r'(RTShadow|SIGMA).*?(尚未实现|未实现设计|下一轮设计)',content),name
        for match in re.finditer(r'\]\(([^)]+)\)',content):
            target = match[1].split('#')[0]
            if not target or '://' in target: continue
            # Existing local source citations may carry one-based line numbers.
            target = re.sub(r':\d+$','',target)
            assert (path.parent/target).exists(),(name,target)
            checked_links += 1
    audit = {
        'objective':'R0-R6 Ho-RT ownership, DI/GI migration, independent per-light RTShadow, real SIGMA continuous V and original NPR ramp; requested Volume/Feature UI and diagnostics; deterministic final spatial coverage.',
        'result':'Delivered within the explicit supported and tested domains; no required implementation step from this bounded R0-R6 objective remains.',
        'evidence':evidence,
        'qualification':'Historical results retain their original source snapshots. Current Runtime/inspectors match Controls serialized Player production; current shader includes match those packed tests. New final spatial tests run on current source in both paths. No old SHA or failure is rewritten.',
        'currentDocumentationLinksChecked':checked_links,
        'preservedLimits':[
            'Win64 Mono D3D12 native RT, single-sample aligned GB/SB, supported opaque/basic Cutout and static/frozen-pose geometry domains. No full XR, IL2CPP, MSAA, hardware dynamic-resolution or arbitrary displaced/animated material claim.',
            'Directional finite angle supports SIGMA; point and spot are delta Cast with explicit range/cone/budget validity. Finite local/rect/disc/tube, cookie, baked-only and restricted caster linking remain explicitly unsupported shapes/policies, not incomplete silent approximations.',
            'Preview uses conservative reset for changed geometry/camera; full object-motion reprojection is not implemented. Same-frame export does not wait for preview convergence.',
            'SIGMA thin-card retention around 88% is bounded evidence, not perfect recovery. Raw high-spp reference remains available; final-color supersampling improves coverage without promising mathematically alias-free output.',
            'Preserve original DI full proposal/energy failures and native AS-growth scratch F39 controls, other DI/GI quality debts, default-AO/fallback diagnosis and rejected candidates; none was relabeled as passed.',
            'No universal all-light grey mask or automatic extra-light NPR BRDF synthesis; explicit per-light products serve consumers, optional weighted derived products remain separately defined future scope.',
            'No complete-frame GPU milliseconds/1080-budget claim. Resource counts and bounded CPU waits do not prove a performance gain. GUI pixels were not inspected through desktop automation.',
        ],
        'documentation':'Architecture, current controls, use and stage conclusions remain in Documentation~/Ho-RT.md. Existing module/index pages only correct status, ownership and navigation. Evidence and machine audits stay under Tests~/RT/.',
    }
    (HERE/'completion-audit.json').write_text(json.dumps(audit,indent=2)+'\n',encoding='utf-8')
    print('R0-R6 completion audit passed: original-stage evidence, current production/Player continuity, preserved unsupported domains and documentation links.')

if __name__ == '__main__': main()
