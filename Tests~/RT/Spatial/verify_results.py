"""Recompute final-color spatial quality from real renderer outputs, without editing historical runs."""
from pathlib import Path
import array
import hashlib
import json
import re
import subprocess

HERE = Path(__file__).parent
ROOT = HERE.parents[2]
BASE = 'c4b7d4ed'

def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest().lower()
def signal(path):
    values = array.array('f'); values.frombytes(path.read_bytes())
    assert len(values) == 64 * 64 * 4
    return values
def mse(a, b): return sum((a[i]-b[i])**2 for i in range(len(a)) if i % 4 != 3) / (64 * 64 * 3)

def main():
    accepted, rejected, metrics, signals = {}, [], {}, {}
    for path in sorted(HERE.glob('run-*.json')):
        run = read(path)
        for entry in run['entries']:
            assert digest(HERE / entry['logFile']) == entry['logSha256'].lower()
            if not entry['pass']: rejected.append({'run': path.name, 'case': entry['name'], 'diagnostics': entry['diagnostics']})
        if run['activeProcess'] is not None: continue
        if any(digest(ROOT / item['path']) != item['sha256'].lower() for item in read(HERE / run['sourcesFile'])): continue
        for entry in run['entries']:
            if not entry['pass']: continue
            assert entry['actualExit'] == 0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            name, stamp = entry['name'], path.stem[4:]
            report = read(HERE / f'report-{name}-{stamp}.json')
            assert report['pass'] and report['renderScale'] == 2 and report['referenceScale'] == 8
            assert len(report['poses']) == 4 and len(report['checks']) == 15
            metrics[name] = {}
            for pose in report['poses']:
                prefix = f"{name}-{stamp}-spatial-{pose['name']}-"
                reference = signal(HERE / (prefix+'reference.rgba32'))
                native = signal(HERE / (prefix+'single.rgba32'))
                spatial = signal(HERE / (prefix+'supersampled.rgba32'))
                native_mse, spatial_mse = mse(native, reference), mse(spatial, reference)
                assert abs(native_mse-pose['singleMse']) < 1e-8 and abs(spatial_mse-pose['supersampledMse']) < 1e-8
                assert spatial_mse < native_mse * (.8 if pose['name'] != 'sigma-soft' else 1)
                metrics[name][pose['name']] = {'singleMse': native_mse, 'supersampledMse': spatial_mse, 'reduction': 1-spatial_mse/native_mse}
                for suffix in ['reference', 'single', 'supersampled']:
                    signals[(name,pose['name'],suffix)] = digest(HERE / (prefix+suffix+'.rgba32'))
            accepted[name] = path.name
    assert set(accepted) == {'spatial-rg','spatial-compat'}
    assert len(rejected) == 1 and any('CS0111' in text for text in rejected[0]['diagnostics'])
    for pose in ['rest','micro1','micro2','sigma-soft']:
        for suffix in ['reference','single','supersampled']:
            assert signals[('spatial-rg',pose,suffix)] == signals[('spatial-compat',pose,suffix)], (pose,suffix)
    # This qualification uses the existing production path: no runtime or GUI
    # algorithm was added after the serialized UI/NRD/multilight Players.
    paths = subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime'],cwd=ROOT).decode().splitlines()
    paths += ['Editor/RT/HoRTRendererFeatureEditor.cs','Editor/RTShadow/HoRTShadowRendererFeatureEditor.cs','Editor/RTShadow/HoRTShadowVolumeEditor.cs','Editor/RTShadow/HoRTShadowLightEditor.cs']
    blobs = subprocess.run(['git','cat-file','--batch'],cwd=ROOT,check=True,stdout=subprocess.PIPE,input=''.join(f'{BASE}:{path}\n' for path in paths).encode()).stdout
    cursor = 0
    for path in paths:
        end = blobs.index(b'\n',cursor); size = int(blobs[cursor:end].split()[2]); cursor = end+1
        original = blobs[cursor:cursor+size]; cursor += size+1
        assert original.replace(b'\r\n',b'\n') == (ROOT/path).read_bytes().replace(b'\r\n',b'\n'),path
    compile_log = (HERE/'compile.log').read_text(encoding='utf-8-sig')
    assert 'ERRORS: 0' in compile_log and not re.search(r'error CS\d+',compile_log)
    review = {
        'stage':'Final-color spatial sampling via existing URP Render Scale', 'accepted':accepted, 'metrics':metrics, 'retainedFailures':rejected,
        'productionReference':BASE, 'unchangedRuntimeAndInspectorFiles':len(paths),
        'benefit':'Actual 128-square primary/RT Cast/nonlinear ramp followed by 64-square final color improves against independent 512-square render/box-resolve reference. Three hard-cast poses and a finite-angle 16-estimate SIGMA case, fixed-frame repeat and exact cross-path floating outputs pass.',
        'cost':'2x per axis gives 4x shaded pixels, trace workload and per-light screen resources; estimate count remains 16 in the SIGMA case, no hidden history warmup. No complete-frame GPU milliseconds claim.',
        'limits':'64-square deterministic bounded scenes with single-sample GB/SB and linear HDR target. The 8x box-resolve reference remains finite sampling. No arbitrary animated shader, XR/MSAA/dynamic-resolution/camera-stack/capture-tool qualification or perfect alias-free guarantee.',
        'scope':'Reuse production URP Render Scale; no extra RT Feature, post-ramp visibility blur or unrelated capture-tool rewrite.'
    }
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('Spatial verified: two GPU paths, four pose/mode cases each, exact cross-path images; all production Runtime and four inspectors unchanged since serialized Players.')

if __name__ == '__main__': main()
