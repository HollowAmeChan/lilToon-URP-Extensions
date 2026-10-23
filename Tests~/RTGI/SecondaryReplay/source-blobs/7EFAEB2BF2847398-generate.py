"""Enrich only the qualified64²private lineage path for real static TLAS replay."""
from pathlib import Path
import runpy,hashlib,json
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent;DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoRTGISelectionLineage'
runpy.run_path(str(ROOT/'Tests~/RTGI/SelectionLineage/generate.py'),run_name='__main__')
def once(s,a,b):
 assert s.count(a)==1,(a,s.count(a));return s.replace(a,b)
p=DEST/'LineageReservoir.hlsl';s=p.read_text(encoding='utf-8-sig').replace('_HoLRecords[(pointer)*2]','_HoLRecords[(pointer)*6]').replace('_HoLRecords[(pointer)*2+1]','_HoLRecords[(pointer)*6+1]')
s=once(s,'''    HoLMeta(target)=source<3*layout.reservoirArrayPitch?HoLMeta(source):0;
    HoLOrigin(target)=source<3*layout.reservoirArrayPitch?HoLOrigin(source):0;''','''    [unroll]for(uint hoWord=0;hoWord<6;hoWord++)_HoLRecords[target*6+hoWord]=source<3*layout.reservoirArrayPitch?_HoLRecords[source*6+hoWord]:0;''');p.write_text(s,encoding='utf-8',newline='\n')
p=DEST/'LineageShading.hlsl';s=p.read_text(encoding='utf-8-sig');
helpers='''
struct HoReplayEvaluation {float3 radiance,sampleRadiance,direction,brdf;float pdf,distance;uint visible;};
HoReplayEvaluation HoReplayEvaluate(HoRTGISecondaryData data,uint4 meta,float3 view)
{
    HoReplayEvaluation result=(HoReplayEvaluation)0;result.radiance=data.emission;
    if(data.flags==1&&(meta.x&0x80000000u)!=0){
        RAB_Surface surface=RAB_EmptySurface();surface.worldPos=data.positionWS;surface.viewDepth=1;
        surface.normal=HoGIUnpackNormal(data.normal);surface.geoNormal=HoGIUnpackNormal(data.geometricNormal);surface.viewDir=view;
        surface.material.diffuseAlbedo=HoDIUnpack(data.diffuseAlbedo).rgb;float4 specular=HoDIUnpack(data.specularRoughness);surface.material.specularF0=specular.rgb;surface.material.roughness=specular.a;
        RAB_LightSample chosen=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(meta.x&0x7fffffffu,false),surface,float2(meta.y&65535,meta.y>>16)/65535.0);
        result.sampleRadiance=chosen.radiance;result.pdf=chosen.solidAnglePdf;RAB_GetLightDirDistance(surface,chosen,result.direction,result.distance);
        result.brdf=RAB_SurfaceEvaluateBrdfTimesNoL(surface,result.direction);result.visible=RAB_GetConservativeVisibility(surface,chosen)?1:0;
        if(result.visible!=0)result.radiance+=result.brdf*chosen.radiance*asfloat(meta.z)/max(chosen.solidAnglePdf,1e-12);
    }return result;
}
'''
s=s.rsplit('#endif',1)[0]+helpers+'\n#endif\n';p.write_text(s,encoding='utf-8',newline='\n')
p=DEST/'Lineage.compute';s=p.read_text(encoding='utf-8-sig').replace('6*HoGILayout().reservoirArrayPitch+hoPixel*4','18*HoGILayout().reservoirArrayPitch+hoPixel*8');s=once(s,'    _HoLRecords[hoLast+2]=HoLMeta(hoPointer);_HoLRecords[hoLast+3]=HoLOrigin(hoPointer);','    _HoLRecords[hoLast+2]=HoLMeta(hoPointer);_HoLRecords[hoLast+3]=HoLOrigin(hoPointer);\n    [unroll]for(uint hoWord=2;hoWord<6;hoWord++)_HoLRecords[hoLast+hoWord+2]=_HoLRecords[hoPointer*6+hoWord];')
s=once(s,'        HoLMeta(hoHistory)=HoLMeta(hoTemporal);HoLOrigin(hoHistory)=HoLOrigin(hoTemporal);','        [unroll]for(uint hoWord=0;hoWord<6;hoWord++)_HoLRecords[hoHistory*6+hoWord]=_HoLRecords[hoTemporal*6+hoWord];');p.write_text(s,encoding='utf-8',newline='\n')
p=DEST/'Lineage.raytrace';s=p.read_text(encoding='utf-8-sig');s+='''
[shader("raygeneration")]
void CaptureReplaySource()
{
    uint2 pixel=DispatchRaysIndex().xy;uint id=pixel.y*(uint)_HoRTGIFrame.x+pixel.x;uint pointer=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,1);HoRTGISecondaryData data=_HoRTGISecondary[id];float3 view=0;
    if(data.flags==1&&(_HoRTGIFrame.z>0||_HoRTGIEnvironment.z>.5))view=normalize(RAB_GetGBufferSurface(pixel,false).worldPos-data.positionWS);
    _HoLRecords[pointer*6+2]=uint4(asuint(data.positionWS),data.normal);_HoLRecords[pointer*6+3]=uint4(data.diffuseAlbedo,data.specularRoughness,asuint(data.emission.xy));
    _HoLRecords[pointer*6+4]=uint4(asuint(data.emission.z),asuint(data.pdf),data.geometricNormal,data.flags);_HoLRecords[pointer*6+5]=asuint(float4(view,0));
}
[shader("raygeneration")]
void ReplaySelected()
{
    uint2 pixel=DispatchRaysIndex().xy;uint id=pixel.y*(uint)_HoRTGIFrame.x+pixel.x;uint pointer=RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,1);
    uint4 a=_HoLRecords[pointer*6+2],b=_HoLRecords[pointer*6+3],c=_HoLRecords[pointer*6+4],m=HoLMeta(pointer);
    HoRTGISecondaryData data;data.positionWS=asfloat(a.xyz);data.normal=a.w;data.diffuseAlbedo=b.x;data.specularRoughness=b.y;data.emission=asfloat(uint3(b.zw,c.x));data.pdf=asfloat(c.y);data.geometricNormal=c.z;data.flags=c.w;
    float3 exactView=asfloat(_HoLRecords[pointer*6+5].xyz);float3 packedView=RTXDI_DecodeNormalizedVectorFromSnorm2x16(m.w);
    HoReplayEvaluation exact=HoReplayEvaluate(data,m,exactView),quant=HoReplayEvaluate(data,m,packedView);
    RTXDI_GIReservoir selected=RTXDI_LoadGIReservoir(HoGILayout(),pixel,1);RAB_Surface primary=RAB_GetGBufferSurface(pixel,false);float primaryVisible=0;
    if(RAB_IsSurfaceValid(primary)&&RTXDI_IsValidGIReservoir(selected)){
        float3 delta=selected.position-primary.worldPos;float distance=length(delta);float3 direction=delta/max(distance,1e-12);
        if(distance>_HoRTGISettings.w*2&&dot(direction,primary.geoNormal)>0&&dot(direction,primary.normal)>0)primaryVisible=HoRTTraceScene(primary.worldPos,direction,_HoRTGISettings.w,distance-_HoRTGISettings.w,255).status==0?1:0;
    }
    uint start=18*HoGILayout().reservoirArrayPitch+(uint)(_HoRTGIFrame.x*_HoRTGIFrame.y)*8+id*8;
    _HoLRecords[start]=uint4(_HoRTGIReservoirs[pointer].packed_radiance,RTXDI_EncodeRGBToLogLuv(exact.radiance),RTXDI_EncodeRGBToLogLuv(quant.radiance),m.x);
    _HoLRecords[start+1]=asuint(float4(exact.radiance,exact.visible));_HoLRecords[start+2]=asuint(float4(quant.radiance,quant.visible));
    _HoLRecords[start+3]=asuint(float4(exact.sampleRadiance,exact.pdf));_HoLRecords[start+4]=asuint(float4(exact.direction,exact.distance));
    _HoLRecords[start+5]=asuint(float4(exact.brdf,asfloat(m.z)));_HoLRecords[start+6]=asuint(float4(primaryVisible,_HoRTGIFinalVisibility[id].x,data.flags,(m.x&0x80000000u)!=0?1:0));
    _HoLRecords[start+7]=asuint(float4(exactView,0));
}
''';p.write_text(s,encoding='utf-8',newline='\n')
s=(ROOT/'Editor/RTGI/Shaders/HoRTGISelectionCapture.compute').read_text(encoding='utf-8-sig').replace('6*_ArrayPitch+pixelIndex*4','18*_ArrayPitch+pixelIndex*8').replace('_Records[address*2]','_Records[address*6]').replace('_Records[address*2+1]','_Records[address*6+1]');(DEST/'ReplayCapture.compute').write_text(s,encoding='utf-8',newline='\n')
records=[{'path':p.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()} for p in DEST.iterdir() if p.suffix!='.meta' and p.name!='LineageTemporal.hlsl']
(HERE/'generated-sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8');print('Generated enriched actual-source static replay; original Runtime/vendor untouched')
