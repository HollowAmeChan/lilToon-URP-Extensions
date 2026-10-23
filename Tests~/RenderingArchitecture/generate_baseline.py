from pathlib import Path
import json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoNrdArchitecture';DEST.mkdir(exist_ok=True)
for name in ['Context','Native','Timing','Manifest']:
 s=(HERE/'baseline'/('HoRTDINrd'+name+'.cs')).read_text(encoding='utf-8');s=s.replace('namespace lilToon.URP.Extensions.RTDI.NRD','namespace lilToon.URP.Extensions.Editor.NrdArchitecture.Legacy')
 for kind in ['Context','Native','Timing','Manifest']:s=s.replace('HoRTDINrd'+kind,'HoLegacyNrd'+kind)
 (DEST/('HoLegacyNrd'+name+'.cs')).write_text(s,encoding='utf-8',newline='\n')
(DEST/'Architecture.asmref').write_text(json.dumps({'reference':'jp.lilxyzw.liltoon.urp.extensions.Editor'})+'\n',encoding='utf-8')
for name in ['ArchitectureValidation.cs','ArchitectureFill.compute','CombinedValidation.cs']:(DEST/name).write_bytes((HERE/name).read_bytes())
print('Generated frozen baseline: type names only, shader ABI strings untouched')
