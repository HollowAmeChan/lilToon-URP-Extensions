using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool compactAuxiliaryAudit;
        private static int SecondAuxiliaryStride=>compactAuxiliaryAudit?8:24;
        public static void RunCompactAuxiliaryDomain(){compactAuxiliaryAudit=true;RunSecondAuxiliaryDomain();}
        public static void RunCompactAuxiliaryDomainCompatibility(){compatibility=true;RunCompactAuxiliaryDomain();}
        public static void RunCompactAuxiliaryRayAudit(){compactAuxiliaryAudit=true;RunSecondAuxiliaryRayAudit();}
        public static void RunCompactOpaqueAuxiliaryDomain(){pairedOpaqueAudit=true;RunCompactAuxiliaryDomain();}
        public static void RunCompactOpaqueAuxiliaryDomainCompatibility(){compatibility=true;RunCompactOpaqueAuxiliaryDomain();}
        public static void RunCompactAuxiliaryEnvironment(){compactAuxiliaryAudit=true;RunAuxiliaryEnvironment();}
        public static void RunCompactAuxiliaryEnvironmentCompatibility(){compatibility=true;RunCompactAuxiliaryEnvironment();}
        public static void RunCompactDynamicAuxiliaryCDF(){compactAuxiliaryAudit=true;RunDynamicAuxiliaryCDF();}
        public static void RunCompactDynamicOpaqueAuxiliaryCDF(){pairedOpaqueAudit=true;RunCompactDynamicAuxiliaryCDF();}

        [StructLayout(LayoutKind.Sequential)] private struct CompactAuxiliary{public uint indexVisibility,inversePdf;}
        private static void ReadSecondAuxiliary(GraphicsBuffer buffer,PairedPackedReservoir[] destination,int destinationOffset,int sourceOffset,int count)
        {
            Require(buffer.stride==SecondAuxiliaryStride,"Auxiliary buffer stride differs from shader variant");
            if(!compactAuxiliaryAudit){buffer.GetData(destination,destinationOffset,sourceOffset,count);return;}
            Require(Marshal.SizeOf<CompactAuxiliary>()==8,"Compact auxiliary ABI must be eight bytes");
            int readCount=count;
            if(opaqueDirectAuxiliaryAudit)
            {
                Require(buffer.count==4,"Direct estimator audit must use only four GPU probe records");
                if(count==1){int x=sourceOffset%DirectProbeWidth,y=sourceOffset/DirectProbeWidth;Require(IsAuxiliaryProbe(x,y),"Direct readback requested a non-probe pixel");sourceOffset=Array.IndexOf(DirectProbeXs,x);}
                else{Require(destinationOffset==0&&sourceOffset==0&&count==DirectProbeWidth*DirectProbeWidth,"Direct bulk readback must target the diagnostic CPU image");readCount=4;}
            }
            var payload=new CompactAuxiliary[readCount];buffer.GetData(payload,0,sourceOffset,readCount);
            for(int i=0;i<readCount;i++)
            {
                uint index=payload[i].indexVisibility&0x7fffffff;bool visible=(payload[i].indexVisibility&0x80000000)!=0;
                int target=opaqueDirectAuxiliaryAudit&&count>1?DirectProbeWidth*DirectProbeWidth/2+DirectProbeXs[i]:destinationOffset+i;
                destination[target]=index==0x7fffffff?default:new PairedPackedReservoir{lightData=index|0x80000000,uvData=0x7fff7fff,mVisibility=(1u<<18)|(visible?0x3ffffu:0),targetPdf=1,weight=BitConverter.Int32BitsToSingle((int)payload[i].inversePdf)};
            }
        }
        private static (string compute,string ray) CompactSecondAuxiliary(string compute,string ray)
        {
            Require(pairedSecondAuxiliary&&pairedAuxiliaryPowerCDF,"Compact sidecar requires fresh analytic CDF samples");
            const string helpers=@"
// Private fresh delta sample, not the SDK reservoir ABI. The low 31 bits
// hold an analytic index (all ones = empty), bit31 is final visibility,
// and the second word preserves the complete FP32 inverse PDF.
uint2 HoDIPackAuxiliary(RTXDI_DIReservoir sample,bool visible)
{
    if(!RTXDI_IsValidDIReservoir(sample))return uint2(0x7fffffffu,0);
    return uint2(RTXDI_GetDIReservoirLightIndex(sample)|(visible?0x80000000u:0),asuint(RTXDI_GetDIReservoirInvPdf(sample)));
}
RTXDI_DIReservoir HoDIUnpackAuxiliary(uint2 payload)
{
    RTXDI_DIReservoir sample=RTXDI_EmptyDIReservoir();uint index=payload.x&0x7fffffffu;
    if(index==0x7fffffffu)return sample;
    sample.lightData=index|RTXDI_DIReservoir_LightValidBit;sample.uvData=0x7fff7fffu;
    sample.weightSum=asfloat(payload.y);sample.targetPdf=1;sample.M=1;
    RTXDI_StoreVisibilityInDIReservoir(sample,((payload.x&0x80000000u)!=0?1:0).xxx,false);return sample;
}
";
            const string declaration="StructuredBuffer<RTXDI_PackedDIReservoir> _HoRTDISecondAuxiliary;";
            compute=ReplaceOnce(compute,declaration,"StructuredBuffer<uint2> _HoRTDISecondAuxiliary;\n"+helpers);
            ray=ReplaceOnce(ray,declaration,"StructuredBuffer<uint2> _HoRTDISecondAuxiliary;\n"+helpers);
            compute=ReplaceOnce(compute,"RTXDI_UnpackDIReservoir(_HoRTDISecondAuxiliary[","HoDIUnpackAuxiliary(_HoRTDISecondAuxiliary[");
            ray=ReplaceOnce(ray,"=RTXDI_PackDIReservoir(second);","=HoDIPackAuxiliary(second,(bits&4)!=0);");
            if(pairedOpaqueAudit)compute=ReplaceOnce(compute,"=RTXDI_PackDIReservoir(auxiliary);","=HoDIPackAuxiliary(auxiliary,visibility>.5);");
            return(compute,ray);
        }
    }
}
