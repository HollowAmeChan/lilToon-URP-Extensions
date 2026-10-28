from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[4];HERE=Path(__file__).parent
NEEDED={'gi-host-rg','gi-host-compat','gi-environment-rg','gi-environment-compat','gi-nrd-rg','gi-nrd-compat','gi-unsupported','gi-native-cutout','di-cutout-rg'}
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def main():
    accepted=None
    for p in sorted(HERE.glob('run-*.json')):
        r=read(p)
        if {e['name'] for e in r['entries']}==NEEDED and all(e['pass'] for e in r['entries']) and r['activeProcess'] is None:accepted=(p,r)
    assert accepted,'No completed GI migration run'
    p,r=accepted
    for e in r['entries']:
        assert e['actualExit']==0 and e['marker'] and not e['diagnostics'] and not e['abort'],e
        assert hashlib.sha256((HERE/e['logFile']).read_bytes()).hexdigest().lower()==e['logSha256'].lower(),e['name']
    sources=read(HERE/r['sourcesFile'])
    for s in sources:assert hashlib.sha256((ROOT/s['path']).read_bytes()).hexdigest().lower()==s['sha256'].lower(),s['path']
    checks={}
    for mode in ('rg','compat'):
        h=read(HERE/f'host-report-{mode}.json');assert h['pass'] and len(h['checks'])>=18,h
        assert any('NRD paths actually dispatch' in x for x in h['checks']),h
        checks[mode]=h['checks']
    result={'stage':'R3-GI','acceptedRun':p.name,'cases':len(NEEDED),'sourceHashes':len(sources),'productionPairChecks':checks,
            'scope':'Production DI/GI shared pose, separate native profiles/history/NRD and bounded environmental/NRD regressions; full old quality and AS growth gaps remain.',
            'benefit':'Both production effects now borrow scenes from the host and share current skin snapshots; distinct AS views remain. No GPU timing improvement claimed.'}
    (HERE/'review.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R3-GI verified: {len(NEEDED)} cases, {len(sources)} current source hashes.')
if __name__=='__main__':main()
