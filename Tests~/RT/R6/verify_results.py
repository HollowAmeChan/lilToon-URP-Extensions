"""Qualify stable per-light products and real downstream reads in both paths."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent;BASE='bc886ed3'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().lower()
def main():
    accepted={};rejected=[];reports={}
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None:continue
        if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            assert entry['pass'] and entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if not entry['name'].startswith('multi-'):continue
            report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
            assert report['pass'] and report['checks']==54 and report['requests']==21
            for text in ['Four independent light products scheduled','Directional/point/spot products preserve independent light visibility','Point trace returns nearest blocker','Caster beyond the point source','Out-of-range surface','Outside spot cone','Insufficient ray budget','Changing the same Light type','Core geometric V remains separate','One light revision resets','All-supported request discovers','Unsupported cookie','Recreated Light never inherits','Downstream graph/compatibility consumer actually reads']:
                assert any(text in evidence for evidence in report['evidence']),text
            reports[entry['name']]={'run':path.name,'requests':21,'checks':54,'report':f"report-{entry['name']}-{path.stem[4:]}.json"}
    assert set(accepted)=={'multi-rg','multi-compat','npr-rg','npr-compat','shadow-sigma-rg','export-rg'}
    assert len(rejected)>=3,'Initial energy/helper/cookie test mistakes retained'
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/NRD','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout;cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1;original=blob[cursor:cursor+size];cursor+=size+1
        assert original.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
    compiled=(HERE/'compile.log').read_text(encoding='utf-8-sig');assert 'ERRORS: 0' in compiled and not re.search(r'error CS\d+',compiled)
    review={'stage':'R6 bounded per-light collection and analytic point/spot products','accepted':accepted,'multiReports':reports,'retainedFailures':rejected,'referenceCommit':BASE,'unchangedDiGiNrdCharacterShadowFiles':len(paths),
            'benefit':'Explicit Main/Declared/AllSupported selection, lifetime-stable LightId independent of slot and separate per-light resources/history. Real finite point/spot segments and domain statuses, per-light receiver declarations, unavailable metadata and current graph/compat borrowing APIs. Actual downstream compute consumes each raw/visibility product. Additional products cannot overwrite main NPR alias.',
            'cost':'Per-light owned full-resolution raw/receiver resources; finite-angle directional SIGMA adds per-light SDK/input/output/history. Resources for omitted/unsupported requests retire through host fence. Single main request does not pay all-light storage/tracing. No light-count-independent cost or GPU milliseconds claim.',
            'supportedMeasure':'Directional finite angular disk uses the existing uniform-solid-angle samples; delta point/spot are exact one-direction visibility with no invented source radius or soft reconstruction. Local outside range/cone is pixel status 3, ray budget shorter than light endpoint is status 4. Color/intensity are not part of V.',
            'limits':'No finite-radius local/rect/disc/tube emitter, cookies, baked-only lights or restricted caster light linking. These are explicit unsupported scope, not approximated. No universal all-lights grey mask is produced. Graph TextureHandle lifetime stays current camera graph; compatibility borrowing is valid only after current producer commands, not a GPU completion promise. R6 Player, final spatial AA/export scope and completion audit remain.',
            'next':'Validate final multi-light production in serialized Player and final image sampling; audit original R0-R6 requirements before deciding goal completion.'}
    (HERE/'review.json').write_text(json.dumps(review,indent=2)+'\n',encoding='utf-8')
    print('R6 verified: six GPU cases; 54 checks / 21 requests per path with real per-light consumption; goal completion not claimed.')
if __name__=='__main__':main()
