using System;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool pairedSecondAuxiliary;
        private static bool secondAuxiliaryRayAudit;
        public static void RunSecondAuxiliaryDomain(){pairedSecondAuxiliary=true;RunPairedPowerCDFDomain();}
        public static void RunSecondAuxiliaryDomainCompatibility(){compatibility=true;RunSecondAuxiliaryDomain();}
        public static void RunSecondAuxiliaryRayAudit(){secondAuxiliaryRayAudit=true;RunSecondAuxiliaryDomain();}
        private static (string compute,string ray) AddSecondAuxiliary(string compute,string ray)
        {
            Require(pairedAuxiliaryPowerCDF,"Second auxiliary requires the reviewed CDF prototype");
            compute=compute.Replace("\r\n","\n");ray=ray.Replace("\r\n","\n");
            compute=ReplaceOnce(compute,"void HoDIShade(uint3 id,out uint peak)","StructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDISecondAuxiliary;\nvoid HoDIShade(uint3 id,out uint peak)");
            compute=compute.Replace("_HoRTDIFinalVisibility.Load(int3(id.xy,0))*3","_HoRTDIFinalVisibility.Load(int3(id.xy,0))*7");
            compute=ReplaceOnce(compute,"diffuse=.5*(diffuse+extraD);specular=.5*(specular+extraS);diffuseLighting=.5*(diffuseLighting+extraLighting);",@"
        RTXDI_DIReservoir second=RTXDI_UnpackDIReservoir(_HoRTDISecondAuxiliary[id.y*(uint)_HoRTDIFrame.x+id.x]);
        float3 secondD=0,secondS=0,secondLighting=0;
        if(RTXDI_IsValidDIReservoir(second))
        {
            RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(RTXDI_GetDIReservoirLightIndex(second),false),surface,RTXDI_GetDIReservoirSampleUV(second));
            uint bits=(uint)round(_HoRTDIFinalVisibility.Load(int3(id.xy,0))*7);
            float3 radiance=sample.radiance*((bits>>2)&1)*RTXDI_GetDIReservoirInvPdf(second)/max(sample.solidAnglePdf,1e-12);
            float3 L;float distance;RAB_GetLightDirDistance(surface,sample,L,distance);
            if(dot(L,surface.geoNormal)>0)
            {
                secondLighting=Lambert(surface.normal,-L)*radiance;secondD=secondLighting*surface.material.diffuseAlbedo;
                if(surface.material.roughness>=.03)secondS=GGX_times_NdotL(surface.viewDir,L,surface.normal,max(.03,surface.material.roughness),surface.material.specularF0)*radiance;
            }
        }
        // Fixed mixture: half of the original estimator plus half of the
        // average of two fresh CDF estimators. No sample-dependent weights.
        diffuse=.5*diffuse+.25*(extraD+secondD);specular=.5*specular+.25*(extraS+secondS);diffuseLighting=.5*diffuseLighting+.25*(extraLighting+secondLighting);");
            ray=ReplaceOnce(ray,"StructuredBuffer<float> _HoRTDIAuxiliaryCDF;","StructuredBuffer<float> _HoRTDIAuxiliaryCDF;\nRWStructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDISecondAuxiliary;");
            ray=ReplaceOnce(ray,"RTXDI_DIReservoir extra=RTXDI_EmptyDIReservoir();uint bits=0;","RTXDI_DIReservoir extra=RTXDI_EmptyDIReservoir(),second=RTXDI_EmptyDIReservoir();uint bits=0;");
            ray=ReplaceOnce(ray,"        if(HoDITraceFinal(surface,sample))bits|=2;",@"        if(HoDITraceFinal(surface,sample))bits|=2;
        uint lo2=0,hi2=count;float u2=RTXDI_GetNextRandom(rng);
        while(lo2<hi2){uint middle=(lo2+hi2)/2;if(_HoRTDIAuxiliaryCDF[middle]<=u2)lo2=middle+1;else hi2=middle;}
        uint index2=min(lo2,count-1);float pdf2=_HoRTDIAuxiliaryCDF[index2]-(index2>0?_HoRTDIAuxiliaryCDF[index2-1]:0);
        RTXDI_StreamSample(second,index2,float2(.5,.5),0,1,1/pdf2);RTXDI_FinalizeResampling(second,1,1);
        RAB_LightSample sample2=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(index2,false),surface,float2(.5,.5));
        if(HoDITraceFinal(surface,sample2))bits|=4;");
            ray=ReplaceOnce(ray,"    _HoRTDIFinalVisibility[pixel]=bits/3.0;","    _HoRTDISecondAuxiliary[pixel.y*(uint)_HoRTDIFrame.x+pixel.x]=RTXDI_PackDIReservoir(second);\n    _HoRTDIFinalVisibility[pixel]=bits/7.0;");
            if(secondAuxiliaryRayAudit)
            {
                ray=ReplaceOnce(ray,"int _HoRTDIExtraSampleFrame;",@"RWStructuredBuffer<uint> _HoRTDIValidationRayCounts;
bool HoDITraceFinalCounted(RAB_Surface surface,RAB_LightSample sample,uint category)
{
    float3 direction;float distance;RAB_GetLightDirDistance(surface,sample,direction,distance);
    if(distance<=_HoRTDISettings.w*2||!all(isfinite(direction)))return false;
    InterlockedAdd(_HoRTDIValidationRayCounts[category],1);return HoDITraceFinal(surface,sample);
}
int _HoRTDIExtraSampleFrame;");
                ray=ReplaceOnce(ray,"bits=HoDITraceFinal(surface,sample)?1:0;","bits=HoDITraceFinalCounted(surface,sample,0)?1:0;");
                ray=ReplaceOnce(ray,"if(HoDITraceFinal(surface,sample))bits|=2;","if(HoDITraceFinalCounted(surface,sample,1))bits|=2;");
                ray=ReplaceOnce(ray,"if(HoDITraceFinal(surface,sample2))bits|=4;","if(HoDITraceFinalCounted(surface,sample2,2))bits|=4;");
            }
            return(compute,ray);
        }
    }
}
