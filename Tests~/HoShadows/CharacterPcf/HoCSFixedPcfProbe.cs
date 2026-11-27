using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HoCSFixedPcfProbe
{
    private const int Size = 128, AtlasSize = 32, TileStart = 8, TileSize = 16;
    private const float Tolerance = 0.0001f, Slope = 0.42f;
    private const string ShaderName = "Hidden/Ho-CS/P1bFixedPcfProbe";
    private const string SoftKeyword = "HO_CS_PROBE_SOFT", RuntimeKeyword = "HO_CS_PROBE_RUNTIME_MODE";
    private static readonly Vector4 FullReceiver = new Vector4(0f, 0f, 1f, 1f);

    [Serializable] private sealed class Report
    {
        public string unity, graphics, gpu;
        public bool reversedZ;
        public bool success;
        public string error;
        public int checks, hardVisibleLevels, softVisibleLevels, softIntermediatePixels;
        public int selfShadowWithPlane, selfShadowWithoutPlane;
        public float tileNeighborDeltaHard, tileNeighborDeltaSoft;
        public List<string> results = new();
    }

    [Serializable] private sealed class CompileRow
    {
        public string shader, pass, sample;
        public string[] keywords, messages, resources;
        public bool success;
        public int bytes;
        public double milliseconds;
    }

    [Serializable] private sealed class CompileReport
    {
        public string unity, graphics, gpu, compiler = "D3D / StandaloneWindows64 / Fragment";
        public List<CompileRow> rows = new();
    }

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("HO_CHARACTER_SHADOW_P1B_OUT");
        if (!Application.isBatchMode || string.IsNullOrEmpty(output) || !Path.IsPathRooted(output) || !Directory.Exists(output)
            || File.Exists(Path.Combine(output, "pcf-validation.json")) || QualitySettings.activeColorSpace != ColorSpace.Linear)
            throw new InvalidOperationException("Use a private Linear project in batch mode and a fresh output directory.");

        var report = new Report {
            unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
            gpu = SystemInfo.graphicsDeviceName, reversedZ = SystemInfo.usesReversedZBuffer
        };
        var resources = new List<Object>();
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var shader = Shader.Find(ShaderName);
            Require(report, shader && shader.isSupported, "Probe shader available.");
            Compile(shader, report, output);
            Material material = Own(new Material(shader), resources);
            ConfigureAtlasContract();
            material.SetVector("_WorldRect", FullReceiver);
            material.SetFloat("_StartDepth", .5f);
            material.SetFloat("_UseDerivatives", 1f);
            material.SetFloat("_Owner", 257f);
            material.SetFloat("_Radius", 1f);
            Texture2D edges = Own(MakeAtlas(report.reversedZ, false, false, false), resources);
            Texture2D changedNeighbor = Own(MakeAtlas(report.reversedZ, true, false, false), resources);
            Texture2D sloped = Own(MakeAtlas(report.reversedZ, false, true, false), resources);
            Texture2D occluded = Own(MakeAtlas(report.reversedZ, false, false, true), resources);
            var target = Own(new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear), resources);
            var readback = Own(new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true), resources);
            Require(report, target.Create(), "Float offscreen color target.");

            Color[] Draw(Texture2D atlas, bool soft, string label = null)
            {
                Shader.SetGlobalTexture("_HoCSAtlas", atlas);
                if (soft) material.EnableKeyword(SoftKeyword);
                else material.DisableKeyword(SoftKeyword);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(Texture2D.blackTexture, target, material);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                    readback.Apply(false);
                    if (label != null)
                        File.WriteAllBytes(Path.Combine(output, label + ".exr"), readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                    return readback.GetPixels();
                }
                finally { RenderTexture.active = previous; }
            }

            Color[] hard = Draw(edges, false, "hard-edge");
            Color[] soft = Draw(edges, true, "soft-edge");
            report.hardVisibleLevels = hard.Select(c => c.r).Distinct().Count();
            report.softVisibleLevels = soft.Select(c => c.r).Distinct().Count();
            report.softIntermediatePixels = soft.Count(c => c.r > .25f + Tolerance && c.r < 1f - Tolerance);
            Require(report, hard.All(c => Near(c.r, .25f) || Near(c.r, 1f)) && report.hardVisibleLevels == 2,
                "Hard has exactly blocked and lit plateaus with shadowStrength .75.");
            Require(report, soft.All(c => c.r >= .25f - Tolerance && c.r <= 1f + Tolerance)
                && report.softIntermediatePixels > 0, "Soft has bounded mixed visibility near an occlusion edge.");
            Require(report, hard.All(c => Near(c.b, 1f)) && soft.All(c => Near(c.b, 1f)),
                "Valid group/slot/box survives every interior probe sample.");
            var inside = hard[64 * Size + 64];
            var border = hard[64 * Size + 6];
            float expectedEdge = Mathf.SmoothStep(0f, 1f, (6.5f / Size) / .2f);
            Require(report, Near(inside.g, 1f) && Near(border.g, expectedEdge),
                "Unmixed border weight equals the original local-box smoothstep.");

            material.SetFloat("_Radius", 2f);
            report.tileNeighborDeltaHard = MaxDelta(hard, Draw(changedNeighbor, false));
            var farSoft = Draw(edges, true, "soft-radius-two");
            report.tileNeighborDeltaSoft = MaxDelta(farSoft, Draw(changedNeighbor, true, "soft-adjacent-flipped"));
            Require(report, report.tileNeighborDeltaHard == 0f && report.tileNeighborDeltaSoft == 0f,
                "Flipping all depth outside the tile leaves hard and radius-two soft output exactly unchanged.");
            material.SetFloat("_Radius", 999f);
            Require(report, MaxDelta(farSoft, Draw(edges, true)) == 0f, "Soft radius cap is exactly two texels.");
            material.SetFloat("_Radius", 0f);
            Require(report, MaxDelta(soft, Draw(edges, true)) == 0f, "Soft radius floor is exactly one texel.");
            material.SetFloat("_Radius", 1f);

            foreach (int invalid in new[] { 0, 258, 513 })
            {
                material.SetFloat("_Owner", invalid);
                Require(report, Draw(edges, true).All(Invalid), "Zero owner, unmapped group or masked slot is neutral: " + invalid);
            }
            material.SetFloat("_Owner", 257);
            Shader.SetGlobalFloat("_HoCSActive", 0f);
            Require(report, Draw(edges, true).All(Invalid), "Inactive producer returns (1,0,0).");
            Shader.SetGlobalFloat("_HoCSActive", 1f);
            material.SetVector("_WorldRect", new Vector4(-.4f, .2f, .1f, .6f));
            Require(report, Draw(edges, false).All(Invalid), "Outside frozen local bounds is neutral.");
            material.SetVector("_WorldRect", FullReceiver);
            material.SetFloat("_StartDepth", 1.4f);
            Require(report, Draw(edges, false).All(Invalid), "Outside shadow depth domain is neutral.");
            material.SetFloat("_StartDepth", .5f);

            material.SetFloat("_DepthSlope", Slope);
            var corrected = Draw(sloped, true, "sloped-with-plane");
            var occlusion = Draw(occluded, true, "far-blocker-with-plane");
            material.SetFloat("_UseDerivatives", 0f);
            var uncorrected = Draw(sloped, true, "sloped-without-plane");
            report.selfShadowWithPlane = corrected.Count(c => c.r < .99f);
            report.selfShadowWithoutPlane = uncorrected.Count(c => c.r < .99f);
            Require(report, report.selfShadowWithPlane == 0 && report.selfShadowWithoutPlane > Size * Size / 4,
                "Receiver derivatives eliminate sloped self-shadowing in the controlled agreeing surface.");
            Require(report, occlusion.All(c => Near(c.r, .25f)),
                "Residual guard preserves the blocker outside the receiver plane tolerance.");
            material.SetFloat("_DepthSlope", 0f);
            material.SetFloat("_UseDerivatives", 1f);
            material.EnableKeyword(RuntimeKeyword);
            material.SetFloat("_ProbeSoft", 0f);
            Require(report, MaxDelta(hard, Draw(edges, false)) == 0f, "Runtime hard is the same as specializing hard.");
            material.SetFloat("_ProbeSoft", 1f);
            Require(report, MaxDelta(soft, Draw(edges, false)) == 0f, "Runtime soft is the same as specializing soft.");

            Require(report, !ShaderUtil.ShaderHasError(shader), "Probe reports no shader errors.");
            report.success = true;
            UnityEngine.Debug.Log("HO_CS_FIXED_PCF_PASSED " + report.checks + " " + report.graphics);
        }
        catch (Exception error) { report.error = error.ToString(); throw; }
        finally
        {
            File.WriteAllText(Path.Combine(output, "pcf-validation.json"), JsonUtility.ToJson(report, true));
            ShaderUtil.allowAsyncCompilation = oldAsync;
            Shader.SetGlobalFloat("_HoCSActive", 0f);
            Shader.SetGlobalTexture("_HoCSAtlas", Texture2D.blackTexture);
            foreach (var resource in resources) Object.DestroyImmediate(resource);
        }
    }

    private static Texture2D MakeAtlas(bool reversed, bool neighboringBlocker, bool sloped, bool fullBlocker)
    {
        float blocked = reversed ? .85f : .15f;
        float lit = reversed ? .15f : .85f;
        var values = new float[AtlasSize * AtlasSize];
        for (int y = 0; y < AtlasSize; y++)
        for (int x = 0; x < AtlasSize; x++)
        {
            bool inTile = x >= TileStart && x < TileStart + TileSize && y >= TileStart && y < TileStart + TileSize;
            float val = neighboringBlocker ? blocked : lit;
            if (inTile)
            {
                float wx = (x + .5f - TileStart) / TileSize;
                val = fullBlocker || wx < .5f ? blocked : lit;
                if (sloped)
                    val = .5f + (wx - .5f) * Slope + (reversed ? -.00002f : .00002f);
            }
            values[y * AtlasSize + x] = val;
        }
        var texture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RFloat, false, true)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixelData(values, 0);
        texture.Apply(false);
        return texture;
    }

    private static void ConfigureAtlasContract()
    {
        Shader.SetGlobalFloat("_HoCSActive", 1f);
        Shader.SetGlobalInt("_HoCSCount", 1);
        Shader.SetGlobalVector("_HoCSAtlasSize", new Vector4(1f / AtlasSize, 1f / AtlasSize, AtlasSize, AtlasSize));
        Upload("_HoCSGroupSlices", 64, new Vector4(0f, 1f, 0f, 0f));
        Upload("_HoCSPartMasks", 16 * 64, new Vector4(0f, 1f, 0f, 0f));
        Upload("_HoCSTileRects", 16, new Vector4(.25f, .25f, .5f, .5f));
        Upload("_HoCSParameters", 16, new Vector4(.2f, .75f, 1f, .0005f));
        Upload("_HoCSDepthParameters", 16, new Vector4(2f, 0f, 0f, 0f));
        var matrices = new Matrix4x4[16];
        matrices[0] = Matrix4x4.identity;
        Shader.SetGlobalMatrixArray("_HoCSWorldToShadow", matrices);
        matrices[0] = Matrix4x4.Translate(Vector3.one * -.5f);
        Shader.SetGlobalMatrixArray("_HoCSWorldToBounds", matrices);
    }

    private static void Upload(string name, int size, Vector4 head)
    {
        var buffer = new Vector4[size];
        buffer[0] = head;
        Shader.SetGlobalVectorArray(name, buffer);
    }

    private static void Compile(Shader shader, Report validation, string output)
    {
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        var data = new CompileReport { unity = validation.unity, graphics = validation.graphics, gpu = validation.gpu };
        var variants = new[] { ("hard", Array.Empty<string>()), ("soft", new[] { SoftKeyword }), ("runtime-toggle", new[] { RuntimeKeyword }) };
        foreach (var (label, keywords) in variants)
        {
            var timer = Stopwatch.StartNew();
            var result = pass.CompileVariant(ShaderType.Fragment, keywords, ShaderCompilerPlatform.D3D,
                BuildTarget.StandaloneWindows64, true);
            timer.Stop();
            var bytes = result.ShaderData;
            var row = new CompileRow {
                shader = ShaderName, pass = "Probe", sample = label, keywords = keywords,
                success = result.Success && bytes != null && bytes.Length != 0,
                bytes = bytes?.Length ?? 0, milliseconds = timer.Elapsed.TotalMilliseconds,
                messages = result.Messages?.Select(m => m.severity + ": " + m.message).ToArray() ?? Array.Empty<string>(),
                resources = result.ResourceBindings?.Select(r => r.Kind + ":" + r.Index + ":" + r.SamplerIndex + ":" + r.Name)
                    .ToArray() ?? Array.Empty<string>()
            };
            if (row.success)
                File.WriteAllBytes(Path.Combine(output, data.rows.Count.ToString("D2") + ".dxbc"), bytes);
            data.rows.Add(row);
            File.WriteAllText(Path.Combine(output, "fragment-compile.json"), JsonUtility.ToJson(data, true));
            Require(validation, row.success && row.messages.Length == 0,
                "CompileVariant succeeded without messages: " + label + " " + string.Join("; ", row.messages));
        }
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= Tolerance;
    private static bool Invalid(Color c) => Near(c.r, 1f) && Near(c.g, 0f) && Near(c.b, 0f);
    private static float MaxDelta(Color[] a, Color[] b)
    {
        float value = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            Color d = a[i] - b[i];
            value = Mathf.Max(value, Mathf.Abs(d.r), Mathf.Abs(d.g), Mathf.Abs(d.b), Mathf.Abs(d.a));
        }
        return value;
    }
    private static T Own<T>(T obj, List<Object> list) where T : Object { list.Add(obj); return obj; }
    private static void Require(Report report, bool condition, string description)
    {
        report.checks++;
        if (!condition) throw new InvalidOperationException(description);
        report.results.Add(description);
    }
}
