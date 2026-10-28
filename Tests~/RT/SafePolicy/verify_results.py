from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
accepted={}
for path in sorted(HERE.glob('run-*.json')):
    run=read(path)
    if run['activeProcess'] is not None:continue
    if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
    for entry in run['entries']:
        assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
        assert entry['pass'] and entry['marker'] and entry['actualExit']==0 and not entry['diagnostics']
        report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json");assert report['pass']
        for text in ['cannot reactivate','Volume correction','values preserved','actual correction zero','product explicitly reports']:
            assert any(text in check for check in report['checks']),text
        accepted[entry['name']]=path.name
assert set(accepted)=={'safe-policy-rg','safe-policy-compat'}
compiled=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compiled and not re.search(r'error CS\d+',compiled)
review={'stage':'Contain real-character displacement regression','accepted':accepted,'result':'Saved Feature and Volume nonzero correction cannot activate the regressed path. Exact current raw images equal strength-zero reference in both GPU paths, actual metadata is zero and serialized values are preserved.',
        'limits':'Containment, not root-cause or full character-quality acceptance. Uncommitted origin experiments are isolated from the package directory used by the user Editor.'}
(HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
print('Safe policy verified in both GPU paths; root-cause work remains isolated.')
