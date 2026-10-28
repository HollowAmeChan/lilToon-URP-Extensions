"""Audit current R1 migration evidence, retaining rejected attempts as rejected."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).parent
REQUIRED = {'scene-rg','scene-compat','primary-rg','primary-compat','skinned-rg',
            'skinned-compat','di-cutout-rg','di-cutout-compat','gi-secondary-rg','scene-unsupported'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    accepted = None
    rejected = []
    for path in sorted(HERE.glob('run-*.json')):
        run = read(path)
        entries = run.get('entries', [])
        if not entries or any(not entry['pass'] for entry in entries):
            rejected.append(path.name)
        if {x['name'] for x in entries} == REQUIRED and all(x['pass'] for x in entries) and run['activeProcess'] is None:
            accepted = (path, run)
    if accepted is None:
        raise SystemExit('No completed ten-case R1 run; do not infer completion from partial passes.')
    path, run = accepted
    for entry in run['entries']:
        log = HERE / entry['logFile']
        assert hashlib.sha256(log.read_bytes()).hexdigest().lower() == entry['logSha256'].lower(), log
        assert entry['actualExit'] == 0 and entry['marker'] and not entry['diagnostics'] and not entry['abort'], entry
        if entry['name'].startswith('primary-'):
            assert entry['colorSpace'] == 'Gamma', 'Keep the existing raw-[Gamma]-value fixture domain explicit'
    sources = read(HERE/run['sourcesFile'])
    for source in sources:
        assert hashlib.sha256((ROOT/source['path']).read_bytes()).hexdigest().lower() == source['sha256'].lower(), source['path']
    migration = read(HERE/'migration.json')
    assert not migration['errors'] and migration['movedMetadataByteIdentical'] == 20
    result = {
        'stage':'R1', 'acceptedRun':path.name, 'cases':len(REQUIRED),
        'currentProductFingerprintsChecked':len(sources), 'rejectedAttempts':rejected,
        'scope':'Pure migration with supported-domain query/consumer checks, not host/SIGMA/full NPR acceptance.',
        'knownCaveats':[
            'First cold-import current scene run returned old transform distance; pre-migration control and subsequent current runs passed. Cause not proven.',
            'Original PrimarySurface raw [Gamma] property assertions run in their original Gamma domain; the rejected Linear-domain attempt remains recorded.',
            'Initial old validation project contained a frozen A01 namespace dependency; clean RT project is now used.',
            'First empty-project package resolution encountered ShaderGraph cache EPERM; matching existing independent-project cache was copied, without source changes.',
            'Existing four-allocation persistent cleanup warning remains in full Unity logs.',
        ]
    }
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R1 evidence verified: {len(REQUIRED)} cases, {len(sources)} current source hashes, {len(rejected)} rejected attempts retained.')


if __name__ == '__main__':
    main()
