from pathlib import Path
import json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoNrdReuse';DEST.mkdir(exist_ok=True)
s=(HERE/'baseline/HoRTGINrdContext.cs').read_text(encoding='utf-8');s=s.replace('namespace lilToon.URP.Extensions.RTGI.NRD','namespace lilToon.URP.Extensions.Editor.NrdReuse.Legacy').replace('HoRTGINrdContext','HoLegacyGINrdContext');s='using lilToon.URP.Extensions.RTGI.NRD;\n'+s
(DEST/'HoLegacyGINrdContext.cs').write_text(s,encoding='utf-8',newline='\n');(DEST/'Reuse.asmref').write_text(json.dumps({'reference':'jp.lilxyzw.liltoon.urp.extensions.Editor'})+'\n',encoding='utf-8')
for name in ['ReuseValidation.cs','ReuseAudit.cs']:(DEST/name).write_bytes((HERE/name).read_bytes())
print('Generated frozen old separate signal/filtered allocation and current paired reuse fixture')
