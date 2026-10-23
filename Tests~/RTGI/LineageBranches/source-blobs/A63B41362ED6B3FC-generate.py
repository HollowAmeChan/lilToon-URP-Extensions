"""Add branch witnesses to the qualified private selection-lineage shaders."""
from pathlib import Path
import runpy,json,hashlib,difflib
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent;DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoRTGISelectionLineage'
runpy.run_path(str(ROOT/'Tests~/RTGI/SelectionLineage/generate.py'),run_name='__main__')
def once(s,a,b):
 assert s.count(a)==1,(a,s.count(a));return s.replace(a,b)
p=DEST/'LineageReservoir.hlsl';s=p.read_text(encoding='utf-8-sig');token='// Converts a GIReservoir into its packed form.'
extra='''uint HoLEventAddress(RTXDI_ReservoirBufferParameters p,uint2 pixel){return 6*p.reservoirArrayPitch+4*(uint)(_HoRTGIFrame.x*_HoRTGIFrame.y)+pixel.y*(uint)_HoRTGIFrame.x+pixel.x;}
void HoLMark(RTXDI_ReservoirBufferParameters p,uint2 pixel,uint flag){uint index=HoLEventAddress(p,pixel);_HoLRecords[index].x|=flag;}
void HoLTemporalState(RTXDI_ReservoirBufferParameters p,uint2 pixel,uint age,uint limit,float jacobian){uint index=HoLEventAddress(p,pixel);_HoLRecords[index].y=age;_HoLRecords[index].z=limit;_HoLRecords[index].w=asuint(jacobian);}
'''
s=once(s,token,extra+'\n'+token);p.write_text(s,encoding='utf-8',newline='\n')
p=DEST/'LineageResampling.hlsl';s=p.read_text(encoding='utf-8-sig');s=s.replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/RTXDI/GI/TemporalResampling.hlsl','LineageTemporal.hlsl')
s=once(s,'    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);','    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);\n    _HoLRecords[HoLEventAddress(HoGILayout(),pixel)]=uint4(1,0,0,0);\n    if(_HoRTGIFlags.x<=.5)HoLMark(HoGILayout(),pixel,16384);if(!RAB_IsSurfaceValid(surface))HoLMark(HoGILayout(),pixel,32768);') if s.count('    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);')==1 else s
# Same surface declaration occurs for T and S; change T only by splitting at spatial function.
t,sp=s.split('void HoGISpatial',1)
if '_HoLRecords[HoLEventAddress' not in t:t=once(t,'    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);','    RAB_Surface surface=RAB_GetGBufferSurface(pixel,false);\n    _HoLRecords[HoLEventAddress(HoGILayout(),pixel)]=uint4(1,0,0,0);\n    if(_HoRTGIFlags.x<=.5)HoLMark(HoGILayout(),pixel,16384);if(!RAB_IsSurfaceValid(surface))HoLMark(HoGILayout(),pixel,32768);')
t=once(t,'    if(_HoRTGIFlags.x>.5&&RAB_IsSurfaceValid(surface))\n    {','    if(_HoRTGIFlags.x>.5&&RAB_IsSurfaceValid(surface))\n    {\n        HoLMark(HoGILayout(),pixel,2);')
t=once(t,'        if(all(isfinite(clip))&&clip.w>1e-6&&previousDepth>0)\n        {','        if(all(isfinite(clip))&&clip.w>1e-6&&previousDepth>0)\n        {\n            HoLMark(HoGILayout(),pixel,4);')
t=once(t,'p);\n        }\n    }\n    RTXDI_StoreGIReservoir','p);\n        }else HoLMark(HoGILayout(),pixel,65536);\n    }\n    RTXDI_StoreGIReservoir');s=t+'void HoGISpatial'+sp;p.write_text(s,encoding='utf-8',newline='\n')
rel='Runtime/RTDI/ThirdParty/RTXDI/GI/TemporalResampling.hlsl';original=(ROOT/rel).read_text(encoding='utf-8-sig');s=original
s=once(s,'    // Backproject this pixel to last frame','    HoLMark(reservoirParams,pixelPosition,8);\n    // Backproject this pixel to last frame')
for token,flag in [('if (!RAB_IsSurfaceValid(temporalSurface))',16),('if (!RAB_AreMaterialsSimilar(RAB_GetMaterial(surface), RAB_GetMaterial(temporalSurface)))',64),('if (!RTXDI_IsValidGIReservoir(temporalReservoir))',128)]:
 anchor=token+'\n        {\n            continue;';s=once(s,anchor,token+'\n        {\n            HoLMark(reservoirParams,pixelPosition,'+str(flag)+');\n            continue;')
s=once(s,'            tparams.normalThreshold, tparams.depthThreshold))\n        {\n            continue;','            tparams.normalThreshold, tparams.depthThreshold))\n        {\n            HoLMark(reservoirParams,pixelPosition,32);\n            continue;')
s=once(s,'        foundTemporalReservoir = true;','        foundTemporalReservoir = true;\n        HoLMark(reservoirParams,pixelPosition,256);')
s=once(s,'    RTXDI_GIReservoir curReservoir = RTXDI_EmptyGIReservoir();','    if(!foundTemporalReservoir)HoLMark(reservoirParams,pixelPosition,8192);\n    RTXDI_GIReservoir curReservoir = RTXDI_EmptyGIReservoir();')
s=once(s,'        if (!RAB_ValidateGISampleWithJacobian(jacobian))\n            foundTemporalReservoir = false;','        HoLTemporalState(reservoirParams,pixelPosition,temporalReservoir.age,tparams.maxReservoirAge,jacobian);\n        if (!RAB_ValidateGISampleWithJacobian(jacobian)){foundTemporalReservoir = false;HoLMark(reservoirParams,pixelPosition,512);}')
s=once(s,'        if (temporalReservoir.age > tparams.maxReservoirAge)\n            foundTemporalReservoir = false;','        if (temporalReservoir.age > tparams.maxReservoirAge){foundTemporalReservoir = false;HoLMark(reservoirParams,pixelPosition,1024);}')
s=once(s,'        if (selectedPreviousSample)\n        {','        if (selectedPreviousSample)\n        {\n            HoLMark(reservoirParams,pixelPosition,2048);')
s=once(s,'    return curReservoir;','    if(!selectedPreviousSample)HoLMark(reservoirParams,pixelPosition,4096);\n    return curReservoir;')
s=s.replace('Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/ThirdParty/RTXDI/GI/Reservoir.hlsl','LineageReservoir.hlsl');(DEST/'LineageTemporal.hlsl').write_text(s,encoding='utf-8',newline='\n')
(HERE/'temporal-events.patch').write_text(''.join(difflib.unified_diff(original.splitlines(True),s.splitlines(True),fromfile=rel,tofile='LineageTemporal.hlsl')),encoding='utf-8',newline='\n')
records=[]
for p in DEST.iterdir():
 if p.suffix=='.meta':continue
 records.append({'path':p.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()})
(HERE/'generated-sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print('Generated private branch event witnesses without changing production temporal options')
