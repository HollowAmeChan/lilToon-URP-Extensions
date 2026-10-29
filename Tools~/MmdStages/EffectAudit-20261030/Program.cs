using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Hollow.HoUnityTools.Editor.Pmx;

internal static class Program
{
    private const string ToolVersion = "effect-manifest-1";

    // These are the keys consumed by HoPmxRayReader. Any other macro is retained as
    // an unknown source field so an MME package can be audited without executing FX.
    private static readonly HashSet<string> KnownRayKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "ALBEDO_MAP_FROM", "ALBEDO_MAP_APPLY_DIFFUSE", "ALBEDO_MAP_APPLY_SCALE", "ALBEDO_MAP_UV_FLIP", "albedo", "albedoMapLoopNum",
        "NORMAL_MAP_FROM", "NORMAL_MAP_TYPE", "NORMAL_MAP_UV_FLIP", "normalMapScale", "normalMapLoopNum", "NORMAL_MAP_FILE",
        "METALNESS_MAP_FROM", "METALNESS_MAP_SWIZZLE", "METALNESS_MAP_APPLY_SCALE", "METALNESS_MAP_UV_FLIP", "metalness", "metalnessMapLoopNum", "METALNESS_MAP_FILE",
        "SMOOTHNESS_MAP_FROM", "SMOOTHNESS_MAP_SWIZZLE", "SMOOTHNESS_MAP_TYPE", "SMOOTHNESS_MAP_APPLY_SCALE", "SMOOTHNESS_MAP_UV_FLIP", "smoothness", "smoothnessMapLoopNum", "SMOOTHNESS_MAP_FILE",
        "ALPHA_MAP_FROM", "ALPHA_MAP_SWIZZLE", "ALPHA_THRESHOLD", "alpha", "ALPHA_MAP_FILE",
        "EMISSIVE_ENABLE", "EMISSIVE_MAP_FROM", "EMISSIVE_MAP_FILE", "EMISSIVE_MAP_APPLY_MORPH_COLOR", "EMISSIVE_MAP_APPLY_MORPH_INTENSITY", "EMISSIVE_MAP_APPLY_BLINK", "emissive", "emissiveIntensity",
        "NORMAL_SUB_MAP_FROM", "NORMAL_SUB_MAP_FILE", "ALBEDO_SUB_ENABLE", "CUSTOM_ENABLE", "PARALLAX_MAP_FROM", "PARALLAX_MAP_FILE", "OCCLUSION_MAP_FROM", "OCCLUSION_MAP_FILE"
    };

    private static int Main(string[] args)
    {
        try
        {
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string root = null, output = null;
            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i])
                {
                    case "--root":
                        root = Path.GetFullPath(args[++i]);
                        foreach (string file in Directory.GetFiles(root, "*.pmx", SearchOption.AllDirectories)) files.Add(file);
                        break;
                    case "--file": files.Add(Path.GetFullPath(args[++i])); break;
                    case "--output": output = Path.GetFullPath(args[++i]); break;
                    default: throw new ArgumentException("Unknown argument: " + args[i]);
                }
            }
            if (root == null && files.Count > 0) root = CommonRoot(files);
            var models = new List<object>();
            var failures = new List<object>();
            foreach (string path in files)
            {
                try
                {
                    models.Add(Audit(path, root));
                    Console.WriteLine("PASS " + path);
                }
                catch (Exception exception)
                {
                    failures.Add(new { path, error = exception.Message });
                    Console.WriteLine("FAIL " + path + ": " + exception.Message);
                }
                GC.Collect();
            }
            var manifest = new
            {
                schema = "HoPmxEffectManifest/1",
                toolVersion = ToolVersion,
                method = "Pure C# PMX + optional Ray EMD/FX source audit; no Unity launch and no source modification",
                root = root == null ? null : Normalize(root),
                files = models,
                failures
            };
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (output != null)
            {
                string directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(output, json);
            }
            Console.WriteLine(json);
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine("Usage: dotnet run --project Tools~/MmdStages/EffectAudit-20261030 -- [--root <dir>] [--file <model.pmx>] [--output <manifest.json>]");
            return 2;
        }
    }

    private static object Audit(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo before = new FileInfo(fullPath);
        string hash = Sha256(fullPath);
        HoPmxDocument document = HoPmxReader.Read(fullPath);
        FileInfo after = new FileInfo(fullPath);
        if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            throw new IOException("Source changed during audit.");

        HoPmxRayDocument ray = HoPmxRayReader.Read(fullPath);
        var materials = new List<object>();
        for (int i = 0; i < document.Materials.Count; ++i)
        {
            HoPmxMaterial material = document.Materials[i];
            HoPmxRayMaterial rayMaterial;
            ray.Materials.TryGetValue(i, out rayMaterial);
            string baseTexture = TextureReference(document, material.TextureIndex);
            string toonTexture = material.SharedToon ? null : TextureReference(document, material.ToonIndex);
            string sphereTexture = TextureReference(document, material.SphereTextureIndex);
            materials.Add(new
            {
                index = i,
                name = material.Name,
                englishName = material.EnglishName,
                memo = material.Comment,
                indexCount = material.IndexCount,
                shininess = material.Shininess,
                smoothnessFromShininess = HoPmxRayMaterial.ShininessToSmoothness(material.Shininess),
                sourceFlags = new
                {
                    value = material.Flags,
                    hex = "0x" + material.Flags.ToString("X2", CultureInfo.InvariantCulture),
                    names = PmxFlagNames(material.Flags)
                },
                alpha = new
                {
                    diffuse = material.Diffuse.W,
                    inferred = material.Diffuse.W < 0.999f ? "Transparent" : "UnknownWithoutTextureDecode",
                    rayMode = rayMaterial == null ? null : rayMaterial.AlphaMode,
                    rayCutoff = rayMaterial == null ? (float?)null : rayMaterial.Cutoff,
                    rayAlpha = rayMaterial == null ? (float?)null : rayMaterial.Alpha,
                    rayMapFrom = rayMaterial == null ? (int?)null : rayMaterial.AlphaMapFrom,
                    rayTexture = rayMaterial == null ? null : RelativeToPackage(rayMaterial.AlphaTexturePath, Path.GetDirectoryName(fullPath))
                },
                textures = new
                {
                    baseTexture,
                    toon = new { shared = material.SharedToon, index = material.ToonIndex, path = toonTexture },
                    sphere = new { mode = material.SphereMode, index = material.SphereTextureIndex, path = sphereTexture }
                },
                emission = rayMaterial == null ? null : new
                {
                    enabled = rayMaterial.EmissionEnabled,
                    mapFrom = rayMaterial.EmissionMapFrom,
                    texture = RelativeToPackage(rayMaterial.EmissionTexturePath, Path.GetDirectoryName(fullPath)),
                    color = rayMaterial.EmissionColor,
                    intensity = rayMaterial.EmissionIntensity
                },
                mme = rayMaterial == null ? null : new
                {
                    fx = RelativeToPackage(rayMaterial.FxPath, Path.GetDirectoryName(fullPath)),
                    recognizedTemplate = rayMaterial.RecognizedTemplate,
                    unknownFields = rayMaterial.Macros.Where(x => !KnownRayKeys.Contains(x.Key)).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                }
            });
        }

        return new
        {
            path = RelativeOrAbsolute(fullPath, root),
            sourceHashSha256 = hash,
            source = new { name = document.Name, englishName = document.EnglishName, version = document.Version },
            counts = new { vertices = document.Vertices.Count, triangles = document.Indices.Length / 3, materials = document.Materials.Count, bones = document.Bones.Count, morphs = document.Morphs.Count, rigidBodies = document.RigidBodyCount, joints = document.JointCount, softBodies = document.SoftBodyCount },
            textures = document.Textures.Select((value, index) => new { index, path = value, normalizedPath = NormalizeReference(value) }).ToList(),
            materials,
            mme = new { emd = RelativeToPackage(ray.EmdPath, Path.GetDirectoryName(fullPath)), dependencies = ray.Dependencies.Select(x => RelativeToPackage(x, Path.GetDirectoryName(fullPath))).ToList(), warnings = ray.Warnings },
            parserWarnings = document.Warnings
        };
    }

    private static string TextureReference(HoPmxDocument document, int index)
    { return index < 0 || index >= document.Textures.Count ? null : NormalizeReference(document.Textures[index]); }

    private static string[] PmxFlagNames(byte flags)
    {
        var names = new List<string>();
        if ((flags & 1) != 0) names.Add("NoCull");
        if ((flags & 2) != 0) names.Add("GroundShadow");
        if ((flags & 4) != 0) names.Add("SelfShadowMap");
        if ((flags & 8) != 0) names.Add("SelfShadow");
        if ((flags & 16) != 0) names.Add("Edge");
        if ((flags & 32) != 0) names.Add("VertexColor");
        if ((flags & 64) != 0) names.Add("Point");
        if ((flags & 128) != 0) names.Add("Line");
        return names.ToArray();
    }

    private static string Sha256(string path)
    {
        using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    private static string RelativeOrAbsolute(string path, string root)
    { return root == null ? Normalize(path) : Normalize(Path.GetRelativePath(root, path)); }

    private static string RelativeToPackage(string path, string packageDirectory)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try { return Normalize(Path.GetRelativePath(packageDirectory, path)); }
        catch (ArgumentException) { return Normalize(path); }
    }

    private static string NormalizeReference(string value)
    { return string.IsNullOrEmpty(value) ? value : value.Replace('\\', '/'); }

    private static string Normalize(string value)
    { return value.Replace('\\', '/'); }

    private static string CommonRoot(IEnumerable<string> files)
    {
        string[] paths = files.Select(Path.GetDirectoryName).ToArray();
        if (paths.Length == 0) return null;
        string root = paths[0];
        foreach (string path in paths.Skip(1))
            while (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(root);
                if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) return root;
                root = parent;
            }
        return root;
    }
}
