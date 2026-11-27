using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

public static class HoShadowAbiCompileProbe
{
    const string PreviewKey = "_HO_SHADOWS_SCREEN_PREVIEW";
    const string Transparent = "_SURFACE_TYPE_TRANSPARENT";
    static readonly string[] Lit = { "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS", "_SHADOWS_SOFT" };
    static readonly string[] NativeAtlases = { "_MainLightShadowmapTexture", "_AdditionalLightsShadowmapTexture" };
    static readonly string[] LegacyAtlases = { "_HoShadowCastAtlas", "_HoShadowCastSecondDirectionalAtlas", "_HoCSAtlas" };

    [Serializable] sealed class Sample
    {
        public string shader, label, pass;
        public string[] keywords, bindings, messages;
        public bool passed;
        public int dxbcBytes;
        public double milliseconds;
    }

    [Serializable] sealed class Report
    {
        public string pipeline, packages, graphics, unity, error;
        public bool passed;
        public List<Sample> samples = new();
    }

    public static void Run()
    {
        var output = Environment.GetEnvironmentVariable("HO_SHADOW_ABI_OUTPUT");
        var provider = Environment.GetEnvironmentVariable("HO_SHADOW_ABI_PROVIDER");
        var packages = Environment.GetEnvironmentVariable("HO_SHADOW_ABI_PACKAGES") ?? "Hair,Skin,Eye,Cloth,Environment,Effect";
        if (!Application.isBatchMode || !Application.dataPath.StartsWith("D:/Temp/", StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(output) || File.Exists(Path.Combine(output, "shader-shadow-abi.json"))
            || (provider != "stock" && provider != "managed"))
            throw new InvalidOperationException("Use an isolated batch host, fresh output and a stock/managed provider declaration.");
        var report = new Report { pipeline = provider, packages = packages, unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString() };
        var selected = new HashSet<string>(packages.Split(','), StringComparer.Ordinal);
        bool managed = provider == "managed";
        bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            if (selected.Contains("Hair"))
            {
                const string shader = "Ho/Hair/URP/Hair";
                Forward(shader, "hair-opaque", "Forward", "_HoShadowsMainVisibility");
                Forward(shader, "hair-cutout-plus", "Forward", "_HoShadowsMainVisibility", "_ALPHATEST_ON", "_CLUSTER_LIGHT_LOOP");
                Phase(shader, "hair-early", "HAIR", true);
                Phase(shader, "hair-late", "HAIR", false);
                Compile(shader, "hair-oit-native", "HO_OIT", null, false, Lit);
            }
            if (selected.Contains("Skin"))
            {
                const string shader = "Ho/Skin/URP/Stylized";
                Forward(shader, "skin-opaque", "Forward", "_HoShadowsMainVisibility");
                Forward(shader, "skin-capture", "HO_CHARACTER_CAPTURE", "_HoShadowsCaptureFaceVisibility");
            }
            if (selected.Contains("Eye"))
            {
                const string shader = "Ho/Skin/URP/Eye";
                Forward(shader, "eye-opaque", "Forward", "_HoShadowsMainVisibility");
                Phase(shader, "eye-early", "EYE", true);
                Phase(shader, "eye-late", "EYE", false);
                Compile(shader, "eye-transparent-without-domain", "Forward", null, false, Lit.Append(Transparent).ToArray());
            }
            if (selected.Contains("Cloth"))
            {
                const string shader = "Ho/Cloth/URP/Cloth";
                Forward(shader, "cloth-opaque", "Forward", "_HoShadowsMainVisibility");
                Phase(shader, "cloth-early", "CLOTH", true);
                Phase(shader, "cloth-late", "CLOTH", false);
                Forward(shader, "cloth-oit", "HO_OIT", "_HoShadowsOitVisibility0");
            }
            if (selected.Contains("Environment"))
            {
                const string shader = "Ho/Environment/URP/Lit";
                Forward(shader, "environment-opaque", "ForwardLit", "_HoShadowsMainVisibility");
                Compile(shader, "environment-native-transparent", "ForwardLit", null, false, Lit.Append(Transparent).ToArray());
            }
            if (selected.Contains("Effect"))
            {
                const string shader = "Ho/Effect/URP/Layered Toon";
                Forward(shader, "effect-opaque", "FORWARD", "_HoShadowsMainVisibility", "_EFFECT_SCREEN_SHADOW_SURFACE");
                Compile(shader, "effect-native-custom-surface", "FORWARD", null, false, Lit);
            }
            Require(report.samples.Count != 0, "No supported shader selected.");
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            throw;
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = async;
            File.WriteAllText(Path.Combine(output, "shader-shadow-abi.json"), JsonUtility.ToJson(report, true));
        }

        void Forward(string shader, string label, string pass, string binding, params string[] keys) =>
            Compile(shader, label, pass, binding, managed, Lit.Concat(keys).ToArray());

        void Phase(string shader, string label, string family, bool early) =>
            Forward(shader, label, "Forward", early ? "_HoShadowsEarlyLayerVisibility0" : "_HoShadowsLayerVisibility0",
                Transparent, $"_{family}_{(early ? "EARLY_" : "")}TRANSPARENT_SCREEN_SHADOW_SURFACE");

        void Compile(string name, string label, string passName, string binding, bool expectManaged, string[] keys)
        {
            var shader = Shader.Find(name);
            Require(shader != null && !shader.keywordSpace.FindKeyword(PreviewKey).isValid, "Missing release shader or retained preview keyword: " + name);
            var data = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            var pass = Enumerable.Range(0, data.PassCount).Select(data.GetPass).Single(item => item.Name == passName);
            var timer = Stopwatch.StartNew();
            var result = pass.CompileVariant(ShaderType.Fragment, keys, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, true);
            var row = new Sample { shader = name, label = label, pass = passName, keywords = keys, milliseconds = timer.Elapsed.TotalMilliseconds,
                bindings = result.ResourceBindings?.Select(value => value.Name).ToArray() ?? Array.Empty<string>(),
                messages = result.Messages?.Select(value => value.message).ToArray() ?? Array.Empty<string>(), dxbcBytes = result.ShaderData?.Length ?? 0 };
            report.samples.Add(row);
            Require(result.Success && row.dxbcBytes > 0, label + ": " + string.Join(" | ", row.messages));
            File.WriteAllBytes(Path.Combine(output, label + ".dxbc"), result.ShaderData);
            Require(!row.bindings.Intersect(LegacyAtlases).Any(), label + ": retained retired Ho atlas");
            bool hasNative = row.bindings.Intersect(NativeAtlases).Any();
            bool hasManaged = row.bindings.Any(value => value.StartsWith("_HoShadows", StringComparison.Ordinal));
            Require(expectManaged ? !hasNative && row.bindings.Contains(binding) : hasNative && !hasManaged,
                label + ": incorrect provider bindings: " + string.Join(", ", row.bindings));
            row.passed = true;
        }
    }

    static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}
