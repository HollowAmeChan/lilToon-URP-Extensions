"""Audit migrated DI output/dependency checks without rewriting historical runs."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[4]
HERE=Path(__file__).parent
REQUIRED={'di-cutout-rg','di-cutout-compat','di-denoise-rg','di-denoise-compat',
          'di-host-rg','di-host-compat','di-unsupported','host-rg','gi-secondary-rg'}
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def main():
    accepted=None
    for p in sorted(HERE.glob('run-*.json')):
        r=read(p)
        if {e['name'] for e in r['entries']}==REQUIRED and all(e['pass'] for e in r['entries']) and r['activeProcess'] is None:accepted=(p,r)
    assert accepted,'No complete nine-case bounded DI migration run'
    p,r=accepted
    for e in r['entries']:
        assert e['actualExit']==0 and e['marker'] and not e['diagnostics'] and not e['abort'],e
        assert hashlib.sha256((HERE/e['logFile']).read_bytes()).hexdigest().lower()==e['logSha256'].lower(),e['name']
    sources=read(HERE/r['sourcesFile'])
    for s in sources:assert hashlib.sha256((ROOT/s['path']).read_bytes()).hexdigest().lower()==s['sha256'].lower(),s['path']
    checks={}
    for mode in ('rg','compat'):
        report=read(HERE/f'host-report-{mode}.json');assert report['pass'] and len(report['checks'])>=15,report
        checks[mode]=report['checks']
    baseline=[read(p) for p in HERE.glob('baseline-core-*.json')]
    assert any(b['actualExit']==1 and b['powerProposalFailure'] and b['asScratchDiagnostics']==4 for b in baseline),'Missing matching rejected pre-migration core control'
    result={'stage':'R3-DI','acceptedRun':p.name,'cases':len(REQUIRED),'sourceHashesChecked':len(sources),
            'hostChecks':checks,'scope':'DI host migration and existing analytic/Cutout/RELAX regressions; GI migration and full RTShadow are still outstanding.',
            'benefit':'DI no longer owns a private world scan, scene AS or hit-profile lifetime; host owns its scene view.',
            'limits':'The legacy full core test remains rejected: exact pre-migration control reproduces four AS scratch errors and power-proposal assertion. No full-core pass or frame-time claim. Mixed production DI+GI shared-pose acceptance awaits GI migration. Existing cleanup warnings preserved.'}
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R3-DI verified: {len(REQUIRED)} cases, {len(sources)} unchanged source hashes.')
if __name__=='__main__':main()
