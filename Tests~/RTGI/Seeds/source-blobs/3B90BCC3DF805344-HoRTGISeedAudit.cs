using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using lilToon.URP.Extensions.RTGI;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.RTGI
{
    internal sealed class HoRTGISeedAudit
    {
        internal static readonly int[] Seeds = { 0, 104729, 1153305, 2201881, 3250457, 4299033, 5347609, 6396185 };
        internal static readonly string[] Modes = { "none", "temporal", "spatial", "temporal-spatial" };

        [Serializable] internal sealed class ReferenceInput { public string path, sha256; }
        [Serializable] private sealed class ReferenceFile { public ReferencePoints denseReference; }
        [Serializable] private sealed class ReferencePoints { public HoRTGIDenseReferenceAudit.Point[] points; }
        [Serializable] internal sealed class Local
        {
            public int x, y;
            public Vector3 raw, filtered, rawVariance, filteredVariance;
            public Vector3[] rawBlockMeans, filteredBlockMeans;
        }
        [Serializable] internal sealed class Segment
        {
            public string mode, firstSecondaryHash, lastSecondaryHash, firstRisHash, lastRisHash;
            public int seed, warmup, measured, firstShaderFrame, lastShaderFrame, firstApplicationFrame, lastApplicationFrame;
            public int lightingSignature, lightCount, instances, cutouts;
            public uint sceneRevision, skyRevision;
            public double startedEditorSeconds, finishedEditorSeconds;
            public float maxPrimaryError;
            public bool temporal, spatial, actualTemporal, environment, alphaVisibility;
            public HoRTGISpatialAudit.Domain full, phaseZero;
            public HoRTGISpatialAudit.Frame[] frameMeans;
            public Local[] points;
        }
        [Serializable] internal sealed class Report
        {
            public string protocol = "Four original production reuse modes, predeclared independent frame seeds, GI/NRD reset per segment. Selected receivers have post-selection bias; reference is the same shading model, not independent absolute truth or full-image acceptance.";
            public bool functionalPass, terminal;
            public int seedCount, warmup, measured, width = 256, height = 256;
            public ReferenceInput[] referenceInputs;
            public HoRTGIDenseReferenceAudit.Point[] referencePoints;
            public Segment[] segments;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Secondary
        {
            public Vector3 position; public uint normal, diffuse, specular;
            public Vector3 emission; public float pdf; public uint geometric, flags;
        }

        readonly Report report;
        readonly List<Segment> segments = new();
        readonly HoRTGIDenseReferenceAudit.Point[] reference;
        readonly int width;
        HoRTGISpatialAudit spatial;
        Segment current;
        double[,,] sums, squares, blocks;
        int frames;

        internal HoRTGISeedAudit(int seedCount, int warmup, int measured, int width)
        {
            if (seedCount < 2 || seedCount > Seeds.Length || warmup < 16 || measured < 16 || measured % 16 != 0 || width != 256)
                throw new ArgumentException("Seed audit budget/domain outside its fixed protocol.");
            this.width = width;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            var inputs = new List<ReferenceInput>();
            var points = new Dictionary<Vector2Int, HoRTGIDenseReferenceAudit.Point>();
            foreach (string name in new[] { "fixed-reference.json", "fixed-controls.json" })
            {
                string relative = "Tests~/RTGI/DenseReference/results/" + name;
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));
                inputs.Add(new ReferenceInput { path = relative, sha256 = Hash(bytes) });
                var file = JsonUtility.FromJson<ReferenceFile>(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));
                foreach (var point in file.denseReference.points) points[new Vector2Int(point.x, point.y)] = point;
            }
            reference = HoRTGIDenseReferenceAudit.Pixels.Select(p => points[p]).ToArray();
            if (reference.Count(p => p.converged) != 15) throw new Exception("Expected 15 qualified reference receivers and one unresolved control.");
            report = new Report { seedCount = seedCount, warmup = warmup, measured = measured,
                referenceInputs = inputs.ToArray(), referencePoints = reference };
        }

        internal void Begin(int mode, int seed)
        {
            frames = 0;
            current = new Segment { mode = Modes[mode], seed = seed, warmup = report.warmup, measured = report.measured,
                temporal = (mode & 1) != 0, spatial = (mode & 2) != 0,
                firstShaderFrame = seed + report.warmup, lastShaderFrame = seed + report.warmup + report.measured - 1,
                startedEditorSeconds = EditorApplication.timeSinceStartup };
            sums = new double[2, reference.Length, 3]; squares = new double[2, reference.Length, 3];
            blocks = new double[2, reference.Length, ((report.measured + 31) / 32) * 3];
            spatial = new HoRTGISpatialAudit(width, width);
        }

        internal void ValidateProbe(HoRTGIDenseReferenceFeature feature, HoRTGIContext context, bool first)
        {
            if (feature.lastLightCount != 18 || feature.lastInstances != 15 || feature.lastCutouts != 12 || !feature.lastEnvironment || !context.alphaVisibility)
                throw new Exception("Seed audit support/light/sky/alpha domain changed.");
            var settings = feature.lastSettings;
            if (settings.temporal != current.temporal || settings.spatial != current.spatial || settings.secondaryLightSamples != 8 ||
                settings.secondaryEnvironmentSamples != 4 || settings.spatialSamples != 4 || settings.spatialRadius != 16 ||
                settings.maxHistory != 20 || settings.maxDistance != 1000 || settings.rayBias != .001f || settings.includeDirectEnvironment)
                throw new Exception("Seed audit effective sampling contract changed.");
            var probes = new HoRTGIDenseReferenceFeature.PrimaryProbe[reference.Length]; feature.probe.GetData(probes);
            var rays = new Vector4[reference.Length * 4]; feature.rayProbe.GetData(rays);
            for (int i = 0; i < reference.Length; i++)
            {
                var probe = probes[i]; var expected = reference[i];
                if (probe.originalWorld.w < .5f || probe.rabWorld.w < .5f) throw new Exception("Seed primary invalid.");
                float error = Mathf.Max(Vector3.Distance(probe.rabWorld, expected.primaryWorld),
                    Vector3.Distance(probe.normalDepth, expected.normal), Vector3.Distance(probe.albedo, expected.albedo),
                    Mathf.Abs(probe.normalDepth.w - expected.viewDepth));
                var vectors = new[] { probe.originalWorld, probe.rabWorld, probe.normalDepth, probe.albedo };
                for (int c = 0; c < 4; c++)
                {
                    error = Mathf.Max(error, probe.errors0[c], probe.errors1[c]);
                    for (int j = 0; j < 4; j++) error = Mathf.Max(error, Mathf.Abs(rays[i * 4 + j][c] - vectors[j][c]));
                }
                if (!float.IsFinite(error) || error > .0005f) throw new Exception("Seed/reference/full/history primary mismatch at " + expected.x + "," + expected.y);
                current.maxPrimaryError = Mathf.Max(current.maxPrimaryError, error);
            }
            string secondaryHash = SecondaryHash(context), risHash = RisHash(context);
            if (first)
            {
                current.lightingSignature = feature.lastLightingSignature; current.sceneRevision = feature.lastSceneRevision;
                current.skyRevision = feature.lastSkyRevision; current.lightCount = feature.lastLightCount;
                current.instances = feature.lastInstances; current.cutouts = feature.lastCutouts;
                current.firstSecondaryHash = secondaryHash; current.firstRisHash = risHash;
                current.firstApplicationFrame = Time.frameCount;
            }
            else
            {
                if (current.lightingSignature != feature.lastLightingSignature || current.sceneRevision != feature.lastSceneRevision || current.skyRevision != feature.lastSkyRevision)
                    throw new Exception("Seed segment domain changed.");
                current.lastSecondaryHash = secondaryHash; current.lastRisHash = risHash;
                current.lastApplicationFrame = Time.frameCount;
            }
            current.environment = feature.lastEnvironment; current.alphaVisibility = context.alphaVisibility;
        }

        internal void Add(Color[] raw, Color[] filtered, HoRTGIContext context)
        {
            if (context.frame != current.firstShaderFrame + frames + 1 || context.lastTemporal != current.temporal)
                throw new Exception("Shader seed progression or actual temporal state differs from protocol.");
            current.actualTemporal = context.lastTemporal;
            spatial.AddPublication(raw, filtered, frames);
            for (int i = 0; i < reference.Length; i++)
            {
                int at = reference[i].y * width + reference[i].x;
                for (int c = 0; c < 3; c++) for (int signal = 0; signal < 2; signal++)
                {
                    double value = signal == 0 ? raw[at][c] : filtered[at][c];
                    sums[signal, i, c] += value; squares[signal, i, c] += value * value;
                    blocks[signal, i, (frames / 32) * 3 + c] += value;
                }
            }
            frames++;
        }

        internal void End()
        {
            if (frames != report.measured) throw new Exception("Seed segment incomplete.");
            var full = spatial.Finish(); current.full = full.full; current.phaseZero = full.phases[0]; current.frameMeans = full.frameMeans;
            current.points = new Local[reference.Length];
            for (int i = 0; i < reference.Length; i++)
            {
                var point = new Local { x = reference[i].x, y = reference[i].y,
                    rawBlockMeans = new Vector3[(frames + 31) / 32], filteredBlockMeans = new Vector3[(frames + 31) / 32] };
                for (int c = 0; c < 3; c++)
                {
                    point.raw[c] = (float)(sums[0, i, c] / frames); point.filtered[c] = (float)(sums[1, i, c] / frames);
                    point.rawVariance[c] = (float)Math.Max(0, (squares[0, i, c] - sums[0, i, c] * sums[0, i, c] / frames) / (frames - 1));
                    point.filteredVariance[c] = (float)Math.Max(0, (squares[1, i, c] - sums[1, i, c] * sums[1, i, c] / frames) / (frames - 1));
                    for (int block = 0; block < point.rawBlockMeans.Length; block++)
                    {
                        int n = Math.Min(32, frames - block * 32);
                        point.rawBlockMeans[block][c] = (float)(blocks[0, i, block * 3 + c] / n);
                        point.filteredBlockMeans[block][c] = (float)(blocks[1, i, block * 3 + c] / n);
                    }
                }
                current.points[i] = point;
            }
            current.finishedEditorSeconds = EditorApplication.timeSinceStartup;
            segments.Add(current); current = null; spatial = null;
        }

        internal Report Snapshot(bool terminal)
        {
            report.segments = segments.ToArray(); report.terminal = terminal;
            report.functionalPass = terminal && segments.Count == report.seedCount * Modes.Length;
            if (terminal)
            {
                if (!report.functionalPass) throw new Exception("Seed matrix incomplete.");
                for (int seed = 0; seed < report.seedCount; seed++)
                {
                    var matched = segments.Where(s => s.seed == Seeds[seed]).ToArray();
                    if (matched.Select(s => s.firstSecondaryHash).Distinct().Count() != 1 || matched.Select(s => s.lastSecondaryHash).Distinct().Count() != 1 ||
                        matched.Select(s => s.firstRisHash).Distinct().Count() != 1 || matched.Select(s => s.lastRisHash).Distinct().Count() != 1)
                        throw new Exception("Paired modes did not use identical selected fresh geometry/RIS proposals.");
                }
            }
            return report;
        }

        string SecondaryHash(HoRTGIContext context)
        {
            if (Marshal.SizeOf<Secondary>() != 48) throw new Exception("Secondary layout drift.");
            var values = new Secondary[context.secondary.count]; context.secondary.GetData(values);
            using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
            foreach (var point in reference)
            {
                var v = values[point.y * width + point.x];
                Write(v.position); writer.Write(v.normal); writer.Write(v.diffuse); writer.Write(v.specular);
                Write(v.emission); writer.Write(v.pdf); writer.Write(v.geometric); writer.Write(v.flags);
            }
            return Hash(memory.ToArray());
            void Write(Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
        }
        static string RisHash(HoRTGIContext context)
        {
            var values = new Vector2Int[context.environmentRis.count]; context.environmentRis.GetData(values);
            using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
            foreach (var v in values) { writer.Write(v.x); writer.Write(v.y); }
            return Hash(memory.ToArray());
        }
        static string Hash(byte[] value)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", ""); }
    }

    public static partial class HoRTGICastleValidation
    {
        public static void RunSeedQualification() => Run(false, 256, denseScheduled: true, validationTag: "seed-qualification",
            seedAudit: true, seedCount: 2, seedWarmup: 16, seedMeasured: 16);
        public static void RunSeedMatrix() => Run(false, 256, denseScheduled: true, validationTag: "seed-matrix", seedAudit: true);

        static async Task<HoRTGISeedAudit.Report> AuditSeeds(HoRTGIRendererFeature gi, HoRTGIContext context,
            HoRTGIDenseReferenceFeature feature, Action render, Func<RenderTexture, Color[]> read,
            int seedCount, int warmup, int measured, string output)
        {
            var audit = new HoRTGISeedAudit(seedCount, warmup, measured, context.width);
            bool temporal = gi.Settings.temporal, spatial = gi.Settings.spatial; int oldFrame = context.frame;
            try
            {
                for (int mode = 0; mode < HoRTGISeedAudit.Modes.Length; mode++) for (int seed = 0; seed < seedCount; seed++)
                {
                    gi.Settings.temporal = (mode & 1) != 0; gi.Settings.spatial = (mode & 2) != 0;
                    context.historyValid = false; context.nrd.historyValid = false; context.frame = HoRTGISeedAudit.Seeds[seed];
                    feature.collect = false;
                    for (int frame = 0; frame < warmup; frame++) { await NextEditorTick(); render(); }
                    audit.Begin(mode, HoRTGISeedAudit.Seeds[seed]);
                    for (int frame = 0; frame < measured; frame++)
                    {
                        await NextEditorTick(); feature.collect = frame == 0 || frame == measured - 1; feature.batch = seed; render();
                        if (feature.collect) audit.ValidateProbe(feature, context, frame == 0);
                        audit.Add(read(context.diffuse.rt), read(context.PublishedDiffuse.rt), context);
                    }
                    feature.collect = false; audit.End();
                    File.WriteAllText(Path.Combine(output, "seed-progress.json"), JsonUtility.ToJson(audit.Snapshot(false), false));
                    Debug.Log("[HoGISeeds] segment mode=" + HoRTGISeedAudit.Modes[mode] + " seed=" + HoRTGISeedAudit.Seeds[seed] + " complete");
                }
                var report = audit.Snapshot(true);
                Debug.Log("[HoGISeeds] COMPLETE segments=" + report.segments.Length);
                return report;
            }
            finally
            {
                feature.collect = false; gi.Settings.temporal = temporal; gi.Settings.spatial = spatial;
                context.historyValid = false; context.nrd.historyValid = false; context.frame = oldFrame;
            }
        }
    }
}
