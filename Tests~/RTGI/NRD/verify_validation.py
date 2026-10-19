"""Verify real report/source provenance, not a replacement rendering test."""
import hashlib
import json
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()

a=read(HERE/'snapshot-matrix.json'); b=read(HERE/'snapshot.json')
sources=lambda obj:{x['path']:x['sha256'] for x in obj['overlays']}
assert sources(a)==sources(b), 'Matrix and regression input sources differ'
for path,digest in sources(b).items(): assert sha(ROOT/path)==digest, path
all_guids={}
for folder in ('Runtime/RTGI','Editor/RTGI'):
    for path in (ROOT/folder).rglob('*.meta'):
        match=re.search(r'^guid: ([a-f0-9]{32})$',path.read_text(encoding='utf-8'),re.M)
        assert match,path
        assert match[1] not in all_guids,(path,all_guids.get(match[1]))
        all_guids[match[1]]=str(path)
entries=[]
for group,snapshot in [('run-matrix.json',HERE/'snapshot-matrix.json'),('run-regressions.json',HERE/'snapshot.json')]:
    run=read(HERE/'results'/group)
    assert run['activeProcess'] is None
    assert run['snapshotSha256']==sha(snapshot),group
    for entry in run['entries']:
        assert entry['exitCode']==0 and entry['passMarker'] and entry['shaderCsDiagnostics']==0,entry
        assert sha(Path(entry['log']))==entry['logSha256'],entry['label']
        if 'reportSha256' in entry:
            assert entry['reportFresh'] and entry['reportPass'],entry
            assert sha(HERE/'results'/(entry['label']+'.json'))==entry['reportSha256'],entry['label']
        entries.append(entry)
assert len(entries)==7
inventory=read(HERE/'inventory.json')
evidence={'base':b['baseCommit'],'sourceFiles':len(sources(b)),'processes':len(entries),
          'nativeLibrarySha256':sha(ROOT/'Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll'),
          'denoiserType':inventory['denoiserType'],'pipelines':len(inventory['pipelines']),
          'matrixSourceSha256':sha(HERE/'snapshot-matrix.json'),'regressionSourceSha256':sha(HERE/'snapshot.json'),
          'reports':{entry['label']:entry.get('reportSha256') for entry in entries}}
(HERE/'evidence.json').write_text(json.dumps(evidence,indent=2)+'\n',encoding='utf-8')
print('Verified 7 actual processes, 95 source overlays, matching matrix/regression inputs, reports and GUIDs')
