using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class HoCaptureManagedBytecode
{
    [Serializable] private sealed class Report
    {
        public bool passed;
        public string error;
        public List<Row> variants = new();
    }

    [Serializable] private sealed class Row
    {
        public string shader, artifact;
        public string[] keywords, bindings, messages;
        public int bytes;
    }

    public static bool Compile(string output)
    {
        var report = new Report();
        try
        {
            foreach (var material in new[] { ("Ho/Skin/URP/Stylized", false), ("Ho/Skin/URP/Eye", true) })
            {
                Shader shader = Shader.Find(material.Item1);
                HoCaptureManagedProbe.Require(shader != null, material.Item1);
                var subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
                var pass = Enumerable.Range(0, subshader.PassCount).Select(subshader.GetPass)
                    .Single(pass => pass.Name == "HO_CHARACTER_CAPTURE");
                foreach (bool full in new[] { false, true })
                {
                    var keywords = new List<string>();
                    if (material.Item2) keywords.Add("_SURFACE_TYPE_TRANSPARENT");
                    if (full) keywords.AddRange(new[] { "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS" });
                    else keywords.AddRange(new[] { "_MAIN_LIGHT_SHADOWS", "_SHADOWS_SOFT_HIGH" });
                    string[] key = keywords.ToArray();
                    var result = pass.CompileVariant(ShaderType.Fragment, key, ShaderCompilerPlatform.D3D,
                        BuildTarget.StandaloneWindows64, true);
                    var row = new Row
                    {
                        shader = shader.name,
                        artifact = $"capture-variant-{report.variants.Count:00}.dxbc",
                        keywords = key,
                        bytes = result.ShaderData?.Length ?? 0,
                        bindings = result.ResourceBindings?.Select(resource => $"{resource.Kind}:{resource.Index}:{resource.Name}").ToArray()
                            ?? Array.Empty<string>(),
                        messages = result.Messages?.Select(message => $"{message.severity}: {message.message}").ToArray()
                            ?? Array.Empty<string>()
                    };
                    report.variants.Add(row);
                    HoCaptureManagedProbe.Require(result.Success && row.bytes != 0, row.artifact + " must compile.");
                    File.WriteAllBytes(Path.Combine(output, row.artifact), result.ShaderData);
                    string[] textures = row.bindings.Where(binding => binding.StartsWith("Texture:", StringComparison.Ordinal))
                        .Select(binding => binding.Split(':')[2]).ToArray();
                    HoCaptureManagedProbe.Require(textures.Contains("_HoShadowsCaptureFaceSurface")
                        && textures.Contains("_HoShadowsCaptureEyeSurface")
                        && textures.Contains("_HoShadowsCaptureFaceVisibility")
                        && textures.Contains("_HoShadowsCaptureEyeVisibility")
                        && textures.Contains("_HoShadowsCaptureAdditionalVisibility") == full, row.artifact + " Capture bindings.");
                    HoCaptureManagedProbe.Require(!textures.Any(name => name.Contains("ShadowmapTexture")
                        || name.StartsWith("_HoShadowCast") || name.StartsWith("_HoCSAtlas")
                        || name.StartsWith("_HoRTShadow")), row.artifact + " shadow-atlas binding.");
                    Debug.Log($"CAPTURE_MANAGED_DXBC {shader.name} full={full} {row.bytes} bytes");
                }
            }
            report.passed = true;
        }
        catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
        finally { File.WriteAllText(Path.Combine(output, "capture-managed-dxbc.json"), JsonUtility.ToJson(report, true)); }
        return report.passed;
    }
}
