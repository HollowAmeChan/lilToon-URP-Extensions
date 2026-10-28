from pathlib import Path
import hashlib,json,re
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
accepted={};rejected=[]
for path in sorted(HERE.glob('run-*.json')):
    run=read(path)
    if run['activeProcess'] is not None:continue
    if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
    for entry in run['entries']:
        assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
        if not entry['pass']:
            rejected.append({'run':path.name,'case':entry['name'],'marker':entry['marker'],'diagnostics':entry['diagnostics']});continue
        assert entry['pass'] and entry['marker'] and entry['actualExit']==0 and not entry['diagnostics']
        report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json");assert report['pass']
        for text in ['cannot reactivate','Volume correction','values preserved','actual correction zero','no current product','NPR activation is zero','Actual production disabled','Positive GPU control','Explicit stale GPU activation','saved debug mode']:
            assert any(text in check for check in report['checks']),text
        accepted[entry['name']]=path.name
assert set(accepted)=={'safe-policy-rg','safe-policy-compat'}
compiled=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compiled and not re.search(r'error CS\d+',compiled)
review={'stage':'Suspend entire regressed RTShadow producer','accepted':accepted,'rejectedAttempts':rejected,'result':'Entire RTShadow production and NPR activation remain disabled even with saved Feature/Volume enabled and correction one. No current or stale product is exposed; beauty is independent of these stored values in both GPU paths. Serialized user values are preserved.',
        'limits':'Containment, not root-cause or full character-quality acceptance. Uncommitted origin experiments are isolated from the package directory used by the user Editor.'}
(HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
print('Safe policy verified in both GPU paths; root-cause work remains isolated.')
