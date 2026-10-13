using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        private static bool opaqueDirectAuxiliaryAudit,opaqueDirectBaselineAudit;
        private static bool AuxiliaryImageHash=>opaqueDirectAuxiliaryAudit||opaqueDirectBaselineAudit;
        private static int SecondAuxiliaryCount(int pixels)=>opaqueDirectAuxiliaryAudit?4:pixels;
        private static string SecondAuxiliaryBinding=>opaqueDirectAuxiliaryAudit?"_HoRTDIProbeAuxiliary":"_HoRTDISecondAuxiliary";
        private static readonly int[] directDomainXs={72,104,136,168},directDynamicXs={52,60,68,76};
        private static int DirectProbeWidth=>dynamicAuxiliaryCDFAudit?128:256;
        private static int[] DirectProbeXs=>dynamicAuxiliaryCDFAudit?directDynamicXs:directDomainXs;
        private static bool IsAuxiliaryProbe(int x,int y)=>y==DirectProbeWidth/2&&Array.IndexOf(DirectProbeXs,x)>=0;
        public static void RunOpaqueDirectAuxiliaryDomain(){opaqueDirectAuxiliaryAudit=true;RunCompactOpaqueAuxiliaryDomain();}
        public static void RunOpaqueDirectAuxiliaryDomainCompatibility(){compatibility=true;RunOpaqueDirectAuxiliaryDomain();}
        public static void RunOpaqueDirectBaselineDomain(){opaqueDirectBaselineAudit=true;RunCompactOpaqueAuxiliaryDomain();}
        public static void RunOpaqueDirectBaselineDomainCompatibility(){compatibility=true;RunOpaqueDirectBaselineDomain();}
        public static void RunOpaqueDirectDynamicCDF(){opaqueDirectAuxiliaryAudit=true;RunCompactDynamicOpaqueAuxiliaryCDF();}
        public static void RunOpaqueDirectDynamicCDFCompatibility(){compatibility=true;RunOpaqueDirectDynamicCDF();}
        public static void RunOpaqueDirectBaselineDynamicCDF(){opaqueDirectBaselineAudit=true;RunCompactDynamicOpaqueAuxiliaryCDF();}
        public static void RunOpaqueDirectBaselineDynamicCDFCompatibility(){compatibility=true;RunOpaqueDirectBaselineDynamicCDF();}

        private static void BindSecondAuxiliary(RayTracingShader ray,ComputeShader compute,int kernel,GraphicsBuffer buffer)
        {
            if(!opaqueDirectAuxiliaryAudit)ray.SetBuffer("_HoRTDISecondAuxiliary",buffer);
            compute.SetBuffer(kernel,Shader.PropertyToID(SecondAuxiliaryBinding),buffer);
        }
        private static string ConsumeOpaqueAuxiliaryDirectly(string compute)
        {
            Require(pairedOpaqueAudit&&compactAuxiliaryAudit&&!auxiliaryEnvironmentAudit,"Direct prototype requires reviewed opaque analytic CDF domain");
            compute=ReplaceOnce(compute,"RWStructuredBuffer<uint2> _HoRTDISecondAuxiliary;","RWStructuredBuffer<uint2> _HoRTDIProbeAuxiliary;");
            const string begin="    if(_HoRTDIFinalVisibilityEnabled==0&&RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)";
            compute=ReplaceOnce(compute,begin,"    RTXDI_DIReservoir directSecond=RTXDI_EmptyDIReservoir();\n"+begin);
            compute=ReplaceOnce(compute,"else _HoRTDISecondAuxiliary[id.y*(uint)_HoRTDIFrame.x+id.x]=HoDIPackAuxiliary(auxiliary,visibility>.5);","else directSecond=auxiliary;");
            compute=ReplaceOnce(compute,"RTXDI_DIReservoir second=HoDIUnpackAuxiliary(_HoRTDISecondAuxiliary[id.y*(uint)_HoRTDIFrame.x+id.x]);","RTXDI_DIReservoir second=directSecond;");
            var xs=DirectProbeXs;string probe=$@"
    // Four sparse records validate the actual second draw. The estimator
    // consumes its local sample, never this test-only GPU snapshot.
    int probeIndex=id.x=={xs[0]}?0:id.x=={xs[1]}?1:id.x=={xs[2]}?2:id.x=={xs[3]}?3:-1;
    if(id.y=={DirectProbeWidth/2}&&probeIndex>=0)
        _HoRTDIProbeAuxiliary[probeIndex]=HoDIPackAuxiliary(directSecond,(directSecond.packedVisibility&63)!=0);
";
            compute=ReplaceOnce(compute,"    if(RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)",probe+"    if(RAB_IsSurfaceValid(surface)&&(uint)_HoRTDIFrame.z>0)");
            Require(!compute.Contains("_HoRTDISecondAuxiliary"),"Direct compute retained full-frame sidecar dependency");return compute;
        }
    }
}
