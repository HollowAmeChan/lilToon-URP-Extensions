using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal static class HoRTGIHistoryFeedbackAudit
    {
        internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static readonly string[] Policies = { "legacy", "packed-spatial", "packed-temporal" };
        [Serializable] internal sealed class ShaderSource { public string policy, path, sha256; public int[] kernels; }
        [Serializable, StructLayout(LayoutKind.Sequential)] internal struct Packet
        {
            public Vector3 position; public uint normal, misc; public float weight; public uint radiance, unused;
        }
        [Serializable] internal sealed class HistoryCheck
        {
            public string policy, mode; public int seed, shaderFrame, pixels, sourceBuffer, packetMismatches, radianceMismatches, normalMismatches;
            public bool strictCopyRequired; public Packet[] selectedHistory, selectedSource;
        }
        [Serializable] internal sealed class Variant
        {
            public string policy; public HoRTGISeedAudit.Report capture;
            public string rawMeanExr, filteredMeanExr, rawPreviewX100, filteredPreviewX100;
        }
        [Serializable] internal sealed class Report
        {
            public bool functionalPass, qualification, restoredOriginal, restoredOriginalBound, qualityQualified, gpuOnly;
            public GpuVariant[] gpuVariants;
            public string originalShaderPath, originalSourceSha256;
            public ShaderSource[] sources; public Variant[] variants; public HistoryCheck[] historyChecks;
        }

        [Serializable] internal sealed class GpuVariant { public string policy; public HoRTGIGpuClock.Summary[] summary; public HoRTGIGpuClock.Sample[] samples; }

        internal static ComputeShader[] Generate(ComputeShader original, Report report)
        {
            string asset = AssetDatabase.GetAssetPath(original);
            const string package = "Packages/jp.lilxyzw.liltoon.urp.extensions/";
            string workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string sourcePath = Path.Combine(workspace, asset.Substring(package.Length));
            if (!asset.StartsWith(package) || !File.Exists(sourcePath)) throw new Exception("Original compute provenance missing.");
            report.originalShaderPath = asset; report.originalSourceSha256 = Hash(File.ReadAllBytes(sourcePath));
            string body = File.ReadAllText(sourcePath);
            string normalized = Regex.Replace(body, "#include \\\"([^\\\"]+)\\\"", match =>
            {
                string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath), match.Groups[1].Value));
                if (!full.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Include outside package.");
                return "#include \"" + package + full.Substring(workspace.Length + 1).Replace('\\', '/') + "\"";
            });
            const string store = "RTXDI_StoreGIReservoir(reservoir,HoGILayout(),pixel,0);";
            if (normalized.Split(new[] { store }, StringSplitOptions.None).Length != 2) throw new Exception("Expected one Final history write.");
            string folder = Path.Combine(Application.dataPath, "HoRTGIHistoryFeedback"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "OriginalNormalized.compute"), normalized);
            var result = new ComputeShader[3]; result[0] = original;
            var sources = new List<ShaderSource>();
            string[] kernels = { "ShadeSecondary", "Temporal", "Spatial", "Final", "FinalNRD", "ResolveNRD", "BuildEnvironmentPdf", "PresampleEnvironment" };
            for (int i = 1; i < 3; i++)
            {
                string from = i == 1 ? "1" : "(_HoRTGIFlags.y>.5?2:1)";
                string replacement = "_HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel,0)]=_HoRTGIReservoirs[RTXDI_ReservoirPositionToPointer(HoGILayout(),pixel," + from + ")];";
                string name = i == 1 ? "PackedSpatial.compute" : "PackedTemporal.compute";
                string file = Path.Combine(folder, name); File.WriteAllText(file, normalized.Replace(store, replacement));
                string path = "Assets/HoRTGIHistoryFeedback/" + name;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path); if (shader == null) throw new Exception("Candidate shader not imported.");
                int[] actual = kernels.Select(shader.FindKernel).ToArray();
                foreach (var message in ShaderUtil.GetComputeShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error || message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning)
                        throw new Exception(message.message);
                result[i] = shader; sources.Add(new ShaderSource { policy = Policies[i], path = path, sha256 = Hash(File.ReadAllBytes(file)), kernels = actual });
            }
            report.sources = sources.ToArray(); return result;
        }

        internal static void CheckBinding(HoRTGIRendererFeature gi, ComputeShader expected)
        {
            var pass = typeof(HoRTGIRendererFeature).GetField("pass", Flags).GetValue(gi);
            if (!ReferenceEquals(pass.GetType().GetField("shader", Flags).GetValue(pass), expected)) throw new Exception("Actual GI pass did not bind selected compute shader.");
        }
        internal static HistoryCheck CheckHistory(HoRTGIContext context, int policy, int mode, int seed)
        {
            if (Marshal.SizeOf<Packet>() != 32) throw new Exception("GI packed reservoir layout drift.");
            var packets = new Packet[context.reservoirs.count]; context.reservoirs.GetData(packets);
            int source = policy == 2 && (mode & 2) != 0 ? 2 : 1;
            var check = new HistoryCheck { policy = Policies[policy], mode = HoRTGISeedAudit.Modes[mode], seed = seed,
                shaderFrame = context.frame - 1, pixels = context.width * context.height, sourceBuffer = source, strictCopyRequired = policy != 0 };
            for (int y = 0; y < context.height; y++) for (int x = 0; x < context.width; x++)
            {
                int at = Address(x, y); var a = packets[at]; var b = packets[at + source * context.arrayPitch];
                if (!Equal(a, b)) check.packetMismatches++;
                if (a.radiance != b.radiance) check.radianceMismatches++;
                if (a.normal != b.normal) check.normalMismatches++;
            }
            check.selectedHistory = HoRTGIDenseReferenceAudit.Pixels.Select(p => packets[Address(p.x, p.y)]).ToArray();
            check.selectedSource = HoRTGIDenseReferenceAudit.Pixels.Select(p => packets[Address(p.x, p.y) + source * context.arrayPitch]).ToArray();
            if (check.strictCopyRequired && check.packetMismatches != 0) throw new Exception("Candidate history is not the exact selected packed source.");
            return check;
            int Address(int x, int y) => (y / 16) * context.rowPitch + (x / 16) * 256 + (y % 16) * 16 + x % 16;
        }
        static bool Equal(Packet a, Packet b) => Bits(a.position.x) == Bits(b.position.x) && Bits(a.position.y) == Bits(b.position.y) && Bits(a.position.z) == Bits(b.position.z)
            && a.normal == b.normal && a.misc == b.misc && Bits(a.weight) == Bits(b.weight) && a.radiance == b.radiance && a.unused == b.unused;
        static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
        internal static string Hash(byte[] bytes) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }

        internal sealed class MeanImages
        {
            readonly double[] raw = new double[256 * 256 * 3], filtered = new double[256 * 256 * 3]; int frames;
            internal void Add(Color[] a, Color[] b)
            {
                for (int i = 0; i < a.Length; i++) for (int c = 0; c < 3; c++) { raw[i * 3 + c] += a[i][c]; filtered[i * 3 + c] += b[i][c]; }
                frames++;
            }
            internal void Save(string output, Variant variant)
            {
                var image = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
                var preview = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
                try
                {
                    for (int signal = 0; signal < 2; signal++)
                    {
                        var values = signal == 0 ? raw : filtered; var colors = new Color[256 * 256];
                        for (int i = 0; i < colors.Length; i++) colors[i] = new Color((float)(values[i * 3] / frames), (float)(values[i * 3 + 1] / frames), (float)(values[i * 3 + 2] / frames), 1);
                        image.SetPixels(colors); image.Apply(); string prefix = variant.policy + (signal == 0 ? "-raw" : "-filtered");
                        string exr = Path.Combine(output, prefix + "-mean.exr"), png = Path.Combine(output, prefix + "-mean-preview-x100.png");
                        File.WriteAllBytes(exr, image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                        for (int i = 0; i < colors.Length; i++) { var p = colors[i]; colors[i] = new Color(Map(p.r * 100), Map(p.g * 100), Map(p.b * 100), 1); }
                        preview.SetPixels(colors); preview.Apply(); File.WriteAllBytes(png, preview.EncodeToPNG());
                        if (signal == 0) { variant.rawMeanExr = exr; variant.rawPreviewX100 = png; } else { variant.filteredMeanExr = exr; variant.filteredPreviewX100 = png; }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(preview); }
            }
            static float Map(float value) => Mathf.Pow(Mathf.Max(0, value) / (1 + Mathf.Max(0, value)), 1 / 2.2f);
        }
    }

    public static partial class HoRTGICastleValidation
    {
        public static void RunFeedbackQualification() => Run(false, 256, denseScheduled: true, validationTag: "feedback-qualification", feedbackAudit: true, feedbackQualification: true);
        public static void RunFeedbackGpu() => Run(false, 256, validationTag: "feedback-gpu", feedbackAudit: true, feedbackGpu: true);
        public static void RunFeedbackMatrix() => Run(false, 256, denseScheduled: true, validationTag: "feedback-matrix", feedbackAudit: true);

        static async Task<HoRTGIHistoryFeedbackAudit.Report> AuditHistoryFeedback(HoRTGIRendererFeature gi, HoRTGIContext context,
            HoRTGIDenseReferenceFeature feature, Action render, Func<RenderTexture, Color[]> read, bool qualification, string output, bool gpuOnly)
        {
            var report = new HoRTGIHistoryFeedbackAudit.Report { qualification = qualification };
            var field = typeof(HoRTGIRendererFeature).GetField("shader", HoRTGIHistoryFeedbackAudit.Flags);
            var original = (ComputeShader)field.GetValue(gi); var shaders = HoRTGIHistoryFeedbackAudit.Generate(original, report);
            if (gpuOnly) return AuditHistoryFeedbackGpu(gi, context, render, field, original, shaders, report);
            int seeds = qualification ? 2 : 8, warmup = qualification ? 16 : 64, measured = qualification ? 16 : 256;
            int[] modes = qualification ? new[] { 0, 1, 2, 3 } : new[] { 3 };
            var audits = Enumerable.Range(0, 3).Select(_ => new HoRTGISeedAudit(seeds, warmup, measured, context.width)).ToArray();
            var images = Enumerable.Range(0, 3).Select(_ => qualification ? null : new HoRTGIHistoryFeedbackAudit.MeanImages()).ToArray();
            var history = new List<HoRTGIHistoryFeedbackAudit.HistoryCheck>();
            bool temporal = gi.Settings.temporal, spatial = gi.Settings.spatial; int oldFrame = context.frame;
            report.variants = Enumerable.Range(0, 3).Select(i => new HoRTGIHistoryFeedbackAudit.Variant { policy = HoRTGIHistoryFeedbackAudit.Policies[i] }).ToArray();
            try
            {
                foreach (int mode in modes) for (int seed = 0; seed < seeds; seed++) for (int variant = 0; variant < 3; variant++)
                {
                    field.SetValue(gi, shaders[variant]); gi.Settings.temporal = (mode & 1) != 0; gi.Settings.spatial = (mode & 2) != 0;
                    context.historyValid = false; context.nrd.historyValid = false; context.frame = HoRTGISeedAudit.Seeds[seed]; feature.collect = false;
                    for (int frame = 0; frame < warmup; frame++) { await NextEditorTick(); render(); }
                    var audit = audits[variant]; audit.Begin(mode, HoRTGISeedAudit.Seeds[seed]);
                    for (int frame = 0; frame < measured; frame++)
                    {
                        await NextEditorTick(); feature.collect = frame == 0 || frame == measured - 1; feature.batch = seed; render();
                        HoRTGIHistoryFeedbackAudit.CheckBinding(gi, shaders[variant]);
                        if (feature.collect) { audit.ValidateProbe(feature, context, frame == 0); history.Add(HoRTGIHistoryFeedbackAudit.CheckHistory(context, variant, mode, HoRTGISeedAudit.Seeds[seed])); }
                        var raw = read(context.diffuse.rt); var filtered = read(context.PublishedDiffuse.rt);
                        audit.Add(raw, filtered, context); images[variant]?.Add(raw, filtered);
                    }
                    feature.collect = false; audit.End();
                    for (int i = 0; i < 3; i++) report.variants[i].capture = audits[i].Snapshot(false, modes);
                    report.historyChecks = history.ToArray();
                    File.WriteAllText(Path.Combine(output, "feedback-progress.json"), JsonUtility.ToJson(report, false));
                    Debug.Log("[HoGIFeedback] segment policy=" + HoRTGIHistoryFeedbackAudit.Policies[variant] + " mode=" + HoRTGISeedAudit.Modes[mode] + " seed=" + HoRTGISeedAudit.Seeds[seed] + " complete");
                }
                for (int i = 0; i < 3; i++) { report.variants[i].capture = audits[i].Snapshot(true, modes); images[i]?.Save(output, report.variants[i]); }
                report.functionalPass = true;
            }
            finally
            {
                feature.collect = false; field.SetValue(gi, original); gi.Settings.temporal = temporal; gi.Settings.spatial = spatial;
                context.historyValid = false; context.nrd.historyValid = false; context.frame = oldFrame;
                report.restoredOriginal = ReferenceEquals(field.GetValue(gi), original);
                if (report.functionalPass) { render(); HoRTGIHistoryFeedbackAudit.CheckBinding(gi, original); report.restoredOriginalBound = true; }
            }
            Debug.Log("[HoGIFeedback] COMPLETE variants=3 qualification=" + qualification);
            return report;
        }
        static HoRTGIHistoryFeedbackAudit.Report AuditHistoryFeedbackGpu(HoRTGIRendererFeature gi, HoRTGIContext context,
            Action render, FieldInfo field, ComputeShader original, ComputeShader[] shaders, HoRTGIHistoryFeedbackAudit.Report report)
        {
            bool temporal = gi.Settings.temporal, spatial = gi.Settings.spatial, composite = gi.Settings.composite;
            float intensity = gi.Settings.intensity; int oldFrame = context.frame;
            var variants = new List<HoRTGIHistoryFeedbackAudit.GpuVariant>(); report.gpuOnly = true;
            try
            {
                gi.Settings.temporal = gi.Settings.spatial = gi.Settings.composite = true; gi.Settings.intensity = .25f;
                for (int variant = 0; variant < shaders.Length; variant++)
                {
                    field.SetValue(gi, shaders[variant]); context.historyValid = false; context.nrd.historyValid = false; context.frame = 104729;
                    for (int frame = 0; frame < 64; frame++) render();
                    using var clock = new HoRTGIGpuClock(); context.ValidationGpuMarker = clock.Mark; context.ValidationGpuRasterMarker = clock.MarkRaster;
                    try
                    {
                        for (int frame = 0; frame < 40; frame++)
                        {
                            clock.Frame = frame; render(); HoRTGIHistoryFeedbackAudit.CheckBinding(gi, shaders[variant]); clock.Collect();
                        }
                        variants.Add(new HoRTGIHistoryFeedbackAudit.GpuVariant { policy = HoRTGIHistoryFeedbackAudit.Policies[variant], summary = clock.Summarize(32), samples = clock.samples.OrderBy(s => s.slot).ToArray() });
                    }
                    finally { context.ValidationGpuMarker = null; context.ValidationGpuRasterMarker = null; }
                    Debug.Log("[HoGIFeedbackGpu] policy=" + HoRTGIHistoryFeedbackAudit.Policies[variant] + " complete");
                }
                report.gpuVariants = variants.ToArray(); report.functionalPass = true;
            }
            finally
            {
                context.ValidationGpuMarker = null; context.ValidationGpuRasterMarker = null; field.SetValue(gi, original);
                gi.Settings.temporal = temporal; gi.Settings.spatial = spatial; gi.Settings.composite = composite; gi.Settings.intensity = intensity;
                context.historyValid = false; context.nrd.historyValid = false; context.frame = oldFrame;
                report.restoredOriginal = ReferenceEquals(field.GetValue(gi), original);
                if (report.functionalPass) { render(); HoRTGIHistoryFeedbackAudit.CheckBinding(gi, original); report.restoredOriginalBound = true; }
            }
            Debug.Log("[HoGIFeedbackGpu] COMPLETE variants=3"); return report;
        }

    }
}
