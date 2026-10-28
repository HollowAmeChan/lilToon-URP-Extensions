"""Check completed host/regression runs and exact current source fingerprints."""
from pathlib import Path
import hashlib
import json

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
REQUIRED={'host-rg','host-compat','host-retirement-rg','host-retirement-compat','host-unsupported','skinned-rg','skinned-compat','di-cutout-rg','gi-secondary-rg'}


def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))


def main():
    accepted=None
    for p in sorted(HERE.glob('run-*.json')):
        run=read(p)
        if {e['name'] for e in run['entries']}==REQUIRED and all(e['pass'] for e in run['entries']) and run['activeProcess'] is None:
            accepted=(p,run)
    assert accepted is not None,'Missing completed nine-case host/regression run'
    p,run=accepted
    for e in run['entries']:
        log=HERE/e['logFile']
        assert hashlib.sha256(log.read_bytes()).hexdigest().lower()==e['logSha256'].lower(),log
        assert e['actualExit']==0 and e['marker'] and not e['diagnostics'] and not e['abort'],e
    sources=read(HERE/run['sourcesFile'])
    for source in sources:
        assert hashlib.sha256((ROOT/source['path']).read_bytes()).hexdigest().lower()==source['sha256'].lower(),source['path']
    reports={}
    for name in ('rg','compat','unsupported','retirement-rg','retirement-compat'):
        report=read(HERE/f'report-{name}.json')
        assert report['pass'] and not report['failure'],report
        if name in ('rg','compat'):
            assert report['bakeCount']==3 and report['createdProfiles']==report['disposedProfiles']==3,report
            assert report['checks']>=90 and report['renderRequests']==11,report
        if name.startswith('retirement-'):
            assert report['bakeCount']==2 and report['createdProfiles']==report['disposedProfiles']==2,report
            assert any('before the tail fence' in x for x in report['evidence']),report
        reports[name]={k:report[k] for k in ('checks','renderRequests','bakeCount','createdProfiles','disposedProfiles')}
    result={'stage':'R2','acceptedRun':p.name,'sourceHashesChecked':len(sources),'reports':reports,
            'scope':'Host shared pose/view and bounded lifecycle checks, standalone scene consumers unchanged; DI/GI host migration is R3.',
            'cost':'Different AS views remain. First implementation waits for previous GPU fence before mutating shared Mesh/material/AS; no frame-time improvement claimed.',
            'knownLimits':'Captured work without an executed completion fence remains quarantined rather than being freed on timeout. Normal tail and explicit retirement paths are separate qualifications.'}
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R2 verified: 9 cases, {len(sources)} exact source hashes; shared pose updates and profile releases balanced.')


if __name__=='__main__': main()
