namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool pairedOpaqueAudit;
        public static void RunOpaqueAuxiliaryDomain(){pairedOpaqueAudit=true;RunSecondAuxiliaryDomain();}
        public static void RunOpaqueAuxiliaryDomainCompatibility(){compatibility=true;RunOpaqueAuxiliaryDomain();}
        private static string AddOpaqueAuxiliary(string compute)
        {
            compute=ReplaceOnce(compute,"StructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDISecondAuxiliary;","RWStructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDISecondAuxiliary;\nStructuredBuffer<float> _HoRTDIAuxiliaryCDF;");
            compute=ReplaceOnce(compute,"    if(_HoRTDIFinalVisibilityEnabled!=0&&RAB_IsSurfaceValid(surface))",@"
    if(_HoRTDIFinalVisibilityEnabled==0&&RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)
    {
        RTXDI_RandomSamplerState rng=RTXDI_InitRandomSampler(id.xy,(uint)_HoRTDIFrameIndex,0x6f527bbdu);
        for(uint draw=0;draw<2;draw++)
        {
            uint count=(uint)_HoRTDIFrame.z,lo=0,hi=count;float u=RTXDI_GetNextRandom(rng);
            while(lo<hi){uint middle=(lo+hi)/2;if(_HoRTDIAuxiliaryCDF[middle]<=u)lo=middle+1;else hi=middle;}
            uint index=min(lo,count-1);float pdf=_HoRTDIAuxiliaryCDF[index]-(index>0?_HoRTDIAuxiliaryCDF[index-1]:0);
            RTXDI_DIReservoir auxiliary=RTXDI_EmptyDIReservoir();RTXDI_StreamSample(auxiliary,index,float2(.5,.5),0,1,1/pdf);RTXDI_FinalizeResampling(auxiliary,1,1);
            RAB_LightSample sample=RAB_SamplePolymorphicLight(RAB_LoadLightInfo(index,false),surface,float2(.5,.5));
            float visibility=RAB_GetConservativeVisibility(surface,sample)?1:0;RTXDI_StoreVisibilityInDIReservoir(auxiliary,visibility.xxx,false);
            if(draw==0)RTXDI_StoreDIReservoir(auxiliary,HoDILayout(),id.xy,2);
            else _HoRTDISecondAuxiliary[id.y*(uint)_HoRTDIFrame.x+id.x]=RTXDI_PackDIReservoir(auxiliary);
        }
    }
    if(RAB_IsSurfaceValid(surface))");
            compute=ReplaceOnce(compute,"float3 radiance=sample.radiance*((bits>>1)&1)*RTXDI_GetDIReservoirInvPdf(extra)/max(sample.solidAnglePdf,1e-12);","float extraVisibility=_HoRTDIFinalVisibilityEnabled!=0?((bits>>1)&1):float(extra.packedVisibility&RTXDI_PackedDIReservoir_VisibilityChannelMax)/RTXDI_PackedDIReservoir_VisibilityChannelMax;\n            float3 radiance=sample.radiance*extraVisibility*RTXDI_GetDIReservoirInvPdf(extra)/max(sample.solidAnglePdf,1e-12);");
            compute=ReplaceOnce(compute,"float3 radiance=sample.radiance*((bits>>2)&1)*RTXDI_GetDIReservoirInvPdf(second)/max(sample.solidAnglePdf,1e-12);","float secondVisibility=_HoRTDIFinalVisibilityEnabled!=0?((bits>>2)&1):float(second.packedVisibility&RTXDI_PackedDIReservoir_VisibilityChannelMax)/RTXDI_PackedDIReservoir_VisibilityChannelMax;\n            float3 radiance=sample.radiance*secondVisibility*RTXDI_GetDIReservoirInvPdf(second)/max(sample.solidAnglePdf,1e-12);");
            return compute;
        }
    }
}
