using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.RayTracing;
using lilToon.URP.Extensions.RTDI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace lilToon.URP.Extensions.Editor.RTDI
{
    // Read-only source-scene audit, run against a copied dependency closure in an isolated project.
    // No renderer Feature, asset repair, scene save, or claim of final image quality.
    public static class HoRTDISceneValidation
    {
        [Serializable] private sealed class Report
        {
            public string gpu, scene;
            public int instances, skinnedSnapshots, cutoutInstances;
            public bool skyShellExcluded;
            public uint bakes, stableBakes;
            public long ownedMeshBytes;
            public double firstSynchronizeMs, stableSynchronizeMeanMs;
            public RendererRow[] renderers;
            public LightRow[] lights;
            public string[] rejections;
        }
        [Serializable] private sealed class RendererRow
        {
            public string name, type, mesh;
            public bool enabled, active, staticBatch;
            public int submeshes, triangles;
            public MaterialRow[] materials;
        }
        [Serializable] private sealed class MaterialRow
        {
            public string name, shader, renderType, alphaMode;
            public int queue, triangles;
            public float cutoff;
        }
        [Serializable] private sealed class LightRow
        {
            public string name, type, bake;
            public bool enabled, active, cookie, supported;
            public int cullingMask;
            public float intensity, range;
        }

        public static void AuditBloodMoonCastle() => Audit(false);
        public static void AuditBloodMoonCastleWithoutSkyShell() => Audit(true);
        private static void Audit(bool excludeSkyShell)
        {
            if (!Application.dataPath.Replace('\\', '/').Contains("/.codex-research/"))
                throw new InvalidOperationException("Copy the scene dependencies to an isolated project first.");
            int exit = 0;
            var excluded=new List<Material>();
            try
            {
                if (!HoRTScene.IsSupported) throw new NotSupportedException("D3D12 inline tracing required for registry audit.");
                const string scenePath = "Assets/mmd场景测试/血月古堡/血月古堡.unity";
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                if(excludeSkyShell)foreach(var renderer in renderers)foreach(var material in renderer.sharedMaterials)if(material!=null&&material.name=="天空"&&!excluded.Contains(material))excluded.Add(material);
                var visibilityShader=AssetDatabase.LoadAssetAtPath<Shader>("Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/RTDI/Shaders/HoRTDIVisibilityMaterial.shader");
                if(visibilityShader==null)throw new Exception("Visibility material shader missing");
                using var visibility=new HoRTDIVisibilityMaterials(visibilityShader);
                using var registry = new HoRTScene(visibility.Resolve,HoRTCutoutSupport.Supports);
                visibility.BeginFrame();var timer = System.Diagnostics.Stopwatch.StartNew(); registry.Synchronize(renderers,0xff,excluded);visibility.EndFrame();timer.Stop();
                double initialMs = timer.Elapsed.TotalMilliseconds; uint initialBakes = registry.SkinnedBakeCount, revision = registry.Revision;
                timer.Restart(); for (int i = 0; i < 64; ++i){visibility.BeginFrame();registry.Synchronize(renderers,0xff,excluded);visibility.EndFrame();} timer.Stop();
                if (registry.SkinnedBakeCount != initialBakes || registry.Revision != revision)
                    throw new Exception("Static source scene must reuse pose and registry revision.");
                // IDs start at one in this fresh registry and no entries have been removed.
                var owned = new HashSet<Mesh>();
                for (uint id = 1; id <= registry.InstanceCount; ++id)
                    if (registry.TryResolve(id, out var instance) && instance.renderer is SkinnedMeshRenderer) owned.Add(instance.mesh);
                var report = new Report
                {
                    scene = scenePath, gpu = SystemInfo.graphicsDeviceType.ToString(), instances = registry.InstanceCount,
                    cutoutInstances=registry.CutoutCount,skyShellExcluded=excludeSkyShell,
                    skinnedSnapshots = registry.SkinnedMeshCount, bakes = initialBakes, stableBakes = registry.SkinnedBakeCount - initialBakes,
                    firstSynchronizeMs = initialMs, stableSynchronizeMeanMs = timer.Elapsed.TotalMilliseconds / 64,
                    ownedMeshBytes = owned.Sum(Profiler.GetRuntimeMemorySizeLong), rejections = registry.Rejections.ToArray(),
                    renderers = renderers.Select(RendererReport).ToArray(),
                    lights = lights.Select(l => new LightRow
                    {
                        name = l.name, type = l.type.ToString(), bake = l.lightmapBakeType.ToString(), enabled = l.enabled,
                        active = l.gameObject.activeInHierarchy, cookie = l.cookie != null, cullingMask = l.cullingMask,
                        intensity = l.intensity, range = l.range,
                        supported = l.cookie == null && l.cullingMask == -1 && l.lightmapBakeType != LightmapBakeType.Baked
                            && (l.type == LightType.Point || l.type == LightType.Spot || l.type == LightType.Directional)
                    }).ToArray()
                };
                File.WriteAllText(Path.Combine(Application.dataPath, excludeSkyShell?"../rtdi-blood-moon-castle-without-sky-audit.json":"../rtdi-blood-moon-castle-audit.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"[HoRTDISceneValidation] PASS registry audit: {report.instances} submeshes, {report.skinnedSnapshots} skin snapshots, {report.rejections.Length} exclusions; no image-quality assertion");
            }
            catch (Exception error) { Debug.LogException(error); exit = 1; }
            EditorApplication.Exit(exit);
        }

        private static RendererRow RendererReport(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>(); var skin = renderer as SkinnedMeshRenderer;
            var mesh = filter != null ? filter.sharedMesh : skin != null ? skin.sharedMesh : null;
            return new RendererRow
            {
                name = renderer.name, type = renderer.GetType().Name, enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy,
                mesh = mesh != null ? mesh.name : "", submeshes = mesh != null ? mesh.subMeshCount : 0,
                triangles = mesh != null ? Enumerable.Range(0, mesh.subMeshCount).Sum(i => (int)mesh.GetIndexCount(i) / 3) : 0,
                staticBatch = renderer.isPartOfStaticBatch,
                materials = renderer.sharedMaterials.Select((m, i) => m == null ? new MaterialRow { name = "MISSING" } : new MaterialRow
                {
                    name = m.name, shader = m.shader != null ? m.shader.name : "MISSING", queue = m.renderQueue,
                    renderType = m.GetTag("RenderType", false), alphaMode = m.GetTag("HO_AlphaModeHint", false),
                    cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0,
                    triangles = mesh != null && i < mesh.subMeshCount ? (int)mesh.GetIndexCount(i) / 3 : 0
                }).ToArray()
            };
        }
    }
}
