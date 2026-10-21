using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal static class HoRTGIProductionHistoryAudit
    {
        [Serializable] internal sealed class Check
        {
            public string mode; public int shaderFrame, spatialTemporalDifferentPackets;
            public HoRTGIHistoryFeedbackAudit.HistoryCheck history;
            public Vector3 rawMean, filteredMean;
        }
        [Serializable] internal sealed class Report
        {
            public bool pass, productionShaderBound, restoredSettings;
            public Check[] checks;
        }
    }
    public static partial class HoRTGICastleValidation
    {
        public static void RunProductionHistory() => Run(false, 256, denseScheduled: true, validationTag: "production-history", productionHistory: true);
        public static void RunProductionHistoryCompatibility() => Run(true, 256, denseScheduled: true, validationTag: "production-history", productionHistory: true);

        static async Task<HoRTGIProductionHistoryAudit.Report> AuditProductionHistory(HoRTGIRendererFeature gi, HoRTGIContext context,
            Action render, Func<RenderTexture, Color[]> read)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTGI/Shaders/HoRTGI.compute");
            if (shader == null) throw new Exception("Actual production compute missing.");
            var checks = new List<HoRTGIProductionHistoryAudit.Check>(); var report = new HoRTGIProductionHistoryAudit.Report();
            bool temporal = gi.Settings.temporal, spatial = gi.Settings.spatial; int oldFrame = context.frame;
            try
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    gi.Settings.temporal = (mode & 1) != 0; gi.Settings.spatial = (mode & 2) != 0;
                    context.historyValid = false; context.nrd.historyValid = false; context.frame = 104729;
                    for (int frame = 0; frame < 32; frame++) { await NextEditorTick(); render(); }
                    for (int frame = 0; frame < 16; frame++)
                    {
                        await NextEditorTick(); render();
                        HoRTGIHistoryFeedbackAudit.CheckBinding(gi, shader);
                        if (context.lastTemporal != gi.Settings.temporal || !context.alphaVisibility || !context.environmentSampling)
                            throw new Exception("Production temporal/alpha/environment contract differs.");
                        if (frame != 0 && frame != 15) continue;
                        var h = HoRTGIHistoryFeedbackAudit.CheckHistory(context, gi.Settings.spatial ? 2 : 0, mode, 104729);
                        var packets = new HoRTGIHistoryFeedbackAudit.Packet[context.reservoirs.count]; context.reservoirs.GetData(packets);
                        int different = 0;
                        for (int i = 0; i < context.arrayPitch; i++)
                            if (!HoRTGIHistoryFeedbackAudit.Equal(packets[i + context.arrayPitch], packets[i + 2 * context.arrayPitch])) different++;
                        if (gi.Settings.spatial && different == 0) throw new Exception("Spatial/temporal witness did not exercise distinct samples.");
                        var a = read(context.diffuse.rt); var b = read(context.PublishedDiffuse.rt); Vector3 raw = Vector3.zero, filtered = Vector3.zero;
                        for (int i = 0; i < a.Length; i++)
                        {
                            for (int c = 0; c < 3; c++) if (!float.IsFinite(a[i][c]) || !float.IsFinite(b[i][c]) || a[i][c] < 0 || b[i][c] < 0 || a[i].a < .5f || b[i].a < .5f)
                                throw new Exception("Invalid production-history publication.");
                            raw += new Vector3(a[i].r, a[i].g, a[i].b) / a.Length; filtered += new Vector3(b[i].r, b[i].g, b[i].b) / b.Length;
                        }
                        checks.Add(new HoRTGIProductionHistoryAudit.Check { mode = HoRTGISeedAudit.Modes[mode], shaderFrame = context.frame - 1,
                            history = h, spatialTemporalDifferentPackets = different, rawMean = raw, filteredMean = filtered });
                    }
                }
                report.pass = report.productionShaderBound = true; report.checks = checks.ToArray();
            }
            finally
            {
                gi.Settings.temporal = temporal; gi.Settings.spatial = spatial; context.historyValid = false; context.nrd.historyValid = false; context.frame = oldFrame;
                report.restoredSettings = true;
            }
            Debug.Log("[HoGIProductionHistory] COMPLETE checks=" + checks.Count); return report;
        }
    }
}
