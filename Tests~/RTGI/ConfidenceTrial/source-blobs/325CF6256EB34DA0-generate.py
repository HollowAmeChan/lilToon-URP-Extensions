from pathlib import Path
import runpy,hashlib,json
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent;DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoRTGISelectionLineage'
manifests=[ROOT/'Tests~/RTGI'/name/'generated-sources.json' for name in ['SelectionLineage','SecondaryReplay','EndpointReplay']]
saved={p:p.read_bytes() for p in manifests}
try:
 runpy.run_path(str(ROOT/'Tests~/RTGI/EndpointReplay/generate.py'),run_name='__main__')
finally:
 for p,b in saved.items():p.write_bytes(b)
(DEST/'EndpointConfidence.compute').write_text((HERE/'EndpointConfidence.compute').read_text(encoding='utf-8'),encoding='utf-8',newline='\n')
records=[{'path':p.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()} for p in DEST.iterdir() if p.suffix!='.meta'];(HERE/'generated-sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
