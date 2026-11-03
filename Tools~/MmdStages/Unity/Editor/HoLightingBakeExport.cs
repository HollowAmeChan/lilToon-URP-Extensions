using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class HoLightingBakeExport
{
    public static string ExportDependencies(string outputPath = null)
    {
        string[] scenes = {
            "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/頑張りたい人へ.unity",
            "Assets/00动画/MMD场景/H2Cu阿相/圣诞万花镜/圣诞万花镜.unity",
            "Assets/00动画/MMD场景/H2Cu阿相/孔雀月令/孔雀月令.unity"
        };
        var roots = new HashSet<string>(scenes, StringComparer.Ordinal) {
            "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset"
        };
        foreach (string settings in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" })
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(settings), @"guid:\s*([0-9a-f]{32})"))
            {
                string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) roots.Add(path);
            }
        var assets = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true), StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles("Assets/00动画/MMD场景/_制作工具", "*", SearchOption.AllDirectories))
            if (!path.EndsWith(".meta", StringComparison.Ordinal)) assets.Add(path.Replace('\\', '/'));
        string output = Path.GetFullPath(string.IsNullOrEmpty(outputPath) ? "Library/HoLightingAudit/bake-export.json" : outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(new Report {
            project = Path.GetFullPath("."), createdUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            assets = assets.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            scenes = scenes.Select(path => new SceneRow { path = path }).ToArray()
        }, true));
        return output;
    }

    [Serializable] public sealed class SceneRow
    {
        public string path;
        public MeshRow[] meshes, skinnedMeshes;
        public string[] materials, lights, cameras;
        public ShaderRow[] shaders;
    }
    [Serializable] public sealed class ShaderRow
    { public string name; public int passes, metaByName, metaByTag; public string[] passNames; public bool supported; }
    [Serializable] public sealed class MeshRow
    {
        public string name, asset, objectPath, receiveGI;
        public int vertices, uv2, users;
        public Vector3 size;
        public bool contributeGI, castShadows, receiveShadows;
    }
    [Serializable] public sealed class Report
    {
        public string project, createdUtc, unity;
        public string[] assets;
        public SceneRow[] scenes;
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Closed-editor batch export only.");
        try
        {
            string[] scenes = {
                "Assets/00动画/MMD动画场景/花吐电気通用动画/Scenes/頑張りたい人へ.unity",
                "Assets/00动画/MMD场景/H2Cu阿相/圣诞万花镜/圣诞万花镜.unity",
                "Assets/00动画/MMD场景/H2Cu阿相/孔雀月令/孔雀月令.unity"
            };
            var assets = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<SceneRow>();
            foreach (string path in scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new IOException(path);
                foreach (string dependency in AssetDatabase.GetDependencies(path, true)) assets.Add(dependency);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                var renderers = roots.SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).ToArray();
                var filters = roots.SelectMany(x => x.GetComponentsInChildren<MeshRenderer>(true))
                    .Select(x => (renderer: x, filter: x.GetComponent<MeshFilter>()))
                    .Where(x => x.filter != null && x.filter.sharedMesh != null).ToArray();
                var skinned = roots.SelectMany(x => x.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .Where(x => x.sharedMesh != null).ToArray();
                rows.Add(new SceneRow {
                    path = path,
                    meshes = filters.Select(x => new MeshRow {
                        name = x.filter.sharedMesh.name, asset = AssetDatabase.GetAssetPath(x.filter.sharedMesh),
                        objectPath = PathOf(x.renderer.transform), vertices = x.filter.sharedMesh.vertexCount,
                        uv2 = x.filter.sharedMesh.uv2.Length, users = 1, size = x.renderer.bounds.size,
                        contributeGI = (GameObjectUtility.GetStaticEditorFlags(x.renderer.gameObject) & StaticEditorFlags.ContributeGI) != 0,
                        receiveGI = x.renderer.receiveGI.ToString(), castShadows = x.renderer.shadowCastingMode != ShadowCastingMode.Off,
                        receiveShadows = x.renderer.receiveShadows
                    }).ToArray(),
                    skinnedMeshes = skinned.Select(x => new MeshRow {
                        name = x.sharedMesh.name, asset = AssetDatabase.GetAssetPath(x.sharedMesh),
                        objectPath = PathOf(x.transform), vertices = x.sharedMesh.vertexCount,
                        uv2 = x.sharedMesh.uv2.Length, users = 1, size = x.bounds.size, receiveGI = "not exposed",
                        contributeGI = (GameObjectUtility.GetStaticEditorFlags(x.gameObject) & StaticEditorFlags.ContributeGI) != 0,
                        castShadows = x.shadowCastingMode != ShadowCastingMode.Off,
                        receiveShadows = x.receiveShadows
                    }).ToArray(),
                    materials = renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).Distinct()
                        .Select(x => AssetDatabase.GetAssetPath(x) + " | shader=" + x.shader.name +
                            " renderQueue=" + x.renderQueue + " GI=" + x.globalIlluminationFlags).ToArray(),
                    shaders = renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).Select(x => x.shader)
                        .Concat(new[] { Shader.Find("lilToon"), Shader.Find("Hidden/lilToonOutline") })
                        .Where(x => x != null).Distinct().Select(shader => {
                            var material = renderers.SelectMany(x => x.sharedMaterials).FirstOrDefault(x => x != null && x.shader == shader);
                            return new ShaderRow { name = shader.name, supported = shader.isSupported,
                                passes = material != null ? material.passCount : -1,
                                metaByName = material != null ? material.FindPass("META") : -1,
                                metaByTag = material != null ? material.FindPass("Meta") : -1,
                                passNames = material == null ? Array.Empty<string>() :
                                    Enumerable.Range(0, material.passCount).Select(material.GetPassName).ToArray() };
                        }).ToArray(),
                    lights = roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).Select(x =>
                        x.name + " enabled=" + (x.enabled && x.gameObject.activeInHierarchy) + " type=" + x.type +
                        " mode=" + x.lightmapBakeType + " shadows=" + x.shadows + " I=" + x.intensity).ToArray(),
                    cameras = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Select(x =>
                        x.name + " pos=" + x.transform.position + " fov=" + x.fieldOfView).ToArray()
                });
            }
            foreach (string path in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset" })
                foreach (string dependency in AssetDatabase.GetDependencies(path, true)) assets.Add(dependency);
            foreach (string settings in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset" })
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(settings), @"guid:\s*([0-9a-f]{32})"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                    if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    foreach (string dependency in AssetDatabase.GetDependencies(path, true)) assets.Add(dependency);
                }
            string tools = "Assets/00动画/MMD场景/_制作工具";
            foreach (string path in Directory.GetFiles(tools, "*", SearchOption.AllDirectories))
                if (!path.EndsWith(".meta", StringComparison.Ordinal)) assets.Add(path.Replace('\\', '/'));
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-hoOutput");
            if (index < 0) throw new ArgumentException("-hoOutput is required.");
            string output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(new Report {
                project = Path.GetFullPath("."), createdUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
                assets = assets.OrderBy(x => x, StringComparer.Ordinal).ToArray(), scenes = rows.ToArray()
            }, true));
            Debug.Log("HO_BAKE_EXPORT=" + output);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static string PathOf(Transform transform)
    {
        var names = new Stack<string>(); for (; transform != null; transform = transform.parent) names.Push(transform.name);
        return string.Join("/", names);
    }
}
