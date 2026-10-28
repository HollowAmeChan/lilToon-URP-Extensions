"""Verify current-source camera overrides, debug pixels, unchanged RT effects and serialized Players."""
from pathlib import Path
import hashlib
import json
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).parent
BASE = '63b8097e'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().lower()

def current_sources(folder, run):
    return all(digest(ROOT / source['path']) == source['sha256'].lower()
               for source in read(folder / run['sourcesFile']))

def main():
    accepted, rejected, signals = {}, [], {}
    for path in sorted(HERE.glob('run-*.json')):
        run = read(path)
        for item in run['entries']:
            assert digest(HERE / item['logFile']) == item['logSha256'].lower()
            if not item['pass']:
                rejected.append({'run': path.name, 'case': item['name'], 'diagnostics': item['diagnostics'], 'marker': item['marker']})
        if run['activeProcess'] is not None or not current_sources(HERE, run):
            continue
        for item in run['entries']:
            if not item['pass']:
                continue
            assert item['actualExit'] == 0 and item['marker'] and not item['diagnostics'] and item['abort'] is None
            name, stamp = item['name'], path.stem[4:]
            report = read(HERE / f'report-{name}-{stamp}.json')
            assert report['pass']
            if name.startswith('controls-'):
                assert report['requests'] == 26 and len(report['checks']) == 51
                for expected in ['Second camera Volume layer mask', 'actual same-frame SIGMA context', 'master switch',
                                 'Per-light angle takes priority', 'Raw debug shows actual', 'Reconstructed debug uses decoded',
                                 'Difference debug', 'Actual sampled nearest blocker', 'Missing slot is purple',
                                 'Missing host clears', 'Missing primary surface is red', 'additional light debug',
                                 'Mid-graph Feature Create', 'restores exact original beauty']:
                    assert any(expected in text for text in report['checks']), expected
                for mode in ['raw', 'visibility', 'difference', 'blocker', 'receiver', 'status', 'all']:
                    signal = HERE / f'{name}-{stamp}-controls-{mode}.rgba32'
                    assert signal.stat().st_size == 64 * 64 * 16
                    signals[(name, mode)] = digest(signal)
            else:
                assert report['checks'] == 54 and report['requests'] == 21
            accepted[name] = path.name
    assert set(accepted) == {'controls-rg', 'controls-compat', 'multi-rg', 'multi-compat'}
    assert len(rejected) == 3, 'Retain two rejected oracles and one wrong executeMethod entry'
    for mode in ['raw', 'visibility', 'difference', 'blocker', 'receiver', 'status', 'all']:
        assert signals[('controls-rg', mode)] == signals[('controls-compat', mode)], mode

    player_folder = HERE / 'Player'
    players = {}
    for path in sorted(player_folder.glob('run-*.json')):
        run = read(path)
        if not run.get('pass') or run['activeProcess'] is not None or not current_sources(player_folder, run):
            continue
        assert run['buildExit'] == run['playerExit'] == 0 and not run['buildDiagnostics'] and not run['playerDiagnostics']
        assert not run['reviewedExistingCleanupWarnings'], 'No GC allowance for this qualification'
        assert digest(player_folder / run['buildLog']) == run['buildLogSha256'].lower()
        assert digest(player_folder / run['playerLog']) == run['playerLogSha256'].lower()
        assert run['probeSha256'].lower() == digest(player_folder / 'HoRTShadowPlayerProbe.cs.txt')
        assert run['buildSourceSha256'].lower() == digest(player_folder / 'BuildShadowPlayer.cs.txt')
        assert run['packedNativeSha256'].lower() == digest(ROOT / 'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')
        report = read(player_folder / run['playerReport'])
        assert report['pass'] and report['updates'] == 21 and len(report['checks']) == 132
        for text in ['Serialized RTShadow Volume asset', 'Volume disables current', 'Volume selects actual SIGMA',
                     'resolved budget', 'Packed debug shader displays', 'Packed debug selects additional',
                     'missing debug slot', 'Packed All debug grid', 'restores exact beauty']:
            assert any(text in evidence for evidence in report['checks']), text
        log = (player_folder / run['playerLog']).read_text(encoding='utf-8-sig')
        assert 'retiredDomains=0 retiredResources=0' in log and 'private OB teardown released=4' in log
        players[run['mode']] = path.name
    assert set(players) == {'rg', 'compat'}

    paths = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', BASE,
                                   'Runtime/RT', 'Runtime/RTDI', 'Runtime/RTGI', 'Runtime/NRD', 'Runtime/CharacterShadow'], cwd=ROOT).decode().splitlines()
    blobs = subprocess.run(['git', 'cat-file', '--batch'], cwd=ROOT, check=True, stdout=subprocess.PIPE,
                           input=''.join(f'{BASE}:{path}\n' for path in paths).encode()).stdout
    cursor = 0
    for name in paths:
        end = blobs.index(b'\n', cursor)
        size = int(blobs[cursor:end].split()[2]); cursor = end + 1
        original = blobs[cursor:cursor+size]; cursor += size + 1
        assert original.replace(b'\r\n', b'\n') == (ROOT / name).read_bytes().replace(b'\r\n', b'\n'), name
    compile_log = (HERE / 'compile.log').read_text(encoding='utf-8-sig')
    assert 'ERRORS: 0' in compile_log and not re.search(r'error CS\d+', compile_log)
    review = {
        'stage': 'RTShadow camera Volume / inspectors / per-light diagnostic presentation',
        'acceptedGpu': accepted, 'serializedPlayers': players, 'retainedFailures': rejected,
        'unchangedRuntimeRtDiGiNrdCharacterShadowFiles': len(paths), 'referenceCommit': BASE,
        'benefit': 'Camera-specific quality without mutating Feature defaults; master switch, inactive and other-camera fallback verified. Six actual per-light debug views and All grid use the shared final presentation, missing product is explicit purple. Main alias unaffected; debug-off beauty identical. Host and light inspectors explain ownership, supported domains and actual parameters.',
        'cost': 'Diagnostic color target and six fullscreen draws only for enabled All debug; single view one draw. Snapshot copies and CPU diagnostics added, no trace count change from UI alone. SIGMA quality overrides retain their existing full per-estimate cost.',
        'limits': 'Shader pixels, serialization, inspector type selection and compile verified in hidden independent project; no desktop/UI operation or claim of manually inspected GUI layout. Win64 Mono D3D12 explicit requests only. Debug raw is last single sample during repeated SIGMA, difference is not a ground-truth error image. Final spatial AA/export and full goal audit remain.',
        'next': 'Finish deterministic final-color spatial sampling and original R0-R6 requirement audit; retain bounded qualification scope.'
    }
    (HERE / 'review.json').write_text(json.dumps(review, indent=2) + '\n', encoding='utf-8')
    print('Controls verified: two 51-check / 26-request GPU cases, original multilight pair, seven byte-identical debug signals and two 132-check serialized Players.')

if __name__ == '__main__':
    main()
