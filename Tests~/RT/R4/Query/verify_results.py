"""Review the native visibility prerequisite without claiming RTShadow completion."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
BASE='696262b4'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def old(path):return subprocess.check_output(['git','show',f'{BASE}:{path}'],cwd=ROOT).decode('utf-8-sig')
def normalized(s):return re.sub(r'\s+','',re.sub(r'//[^\n]*','',s))
def current(source):
    data=(ROOT/source['path']).read_bytes();expected=source['sha256'].lower()
    if hashlib.sha256(data).hexdigest()==expected:return True
    # Filter the accidental extra EOF blank line before commit. Preserve the
    # executed hashes and require this exact byte edit; no general normalization.
    return source['path']=='Editor/RTDI/HoRTDISceneValidation.cs' and hashlib.sha256(data+b'\r\n').hexdigest()==expected
def main():
    accepted={};rejected=[]
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None:continue
        if any(not current(s) for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            assert entry['pass'] and entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if entry['name'].startswith('native-query-'):
                p=HERE/f"report-{entry['name']}-{path.stem[4:]}.json";report=read(p)
                assert report['pass'] and report['requests']==13 and len(report['checks'])>=122
                assert 'Mirrored native culling agrees with actual raster front/back' in report['checks']
    assert set(accepted)=={'native-query-rg','native-query-compat','di-cutout-rg','di-cutout-compat','gi-native-cutout','scene-rg'}
    shared=(ROOT/'Runtime/RT/Shaders/HoRTCutoutAlpha.hlsl').read_text()
    shared=shared[shared.index('    if(backFace'):shared.index('    return alpha;')]
    for file in ['Runtime/RTDI/Shaders/HoRTDIVisibilityMaterial.shader','Runtime/RTGI/Shaders/HoRTGISecondaryMaterial.hlsl']:
        before=old(file);start=before.index('if(HitKind()==HIT_KIND_TRIANGLE_BACK_FACE&&_ShiftBackfaceUV>0)')
        body=before[start:before.index('if(alpha<_Cutoff)IgnoreHit();',start)]
        assert normalized(body.replace('HitKind()==HIT_KIND_TRIANGLE_BACK_FACE','backFace'))==normalized(shared),file
        assert 'HoRTEvaluateCutoutAlpha(uv,HitKind()==HIT_KIND_TRIANGLE_BACK_FACE)' in (ROOT/file).read_text()
    oldmeta=old('Runtime/RTDI/HoRTDIVisibilityMaterials.cs.meta')
    assert oldmeta.strip()==(ROOT/'Runtime/RT/HoRTVisibilityMaterials.cs.meta').read_text().strip()
    allowed={'Runtime/RTDI/HoRTDIRendererFeature.cs','Runtime/RTDI/HoRTDIVisibilityMaterials.cs',
             'Runtime/RTDI/Shaders/HoRTDIVisibilityMaterial.shader','Runtime/RTGI/Shaders/HoRTGISecondaryMaterial.hlsl'}
    files=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI'],cwd=ROOT).decode().splitlines()
    unchanged=[]
    for file in files:
        if file in allowed or Path(file).suffix not in {'.cs','.hlsl','.hlsli','.compute','.shader','.raytrace','.dll'}:continue
        assert old(file).replace('\r\n','\n')==(ROOT/file).read_text(encoding='utf-8-sig').replace('\r\n','\n'),file
        unchanged.append(file)
    compile_text=(HERE/'compile.log').read_text(encoding='utf-8-sig')
    assert 'ERRORS: 0' in compile_text and not re.search(r'error CS\d+',compile_text)
    review={'stage':'R4 native visibility prerequisite','accepted':accepted,'retainedFailures':rejected,
            'postValidationFormatting':'Removed exactly one accidental EOF newline from Editor/RTDI/HoRTDISceneValidation.cs; original executed hashes retained and exact byte transformation verified.',
            'referenceCommit':BASE,'unchangedDiGiCoreFiles':len(unchanged),'sharedAlphaMath':'identical operations to both previous any-hit implementations',
            'runtimeScope':'Host-owned native query at BeforeRenderingOpaques, normalized world-distance nearest and occlusion-only contracts, alpha holes, offscreen casters, Cast Off versus surface policy, Shadows Only, material front/back/off and renderer TwoSided, masks, invalid inputs, updates and actual mirrored raster reference. RG and compatibility, 122 checks / 13 requests each.',
            'benefit':'One bounded alpha implementation and a nearest-blocker query for SIGMA; caster semantics no longer forced onto DI/GI surface views.',
            'costAndLimits':'Different purpose views can require distinct AS; no GPU time or final shadow quality claim. This is not the RTShadow feature, SIGMA production, NPR receiver integration, or multilight completion.'}
    (HERE/'review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R4 query prerequisite verified: {len(accepted)} cases, exact shared alpha math, {len(unchanged)} untouched DI/GI core files. RTShadow/SIGMA still pending.')
if __name__=='__main__':main()
