"""Extend diagnostic static replay with previous actualShade endpoint queries."""
from pathlib import Path
import runpy,json,hashlib
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).resolve().parent;DEST=ROOT/'.codex-research/RTGIValidation/Assets/HoRTGISelectionLineage'
runpy.run_path(str(ROOT/'Tests~/RTGI/SecondaryReplay/generate.py'),run_name='__main__')
p=DEST/'Lineage.compute';s=p.read_text(encoding='utf-8-sig');token='    [unroll]for(uint hoWord=2;hoWord<6;hoWord++)_HoLRecords[hoLast+hoWord+2]=_HoLRecords[hoPointer*6+hoWord];';assert s.count(token)==1;s=s.replace(token,token+'\n    _HoLRecords[hoLast+7].w=asuint(_HoRTGIFinalVisibility[hoPixel].x);');p.write_text(s,encoding='utf-8',newline='\n')
p=DEST/'Lineage.raytrace';s=p.read_text(encoding='utf-8-sig');s+='''
[shader("raygeneration")]
void ReplayPreviousEndpoint()
{
    uint2 pixel=DispatchRaysIndex().xy;uint id=pixel.y*(uint)_HoRTGIFrame.x+pixel.x,n=(uint)(_HoRTGIFrame.x*_HoRTGIFrame.y);uint baseIndex=18*HoGILayout().reservoirArrayPitch+id*8;
    uint4 p0=_HoLRecords[baseIndex],p1=_HoLRecords[baseIndex+1],meta=_HoLRecords[baseIndex+2],origin=_HoLRecords[baseIndex+3];
    uint4 a=_HoLRecords[baseIndex+4],b=_HoLRecords[baseIndex+5],c=_HoLRecords[baseIndex+6],viewWord=_HoLRecords[baseIndex+7];
    float3 oldPosition=asfloat(p0.xyz),oldGeo=HoGIUnpackNormal(c.z),view=asfloat(viewWord.xyz);uint kind=c.w;
    float bias=_HoRTGISettings.w,epsilon=4*bias;float3 probeOrigin=oldPosition+oldGeo*epsilon,probeDirection=-oldGeo;
    HoRTGISecondarySurface hit=(HoRTGISecondarySurface)0;float3 matchedPosition=0;float positionMatch=0,normalDot=0,matched=0;
    if(kind==1&&(p0.w&255)!=0){RayDesc ray;ray.Origin=probeOrigin;ray.Direction=probeDirection;ray.TMin=bias;ray.TMax=8*bias;TraceRay(_HoRTSceneAccelerationStructure,0,255,0,1,0,ray,hit);
        if(hit.status==1){matchedPosition=probeOrigin+probeDirection*hit.distance;positionMatch=length(matchedPosition-oldPosition)<=2*bias?1:0;normalDot=dot(oldGeo,hit.geometricNormalWS);matched=positionMatch!=0&&normalDot>=.9?1:0;}
    }
    RAB_Surface primary=RAB_GetGBufferSurface(pixel,false);float openOld=0,openMatched=0;
    if(RAB_IsSurfaceValid(primary)&&kind==1){float3 delta=oldPosition-primary.worldPos;float d=length(delta);if(d>2*bias)openOld=HoRTTraceScene(primary.worldPos,delta/d,bias,d-bias,255).status==0?1:0;
        if(matched!=0){delta=matchedPosition-primary.worldPos;d=length(delta);if(d>2*bias)openMatched=HoRTTraceScene(primary.worldPos,delta/d,bias,d-bias,255).status==0?1:0;}
    }
    HoReplayEvaluation eval=(HoReplayEvaluation)0;
    if(matched!=0){HoRTGISecondaryData current=(HoRTGISecondaryData)0;current.positionWS=matchedPosition;current.normal=HoGIPackNormal(hit.normalWS);current.geometricNormal=HoGIPackNormal(hit.geometricNormalWS);current.flags=1;
        current.diffuseAlbedo=HoGIPackColor(float4(saturate(hit.baseColor)*(1-hit.metallic),1));current.specularRoughness=HoGIPackColor(float4(lerp(hit.reflectance.xxx,saturate(hit.baseColor),hit.metallic),hit.perceptualRoughness));current.emission=hit.emission;eval=HoReplayEvaluate(current,meta,view);
    }
    uint start=18*HoGILayout().reservoirArrayPitch+n*8+id*18;
    _HoLRecords[start]=uint4(p1.x,meta.x,meta.y,kind);_HoLRecords[start+1]=asuint(float4(oldPosition,p0.w&255));_HoLRecords[start+2]=asuint(float4(probeOrigin,epsilon));_HoLRecords[start+3]=asuint(float4(probeDirection,8*bias));
    _HoLRecords[start+4]=asuint(float4(hit.distance,hit.status,positionMatch,normalDot));_HoLRecords[start+5]=asuint(float4(matchedPosition,matched));_HoLRecords[start+6]=asuint(float4(hit.normalWS,hit.perceptualRoughness));
    _HoLRecords[start+7]=asuint(float4(hit.baseColor,hit.metallic));_HoLRecords[start+8]=asuint(float4(hit.emission,hit.reflectance));_HoLRecords[start+9]=asuint(float4(openOld,openMatched,eval.visible,asfloat(viewWord.w)));
    _HoLRecords[start+10]=asuint(float4(eval.direction,eval.distance));_HoLRecords[start+11]=asuint(float4(eval.radiance,eval.visible));_HoLRecords[start+12]=asuint(float4(RTXDI_DecodeLogLuvToRGB(p1.x),asfloat(meta.z)));
    _HoLRecords[start+13]=asuint(float4(view,origin.x));_HoLRecords[start+14]=asuint(float4(eval.sampleRadiance,eval.pdf));_HoLRecords[start+15]=uint4(origin.xyz,origin.w);
    _HoLRecords[start+16]=asuint(float4(primary.worldPos,bias));_HoLRecords[start+17]=asuint(float4(eval.brdf,asfloat(p1.y)));
}
''';p.write_text(s,encoding='utf-8',newline='\n')
records=[{'path':p.relative_to(ROOT).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()} for p in DEST.iterdir() if p.suffix!='.meta' and p.name!='LineageTemporal.hlsl'];(HERE/'generated-sources.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8');print('Generated previous actualShade current-TLAS full-material endpoint query')
