"""Generate GI-only diagnostic shaders; preserve SDK/production bytes."""
from pathlib import Path
import json,hashlib,re,difflib
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent;DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoRTGISelectionLineage';DEST.mkdir(exist_ok=True)
files={'Runtime/RTGI/Shaders/HoRTGI.compute':'Lineage.compute','Runtime/RTGI/Shaders/HoRTGI.raytrace':'Lineage.raytrace','Runtime/RTGI/Shaders/HoRTGIResampling.hlsl':'LineageResampling.hlsl','Runtime/RTGI/Shaders/HoRTGIShadingCommon.hlsl':'LineageShading.hlsl','Runtime/RTDI/ThirdParty/RTXDI/GI/Reservoir.hlsl':'LineageReservoir.hlsl'}
def once(s,a,b):
 assert s.count(a)==1,(a,s.count(a));return s.replace(a,b)
records=[]
for rel,name in files.items():
 p=ROOT/rel;original=p.read_text(encoding='utf-8-sig').replace('\r\n','\n');s=original
 if name=='LineageReservoir.hlsl':
  s=once(s,'    uint age;','    uint age;\n    uint hoSourcePointer;')
  s=once(s,'    return res;','    res.hoSourcePointer = 0xffffffffu;\n    return res;')
  for a in ['return RTXDI_UnpackGIReservoir(RTXDI_GI_RESERVOIR_BUFFER[pointer]);','return RTXDI_UnpackGIReservoir(RTXDI_GI_RESERVOIR_BUFFER[pointer], miscFlags);']:
   expression=a[len('return '):-1];s=once(s,a,'RTXDI_GIReservoir hoResult = '+expression+'; hoResult.hoSourcePointer=pointer; return hoResult;')
  s=once(s,'    s.age = 0;','    s.age = 0;\n    s.hoSourcePointer = 0xffffffffu;')
  s=once(s,'    reservoir.age = 0;','    reservoir.age = 0;\n    reservoir.hoSourcePointer = 0xffffffffu;')
  s=once(s,'        reservoir.age = newReservoir.age;','        reservoir.age = newReservoir.age;\n        reservoir.hoSourcePointer = newReservoir.hoSourcePointer;')
  decl='''RWStructuredBuffer<uint4> _HoLRecords;
#define HoLMeta(pointer) _HoLRecords[(pointer)*2]
#define HoLOrigin(pointer) _HoLRecords[(pointer)*2+1]
void HoLStore(uint source, RTXDI_ReservoirBufferParameters layout, uint2 pixel, uint arrayIndex)
{
    uint target=RTXDI_ReservoirPositionToPointer(layout,pixel,arrayIndex);
    HoLMeta(target)=source<3*layout.reservoirArrayPitch?HoLMeta(source):0;
    HoLOrigin(target)=source<3*layout.reservoirArrayPitch?HoLOrigin(source):0;
}
'''
  token='// Converts a GIReservoir into its packed form.';s=once(s,token,decl+'\n'+token)
  token='    RTXDI_StorePackedGIReservoir(\n        RTXDI_PackGIReservoir(reservoir, 0),';s=once(s,token,'    HoLStore(reservoir.hoSourcePointer,reservoirParams,reservoirPosition,reservoirArrayIndex);\n'+token)
  token='    RTXDI_StorePackedGIReservoir(\n        RTXDI_PackGIReservoir(reservoir, miscFlags),';s=once(s,token,'    HoLStore(reservoir.hoSourcePointer,reservoirParams,reservoirPosition,reservoirArrayIndex);\n'+token)
 if name=='LineageShading.hlsl':
  s=once(s,'    RTXDI_GIReservoir reservoir=RTXDI_EmptyGIReservoir();','    RTXDI_GIReservoir reservoir=RTXDI_EmptyGIReservoir();\n    uint4 hoMeta=uint4(0,data.flags,0,0);')
  s=once(s,'                // Reconstruct the stored choice before visibility;', '                hoMeta=uint4(lightReservoir.lightData,lightReservoir.uvData,asuint(RTXDI_GetDIReservoirInvPdf(lightReservoir)),RTXDI_EncodeNormalizedVectorToSnorm2x16(surface.viewDir));\n                // Reconstruct the stored choice before visibility;')
  s=once(s,'    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,1);','''    uint hoPointer=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,1);
    HoLMeta(hoPointer)=hoMeta;
    HoLOrigin(hoPointer)=data.flags!=0?uint4((uint)_HoRTGIFrameIndex,index,data.flags,((uint)_HoRTGIFrameIndex+1)*(uint)(_HoRTGIFrame.x*_HoRTGIFrame.y)+index+1):0;
    reservoir.hoSourcePointer=hoPointer;
    RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,1);''')
 if name=='Lineage.compute':
  s=once(s,'    RTXDI_GIReservoir reservoir=RTXDI_LoadGIReservoir(HoGILayout(),pixel,1);','''    RTXDI_GIReservoir reservoir=RTXDI_LoadGIReservoir(HoGILayout(),pixel,1);
    uint hoPixel=pixel.y*(uint)_HoRTGIFrame.x+pixel.x;uint hoPointer=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,1);
    RTXDI_PackedGIReservoir hoActual=_HoRTGIReservoirs[hoPointer];uint hoLast=6*HoGILayout().reservoirArrayPitch+hoPixel*4;
    _HoLRecords[hoLast]=uint4(asuint(hoActual.position),hoActual.packed_miscData_age_M);_HoLRecords[hoLast+1]=uint4(hoActual.packed_radiance,asuint(hoActual.weight),hoActual.packed_normal,asuint(hoActual.unused));
    _HoLRecords[hoLast+2]=HoLMeta(hoPointer);_HoLRecords[hoLast+3]=HoLOrigin(hoPointer);''')
  s=once(s,'''    if(_HoRTGIFlags.y>.5)
        _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0)]=
            _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,2)];''','''    if(_HoRTGIFlags.y>.5) {
        _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0)]=
            _HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,2)];
        uint hoHistory=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0),hoTemporal=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,2);
        HoLMeta(hoHistory)=HoLMeta(hoTemporal);HoLOrigin(hoHistory)=HoLOrigin(hoTemporal);
    }''')
 patch=''.join(difflib.unified_diff(original.splitlines(True),s.splitlines(True),fromfile=rel,tofile=name));(HERE/(name+'.patch')).write_text(patch,encoding='utf-8',newline='\n')
 def include(m):
  inc=m[1];target=(p.parent/inc).resolve() if not inc.startswith('Packages/') else None
  if target and target.is_relative_to(ROOT):
   path=target.relative_to(ROOT).as_posix();replacement=files.get(path,'Packages/jp.lilxyzw.liltoon.urp.extensions/'+path)
  else:replacement=inc
  return '#include "'+replacement+'"'
 s=re.sub(r'#include "([^"]+)"',include,s);(DEST/name).write_text(s,encoding='utf-8',newline='\n')
 records.append({'original':rel,'originalRawSha256':hashlib.sha256(p.read_bytes()).hexdigest().upper(),'generated':'Assets/HoRTGISelectionLineage/'+name,'generatedSha256':hashlib.sha256(s.encode()).hexdigest().upper()})
(HERE/'generated-sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print('Generated five private lineage sources with exact anchored diffs; production/vendor unchanged')
