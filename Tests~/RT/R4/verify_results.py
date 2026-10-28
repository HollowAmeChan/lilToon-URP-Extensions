"""Qualify the raw Cast stage; explicitly retain the failed NPR gates."""
from pathlib import Path
import hashlib,json,re,subprocess
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent
BASE='338dd35f'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest().lower()
def main():
    accepted={};rejected=[]
    for path in sorted(HERE.glob('run-*.json')):
        run=read(path)
        for entry in run['entries']:
            assert digest(HERE/entry['logFile'])==entry['logSha256'].lower()
            if not entry['pass']:rejected.append({'run':path.name,'case':entry['name'],'exit':entry['actualExit'],'diagnostics':entry['diagnostics']})
        if run['activeProcess'] is not None:continue
        if any(digest(ROOT/s['path'])!=s['sha256'].lower() for s in read(HERE/run['sourcesFile'])):continue
        for entry in run['entries']:
            assert entry['pass'] and entry['actualExit']==0 and entry['marker'] and not entry['diagnostics'] and entry['abort'] is None
            accepted[entry['name']]=path.name
            if entry['name'].startswith('shadow-raw-'):
                report=read(HERE/f"report-{entry['name']}-{path.stem[4:]}.json")
                assert report['pass'] and report['rawOnly'] and report['requests']==19
                assert len(report['checks'])==52 and report['fractionalPixels']==1244 and report['distinctLevels']==199
                assert 'Deferred consumer resources released after completion' in report['checks']
                log=(HERE/entry['logFile']).read_text(encoding='utf-8-sig')
                assert 'PASS scope=raw-only; NPR acceptance pending' in log
    assert set(accepted)=={'shadow-raw-rg','shadow-raw-compat','primary-rg','primary-compat','host-retirement-rg','host-retirement-compat','gi-host-rg','di-cutout-rg'}
    assert any(x['case']=='shadow-rg' for x in rejected) and any(x['case']=='raster-fallback' for x in rejected)
    paths=subprocess.check_output(['git','ls-tree','-r','--name-only',BASE,'Runtime/RTDI','Runtime/RTGI','Runtime/CharacterShadow'],cwd=ROOT).decode().splitlines()
    unchanged=[]
    # One batched Git read, retaining every original effect algorithm.
    blob=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=''.join(f'{BASE}:{p}\n' for p in paths).encode(),stdout=subprocess.PIPE,check=True).stdout
    cursor=0
    for name in paths:
        end=blob.index(b'\n',cursor);size=int(blob[cursor:end].split()[2]);cursor=end+1
        before=blob[cursor:cursor+size];cursor+=size+1
        assert before.replace(b'\r\n',b'\n')==(ROOT/name).read_bytes().replace(b'\r\n',b'\n'),name
        unchanged.append(name)
    compilation=(HERE/'compile.log').read_text(encoding='utf-8-sig')
    assert 'ERRORS: 0' in compilation and not re.search(r'error CS\d+',compilation)
    review={'stage':'R4 raw Cast only; R4 incomplete','accepted':accepted,'retainedFailures':rejected,
            'referenceCommit':BASE,'unchangedDiGiCharacterShadowFiles':len(unchanged),
            'benefit':'Independent host-backed full-resolution 32F main directional Cast, explicit raw visibility/status/owner/blocker distance, OB part selection, real finite-cone sampling, fixed-seed no-history mode and fenced consumer resources.',
            'evidence':'Two modes each 52 checks / 19 requests, 1244 fractional pixels and 199 distinct raw levels at 256 spp. Alpha hole selects the far actual blocker; miss sentinel, unsupported domain/linking, missing host/SB, resize, feature toggle and mid-frame Create covered. Primary and host retirement regression pairs plus production DI/GI and DI cutout pass.',
            'cost':'16 B/pixel raw plus 8 KiB receiver mask per renderer/camera, distinct shadow-purpose AS view. Samples grow tracing cost; no frame-time claim. Closest of multiple sampled blocker distances is reference/debug data, never directly SIGMA input.',
            'nprGate':'Rejected: actual lilToon shadow/receive/lit samples all stayed 0.001041412 in isolated test; current raster-only existing validation also failed 0/0. Cause unproven; no assertion of a pre-existing baseline defect. Diagnostic shader imported only in private project was removed. Main Cast hook reverted; existing CharacterShadow bytes/algorithm retained. Sampling helper is preparatory and not a qualified NPR integration.',
            'next':'R4 NPR gate remains open. Proceed with the independent matching-version SIGMA fixture and production signal while retaining this gate, then complete actual material/ramp reception. No R4-R6 or final quality completion claim.'}
    (HERE/'review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'R4 raw verified: 8 cases; {len(unchanged)} DI/GI/CharacterShadow files unchanged; NPR explicitly rejected.')
if __name__=='__main__':main()
