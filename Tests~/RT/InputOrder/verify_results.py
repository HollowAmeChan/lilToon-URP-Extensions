from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
accepted={};unfinished=[]
for p in sorted(HERE.glob('run-*.json')):
    run=read(p)
    if run['activeProcess'] is not None:unfinished.append(p.name);continue
    for e in run['entries']:assert digest(HERE/e['logFile'])==e['logSha256'].lower()
    if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
    for e in run['entries']:
        assert e['pass'] and e['actualExit']==0 and e['marker'] and not e['diagnostics']
        report=read(HERE/f"report-{e['name']}-{p.stem[4:]}.json")
        assert report['pass']
        for phrase in ['all-red InvalidSurface','restores current receiver shadow','late event','pre-cull']:
            assert any(phrase in text for text in report['checks']),phrase
        accepted[e['name']]={'run':p.name,'checks':len(report['checks']),'requests':report['requests']}
assert set(accepted)=={'input-order-rg','input-order-compat'}
review={'stage':'RTShadow producer order diagnosis','accepted':accepted,'unfinishedRunsPreserved':unfinished,
        'benefit':'Inspector identifies missing/inactive host/GB/SB, wrong host pre-cull order, GB scale mismatch, same-event list reversal and too-late producer event. Both GPU paths reproduce all InvalidSurface for SB after RTShadow, and restore actual valid Cast by correcting order.',
        'limits':'Scheduling count does not prove valid GPU pixels; diagnosis does not change renderer assets or silently reorder dependencies. Runtime algorithms unchanged. Canonical whole-pipeline placement and required new Feature declarations live in Documentation~/Ho-管线总览.md.'}
(HERE/'review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Input order verified in both paths with real bad-order / restored-Cast outputs.')
