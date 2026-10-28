"""Verify actual material Cast reception without URP shadow maps."""
from pathlib import Path
from array import array
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def signal(p):
    f=array('f');f.frombytes(p.read_bytes());assert len(f)==64*64*4;return f
def main():
    accepted={};failures=[];metrics={};signals={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:failures.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None:continue
        if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            if not entry['pass']:continue
            assert entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if not entry['name'].startswith('npr-'):continue
            report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
            assert report['pass'] and report['requests']==18 and report['toonShadow']==0 and report['toonReceiveOff']==report['toonLit']==1
            checks=report['checks'];assert len(checks)==32
            for text in ['URP main shadow maps disabled','Original material receive mask','Light shadow strength','Second shadow layer consumes','Third nested shadow layer','Third-layer receive switch','Real narrow ramp thresholds']:
                assert any(text in c for c in checks),text
            alignment=[c for c in checks if c.startswith('Asymmetric perspective/jitter')]
            assert len(alignment)==2 and all(c.endswith('maxError=0') for c in alignment)
            metrics[entry['name']]={'requests':18,'checks':len(checks),'shadow':0,'receiveOff':1,'lit':1,'alignment':alignment,'ramp':next(c for c in checks if c.startswith('Real narrow ramp'))}
            signals[entry['name']]={}
            for name in ['narrow','wide']:
                file=f"{entry['name']}-{path.stem[4:]}-{name}.rgba32";data=signal(HERE/file)
                assert all(0<=v<=1 for i,v in enumerate(data) if i%4<3)
                signals[entry['name']][name]={'file':file,'sha256':digest(HERE/file),'preview':file.replace('.rgba32','.png')}
            assert signals[entry['name']]['narrow']['sha256']!=signals[entry['name']]['wide']['sha256']
    expected={'npr-rg','npr-compat','raster-rg','raster-compat','raster-d3d11','shadow-sigma-rg','shadow-sigma-compat','di-cutout-rg','gi-native-cutout'}
    assert set(accepted)==expected,(set(accepted),expected)
    for name in ['narrow','wide']:assert signals['npr-rg'][name]['sha256']==signals['npr-compat'][name]['sha256']
    includes=ROOT.parent/'lilToon/Assets/lilToon/Shader/Includes'
    sourcefiles=['lil_common_macro.hlsl','lil_common_frag.hlsl','lil_pass_forward_normal.hlsl']
    sources={name:digest(includes/name) for name in sourcefiles}
    assert 'defined(LIL_USE_HO_CAST)' in (includes/'lil_common_frag.hlsl').read_text(encoding='utf-8-sig')
    assert 'defined(LIL_USE_HO_CAST)' in (includes/'lil_pass_forward_normal.hlsl').read_text(encoding='utf-8-sig')
    compilation=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compilation and not re.search(r'error CS\d+',compilation)
    review={'stage':'R4 actual NPR reception + bounded R5 ramp acceptance','accepted':accepted,'retainedFailures':failures,'materialMetrics':metrics,'signals':signals,'lilToonIncludeSha256':sources,
            'benefit':'Actual lilToon receives raw or SIGMA Cast before its existing layered ramps, including when URP has no main shadow-map keyword. Material receive masks, nested per-layer switches, one light strength and fallback remain. Asymmetric perspective/jitter reception matches actual primary pixels.',
            'rootCause':'Original Cast test enabled realtime AO by default without an AO producer. Replay showed ~0.2158203 AO suppressing the ramp; only private test material AO was disabled. Original failed logs remain, and production AO settings/algorithm were not changed.',
            'costAndScope':'Extra guarded screen loads before the existing ramp, no new global shadow keyword or lighting-composition rewrite. 64x64 two-part fixture only; no claim of animation/character hair, AA convergence, normal automatic camera Player qualification, or multilight outputs. Original CS receiver-selection regression passes D3D12 RG/compat and D3D11. Full CS atlas-debug test is retained as separate rejected evidence.',
            'next':'Qualify character thin/cutout dynamic behavior and no-history export; extend stable per-light request/output contract in R6. Full goal remains active.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('NPR verified: 9 GPU cases; actual three-layer reception without URP maps, matching RG/compat ramp pixels; full goal still active.')
if __name__=='__main__':main()
