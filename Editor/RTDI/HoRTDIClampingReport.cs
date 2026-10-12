using System;
using System.IO;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    public static partial class HoRTDIValidation
    {
        [Serializable] private sealed class ClampingAuditReport
        {
            public bool diagnosticOnly=true,reuse;public string path,mode;public long samples,changedSamples,fastChangedSamples;public uint normalBudget,fastBudget;
            public double changedFraction,fastChangedFraction,meanChange,meanFastChange,meanNormalFastDistanceBefore,meanNormalFastDistanceAfter,maxChange;
        }
        private static void WriteClampingAudit(bool reuse,long samples,long changed,long fastChanged,double change,double fastChange,double before,double after,double max,uint normalBudget,uint fastBudget)
        {
            var report=new ClampingAuditReport{reuse=reuse,path=compatibility?"compatibility":"RenderGraph",mode=cutoutPairedFinalSample?"partition":"baseline",samples=samples,changedSamples=changed,fastChangedSamples=fastChanged,normalBudget=normalBudget,fastBudget=fastBudget,changedFraction=(double)changed/samples,fastChangedFraction=(double)fastChanged/samples,meanChange=change/samples,meanFastChange=fastChange/samples,meanNormalFastDistanceBefore=before/samples,meanNormalFastDistanceAfter=after/samples,maxChange=max};
            File.WriteAllText(Path.Combine(Application.dataPath,"../rtdi-auxclamp67-"+report.mode+"-"+(reuse?"reuse-":"none-")+(compatibility?"compat":"rg")+".json"),JsonUtility.ToJson(report,true));
            Debug.Log($"[HoRTDIValidation] AUX_CLAMP reuse={reuse} samples={samples} changed={report.changedFraction:F6} meanChange={report.meanChange:E6} fastChanged={report.fastChangedFraction:F6} fastDistance={report.meanNormalFastDistanceBefore:F6}->{report.meanNormalFastDistanceAfter:F6} maxChange={max:E6} budgets={normalBudget}/{fastBudget}");
        }
    }
}
