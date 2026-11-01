using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using lilToon.URP.Extensions.VolumeLight;
using UnityEngine.Rendering.Universal;

// Geometry-based fixture survey. A PMX material can contain many disconnected lamps.
// Only explicit plans are authored; surveying never reapplies a scene recipe.
public static class HoMmdPracticalLighting
{
    static string[] batchStages; static string batchOperation; static int batchStage, batchView, probeFrame;
    public static void EnableLightProbe()
    {
        probeFrame = 0;
        RenderPipelineManager.endCameraRendering -= Probe;
        RenderPipelineManager.endCameraRendering += Probe;
    }
    public static void RunBatch()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 ? args[i + 1] : null; }
            batchStages = Arg("-hoStages")?.Split(',') ?? Directory.GetDirectories(HoMmdStageBuilder.Root)
                .Where(x => File.Exists(x + "/" + System.IO.Path.GetFileName(x) + ".unity")).Select(System.IO.Path.GetFileName).OrderBy(x => x).ToArray();
            batchOperation = Arg("-hoOperation") ?? "before"; batchStage = 0; batchView = 0;
            if (args.Contains("-hoLightProbe")) EnableLightProbe();
            EditorSceneManager.OpenScene("Assets/Editor/MMD\u5236\u4F5C\u9A8C\u8BC1/WTD\u5236\u4F5C\u53C2\u8003/WTD\u9884\u89C8.unity", OpenSceneMode.Single);
            EditorApplication.delayCall += BatchNext;
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void BatchNext()
    {
        if (batchStage >= batchStages.Length) { EditorApplication.Exit(0); return; }
        string stage = batchStages[batchStage]; string scene = HoMmdStageBuilder.Root + stage + "/" + stage + ".unity";
        try
        {
            if (batchOperation == "apply" || batchOperation == "inventory")
            {
                Debug.Log("PRACTICAL_" + batchOperation.ToUpperInvariant() + " " + stage + " " + (batchOperation == "apply" ? Apply(scene) : Inventory(scene)));
                batchStage++; EditorApplication.delayCall += BatchNext; return;
            }
            string output = System.IO.Path.Combine(HoMmdStageWork.Work, "PracticalLighting", stage, batchOperation + (batchView == 0 ? "" : "-view" + batchView) + ".png");
            int view = batchView;
            probeFrame = 0;
            HoMmdStageCapture.Begin(scene, output, 1280, 720, p =>
            {
                Debug.Log("PRACTICAL_CAPTURE " + stage + " " + batchOperation + " view=" + view);
                if (batchOperation == "before" || ++batchView == 3) { batchView = 0; batchStage++; }
                EditorApplication.delayCall += BatchNext;
            }, e => { Debug.LogException(e); EditorApplication.Exit(1); }, camera =>
            {
                if (view == 0) return;
                var other = camera.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true))
                    .First(x => x.name == (view == 1 ? "左侧观察相机" : "右侧观察相机"));
                camera.CopyFrom(other); camera.enabled = false;
                camera.transform.SetPositionAndRotation(other.transform.position, other.transform.rotation);
            }, batchOperation == "before" ? 8 : 16);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    [Serializable] sealed class LightProbe
    { public string scene; public float volumeMultiplier; public string[] sources, media, floors; }
    static void Probe(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game || ++probeFrame != 16) return;
        var roots = camera.gameObject.scene.GetRootGameObjects();
        var volume = VolumeManager.instance.stack.GetComponent<HoVolumeLightVolume>();
        var result = new LightProbe { scene = camera.gameObject.scene.path, volumeMultiplier = volume?.intensity.value ?? -1,
            sources = roots.SelectMany(x => x.GetComponentsInChildren<HoVolumeLightSource>()).Where(x => x.enabled).Select(x =>
                PathOf(x.transform) + " | " + x.Status + " | source=" + x.intensity + " light=" + x.GetComponent<Light>().intensity + " pos=" + x.transform.position).ToArray(),
            media = roots.SelectMany(x => x.GetComponentsInChildren<lilToon.URP.Extensions.Atmosphere.HoAtmosphereRegion>()).Select(x =>
                x.name + " position=" + x.transform.position + " size=" + x.size + " density=" + x.density + " weight=" + x.volumeLightWeight).ToArray(),
            floors = roots.SelectMany(x => x.GetComponentsInChildren<MeshRenderer>()).Where(x => x.sharedMaterial != null &&
                (x.sharedMaterial.name.Contains("地板") || x.sharedMaterial.name.Contains("地面"))).Select(x =>
                    x.name + " bounds=" + x.bounds + " layers=" + x.renderingLayerMask + " color=" + (x.sharedMaterial.HasProperty("_Color") ? x.sharedMaterial.GetColor("_Color").ToString() : "n/a")).ToArray() };
        string path = System.IO.Path.Combine(HoMmdStageWork.Work, "PracticalLighting", camera.gameObject.scene.name, "light-state-view" + batchView + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(result, true));
    }
    [Serializable] public sealed class Part { public Vector3 center, size; public int vertices; }
    [Serializable] public sealed class Surface
    {
        public string path, material, asset;
        public bool emission, fixtureName;
        public Color emissionColor;
        public int vertexCount, componentCount;
        public Bounds bounds;
        public Part[] parts;
        public Color baseColor, vertexColor;
        public string mainTexture;
        public bool reflection;
    }
    [Serializable] public sealed class ExistingLight
    { public string path, type; public Vector3 position, forward; public Color color; public float intensity, range, spotAngle; public bool shadows, enabled, volume; }
    [Serializable] public sealed class SurveyCamera
    { public string name; public Vector3 position, forward; public float fov; public bool main; }
    [Serializable] public sealed class SurveyRegion
    { public string name; public Vector3 position, size; public float density, volumeWeight; }
    [Serializable] public sealed class Survey
    { public string scene; public string[] groups; public Surface[] surfaces; public ExistingLight[] lights;
      public SurveyCamera[] cameras; public SurveyRegion[] regions; public float volumeMultiplier; }
    [Serializable] public sealed class Lamp
    {
        public string name, group, source;
        public Vector3 position, direction = Vector3.down;
        public Color color = Color.white;
        public float power = .5f, range = 2, angle = 65, volume, shadowStrength = .85f;
        public bool spot, shadows, setShadowStrength;
        public bool highShadow;
        public int volumeSteps = 48, volumeShadowResolution = 256;
        public float volumeRadius = .18f, volumeSoftness = .35f;
    }
    [Serializable] public sealed class MaterialEdit
    { public string path; public bool emission = true, skipEmission, enableSpecular, smoothSpecular, setSmoothness; public float smoothness; public Color color; }
    [Serializable] public sealed class RegionEdit
    { public string name; public Vector3 position, size; }
    [Serializable] public sealed class Plan
    { public string note; public Lamp[] lamps; public string[] disableLights, disableVolumeSources; public MaterialEdit[] materials;
      public int volumeLimit = 4; public bool aimCameras, setVolumeIntensity; public Vector3 cameraTarget;
      public float focusDistance, volumeDistance, volumeIntensity; public RegionEdit[] regions; }

    public static string Apply(string scenePath)
    {
        RequireSavedScenes();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        string folder = System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/');
        string planPath = folder + "/Rendering/灯具补光.json";
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
        int added = 0, labels = 0, reused = 0;
        try
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var root = roots.First(x => x.GetComponentInChildren<Hollow.HoMmdStages.HoMmdStageEnvironment>(true) != null);
            var allTransforms = roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(var edit in plan.regions ?? Array.Empty<RegionEdit>())
            {
                var region=roots.SelectMany(x=>x.GetComponentsInChildren<lilToon.URP.Extensions.Atmosphere.HoAtmosphereRegion>(true))
                    .Single(x=>x.name==edit.name);
                if(edit.size.x<=0||edit.size.y<=0||edit.size.z<=0)throw new ArgumentException("Atmosphere size must be positive.");
                region.transform.position=edit.position;region.size=edit.size;
            }
            foreach (string path in plan.disableLights ?? Array.Empty<string>())
            {
                var old = allTransforms.FirstOrDefault(x => PathOf(x) == path)?.GetComponent<Light>();
                if (old != null) old.enabled = false;
            }
            foreach(string path in plan.disableVolumeSources ?? Array.Empty<string>())
            {
                var old=allTransforms.FirstOrDefault(x=>PathOf(x)==path)?.GetComponent<HoVolumeLightSource>();
                if(old!=null)old.enabled=false;
            }
            if(plan.aimCameras)
                foreach(var view in roots.SelectMany(x=>x.GetComponentsInChildren<Camera>(true)))
                    if(view.CompareTag("MainCamera")||view.name=="左侧观察相机"||view.name=="右侧观察相机")
                        view.transform.LookAt(plan.cameraTarget);
            if(plan.focusDistance>0)
                foreach(var volume in roots.SelectMany(x=>x.GetComponentsInChildren<Volume>(true)))
                    if(volume.sharedProfile!=null&&volume.sharedProfile.TryGet<lilToon.URP.Extensions.PostProcessing.ScreenProcessStackVolume>(out var stack))
                    {
                        if(!AssetDatabase.GetAssetPath(volume.sharedProfile).StartsWith(folder+"/",StringComparison.Ordinal))
                            throw new InvalidOperationException("Focus profile belongs to another stage.");
                        foreach(var layer in stack.layers.value.Where(l=>l!=null&&l.effect==lilToon.URP.Extensions.PostProcessing.ScreenProcessEffect.DepthOfField))
                        {var p=layer.parameters0;p.y=plan.focusDistance;layer.parameters0=p;}
                        EditorUtility.SetDirty(stack);EditorUtility.SetDirty(volume.sharedProfile);
                        AssetDatabase.SaveAssetIfDirty(volume.sharedProfile);
                    }
            if(plan.volumeDistance>0 || plan.setVolumeIntensity)
                foreach(var volume in roots.SelectMany(x=>x.GetComponentsInChildren<Volume>(true)))
                    if(volume.sharedProfile!=null&&volume.sharedProfile.TryGet<HoVolumeLightVolume>(out var lighting))
                    {
                        if(!AssetDatabase.GetAssetPath(volume.sharedProfile).StartsWith(folder+"/",StringComparison.Ordinal))
                            throw new InvalidOperationException("Volume lighting profile belongs to another stage.");
                        if(plan.volumeDistance>0)lighting.maxDistance.Override(plan.volumeDistance);
                        if(plan.setVolumeIntensity)lighting.intensity.Override(Mathf.Max(0,plan.volumeIntensity));
                        EditorUtility.SetDirty(lighting);EditorUtility.SetDirty(volume.sharedProfile);
                        AssetDatabase.SaveAssetIfDirty(volume.sharedProfile);
                    }
            var holder = root.transform.Find("11 实景灯具");
            if (holder == null && (plan.lamps?.Length ?? 0) > 0) holder = Child(root.transform, "11 实景灯具");
            if (holder != null && holder.childCount == 0 && (plan.lamps?.Length ?? 0) == 0)
            { UnityEngine.Object.DestroyImmediate(holder.gameObject); holder = null; }
            var camera = roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true)).FirstOrDefault(x => x.CompareTag("MainCamera"));
            var volumeGroups = (plan.lamps ?? Array.Empty<Lamp>()).Where(x => x.volume > 0).GroupBy(x => x.group).ToArray();
            int slotsPerGroup = Mathf.Max(1, plan.volumeLimit / Mathf.Max(1, volumeGroups.Length));
            var volumetric = volumeGroups.SelectMany(group => group
                .OrderBy(x => VolumePriority(x, camera))
                .Take(slotsPerGroup)).Take(plan.volumeLimit).ToHashSet();
            foreach (var lamp in plan.lamps ?? Array.Empty<Lamp>())
            {
                var group = holder.Find(lamp.group); if (group == null) group = Child(holder, lamp.group);
                var node = group.Find(lamp.name);
                if (node == null) { node = Child(group, lamp.name); added++; } else reused++;
                node.position = lamp.position;
                node.rotation = Quaternion.LookRotation(lamp.direction.sqrMagnitude > .001f ? lamp.direction.normalized : Vector3.down,
                    Mathf.Abs(lamp.direction.normalized.y) > .95f ? Vector3.forward : Vector3.up);
                var light = node.GetComponent<Light>();
                if (light == null) light = node.gameObject.AddComponent<Light>();
                light.enabled = true; light.type = lamp.spot ? LightType.Spot : LightType.Point;
                light.color = lamp.color; light.intensity = lamp.power; light.range = lamp.range;
                light.spotAngle = lamp.angle; light.innerSpotAngle = lamp.angle * .6f;
                light.shadows = lamp.shadows ? LightShadows.Soft : LightShadows.None;
                light.shadowStrength = lamp.setShadowStrength ? Mathf.Clamp01(lamp.shadowStrength) : .85f;
                light.shadowBias = .025f; light.shadowNormalBias = .08f; light.shadowNearPlane = .03f;
                var extra = light.GetUniversalAdditionalLightData(); extra.usePipelineSettings = false;
                extra.renderingLayers = (RenderingLayerMask)1u;
                var serializedExtra = new SerializedObject(extra);
                serializedExtra.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = lamp.highShadow ?
                    UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                serializedExtra.ApplyModifiedPropertiesWithoutUndo();
                var source = node.GetComponent<HoVolumeLightSource>();
                if (volumetric.Contains(lamp))
                {
                    if (source == null) source = node.gameObject.AddComponent<HoVolumeLightSource>();
                    source.enabled = true; source.quality = HoVolumeLightQuality.HD;
                    bool advanced=lamp.volumeSteps>0;
                    source.intensity = lamp.volume; source.tint = Color.white; source.stepCount = advanced?lamp.volumeSteps:48;
                    source.shadowResolution = lamp.volumeShadowResolution>0?lamp.volumeShadowResolution:256;
                    source.sourceRadius = advanced?Mathf.Max(.01f,lamp.volumeRadius):.18f; source.edgeSoftness = advanced?lamp.volumeSoftness:.35f;
                }
                else if (source != null) source.enabled = false;
            }
            foreach (var edit in plan.materials ?? Array.Empty<MaterialEdit>())
            {
                if (!edit.path.StartsWith(folder + "/", StringComparison.Ordinal)) throw new ArgumentException("Material must belong to this stage: " + edit.path);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(edit.path);
                if (mat == null) throw new IOException("Missing material " + edit.path);
                if(!edit.skipEmission)
                {
                    if (mat.HasProperty("_UseEmission")) mat.SetFloat("_UseEmission", edit.emission ? 1 : 0);
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", edit.color);
                    if (mat.HasProperty("_EmissionBlend")) mat.SetFloat("_EmissionBlend", 1);
                }
                if(edit.enableSpecular)
                    foreach(string property in new[]{"_UseReflection","_ApplySpecular","_ApplySpecularFA"})
                        if(mat.HasProperty(property))mat.SetFloat(property,1);
                if(edit.smoothSpecular&&mat.HasProperty("_SpecularToon"))mat.SetFloat("_SpecularToon",0);
                if(edit.setSmoothness&&mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",edit.smoothness);
                EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
            }
            // An unshadowed point inside a two-sided paper shade otherwise lights its
            // own surface at near-zero distance and clips it to white. Emission shades
            // the lamp; local practicals light its surroundings. Keep prior key/fill
            // lighting on the emitter while preserving other authored layer bits.
            const uint emitterLayer = 128u;
            var modelRenderers = root.transform.Find("01 场景模型").GetComponentsInChildren<Renderer>(true);
            var emitterRenderers = new HashSet<Renderer>();
            foreach (var lamp in plan.lamps ?? Array.Empty<Lamp>())
            {
                string sourcePath = (lamp.source ?? "").Split(new[] { " [" }, StringSplitOptions.None)[0];
                foreach (var renderer in modelRenderers.Where(x => PathOf(x.transform) == sourcePath || x.sharedMaterials.Any(m => m != null && m.name == sourcePath)))
                    if (renderer.sharedMaterials.Any(m => m != null && m.HasProperty("_UseEmission") && m.GetFloat("_UseEmission") > .5f)) emitterRenderers.Add(renderer);
            }
            if (emitterRenderers.Count > 0)
            {
                foreach (var renderer in emitterRenderers) renderer.renderingLayerMask = (renderer.renderingLayerMask & ~1u) | emitterLayer;
                foreach (var old in roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).Where(x => !x.transform.IsChildOf(holder)))
                {
                    var extra = old.GetUniversalAdditionalLightData(); uint mask = (uint)extra.renderingLayers;
                    if ((mask & 1u) != 0) extra.renderingLayers = (RenderingLayerMask)(mask | emitterLayer);
                }
            }
            // Saving first allocates stable fileIDs for vHierarchy's GlobalObjectIDs.
            EditorSceneManager.SaveScene(scene);
            foreach (var t in roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)))
            {
                if (t.GetComponent<Renderer>() != null) continue;
                string path = PathOf(t);
                int color = 0; string icon = "Folder Icon";
                if (t.parent == null) color = 8;
                else if (t.name.Contains("反射")) { color = 8; icon = "ReflectionProbe Icon"; }
                else if (t.GetComponent<Camera>() != null || t.name.Contains("相机") || t.name.Contains("机位")) { color = 7; icon = "Camera Icon"; }
                else if (t.name.Contains("环境") || t.name.Contains("介质") || t.name.Contains("天空")) color = 6;
                else if (t.name.Contains("模型")) color = 5;
                else if (t.GetComponent<Light>() == null && (t.parent == root.transform || path.Contains("11 实景灯具"))) color = 3;
                if (color != 0 && Mark(t.gameObject, color, icon)) labels++;
            }
            SaveHierarchyData();
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            string report = JsonUtility.ToJson(new ApplyResult { scene = scenePath, newLights = added, updatedLights = reused,
                planLights = plan.lamps?.Length ?? 0, volumeSources = volumetric.Count, hierarchyLabels = labels }, true);
            string reportPath = System.IO.Path.Combine(HoMmdStageWork.Work, "PracticalLighting", scene.name, "applied.json");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(reportPath)); File.WriteAllText(reportPath, report);
            return report;
        }
        finally { if (setup.Any(x => x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }
    [Serializable] sealed class ApplyResult { public string scene; public int newLights, updatedLights, planLights, volumeSources, hierarchyLabels; }
    static Transform Child(Transform parent, string name) { var go = new GameObject(name).transform; go.SetParent(parent, false); return go; }
    static float VolumePriority(Lamp lamp, Camera camera)
    {
        if (camera == null) return lamp.position.magnitude;
        var midpoint = lamp.position + (lamp.spot ? lamp.direction.normalized * lamp.range * .5f : Vector3.zero);
        var p = camera.WorldToViewportPoint(midpoint);
        bool visible = p.z > 1 && p.x > .05f && p.x < .95f && p.y > .05f && p.y < .95f;
        return (visible ? 0 : 1000) + Mathf.Abs(p.z - 15) * .025f + Mathf.Abs(p.x - .5f) + Mathf.Abs(p.y - .5f) * .25f;
    }
    static Type HierarchyType => AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("VHierarchy.VHierarchy")).FirstOrDefault(x => x != null);
    static bool Mark(GameObject go, int color, string icon)
    {
        var type = HierarchyType; if (type == null) return false;
        var data = type.GetMethod("GetGameObjectData", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { go, true });
        if (data == null) return false;
        var field = data.GetType().GetField("colorIndex");
        if ((int)field.GetValue(data) != 0) return false; // Preserve artist-selected labels.
        field.SetValue(data, color); data.GetType().GetField("iconNameOrGuid").SetValue(data, icon);
        return true;
    }
    static void SaveHierarchyData()
    {
        var type = HierarchyType; if (type == null) return;
        var data = type.GetField("data", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as ScriptableObject;
        if (data != null) { EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); }
        foreach (string name in new[] { "goInfoCache", "goDataCache" })
            (type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IDictionary)?.Clear();
        EditorApplication.RepaintHierarchyWindow();
    }

    public static string Inventory(string scenePath, bool includeAll = false)
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        RequireSavedScenes();
        try
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var surfaces = new List<Surface>();
            foreach (var root in roots)
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!PathOf(renderer.transform).Contains("01 场景模型")) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int sub = 0; sub < renderer.sharedMaterials.Length; sub++)
                {
                    var mat = renderer.sharedMaterials[sub];
                    if (mat == null) continue;
                    bool named = IsFixture(mat.name + " " + renderer.name);
                    bool emission = mat.HasProperty("_UseEmission") && mat.GetFloat("_UseEmission") > .5f;
                    if (!includeAll && !named && !emission) continue;
                    var parts = Components(mesh, renderer.transform, Mathf.Min(sub, mesh.subMeshCount - 1));
                    surfaces.Add(new Surface { path = PathOf(renderer.transform), material = mat.name,
                        asset = AssetDatabase.GetAssetPath(mat), fixtureName = named, emission = emission,
                        emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black,
                        bounds = renderer.bounds, vertexCount = mesh.vertexCount, componentCount = parts.Length, parts = parts,
                        baseColor=mat.HasProperty("_Color")?mat.GetColor("_Color"):Color.white,
                        vertexColor=mesh.colors.Length>0?mesh.colors[0]:Color.white,
                        mainTexture=AssetDatabase.GetAssetPath(mat.HasProperty("_MainTex")?mat.GetTexture("_MainTex"):null),
                        reflection=mat.HasProperty("_UseReflection")&&mat.GetFloat("_UseReflection")>.5f });
                }
            }
            var lights = roots.SelectMany(x => x.GetComponentsInChildren<Light>(true)).Select(x => new ExistingLight {
                path = PathOf(x.transform), type = x.type.ToString(), position = x.transform.position, forward = x.transform.forward,
                color = x.color, intensity = x.intensity, range = x.range, spotAngle = x.spotAngle,
                shadows = x.shadows != LightShadows.None, enabled = x.isActiveAndEnabled,
                volume = x.GetComponent<lilToon.URP.Extensions.VolumeLight.HoVolumeLightSource>() != null }).ToArray();
            var survey = new Survey { scene = scenePath, surfaces = surfaces.ToArray(), lights = lights,
                cameras=roots.SelectMany(x=>x.GetComponentsInChildren<Camera>(true)).Select(x=>new SurveyCamera {
                    name=x.name,position=x.transform.position,forward=x.transform.forward,main=x.CompareTag("MainCamera"),fov=x.fieldOfView
                }).ToArray(),
                regions=roots.SelectMany(x=>x.GetComponentsInChildren<lilToon.URP.Extensions.Atmosphere.HoAtmosphereRegion>(true)).Select(x=>new SurveyRegion {
                    name=x.name,position=x.transform.position,size=x.size,density=x.density,volumeWeight=x.volumeLightWeight
                }).ToArray(),
                groups = roots.SelectMany(x => x.GetComponentsInChildren<Transform>(true)).Where(x => x.GetComponent<Renderer>() == null &&
                    (x.parent == null || x.parent.parent == null)).Select(x => PathOf(x)).ToArray() };
            foreach(var volume in roots.SelectMany(x=>x.GetComponentsInChildren<Volume>(true)))
                if(volume.sharedProfile!=null&&volume.sharedProfile.TryGet<HoVolumeLightVolume>(out var vl))
                    survey.volumeMultiplier=vl.intensity.value;
            string path = System.IO.Path.Combine(HoMmdStageWork.Work, "PracticalLighting", scene.name, "inventory.json");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(survey, true));
            return path;
        }
        finally { if (setup.Any(x => x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    static bool IsFixture(string value) => new[] { "灯", "燈", "烛", "燭", "lamp", "bulb", "candle", "chandelier", "light" }
        .Any(x => value.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
    public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    static void RequireSavedScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("先保存当前场景，再调查/编辑其他场景。");
    }
    static Part[] Components(Mesh mesh, Transform transform, int submesh)
    {
        var positions = mesh.vertices;
        var indices = mesh.GetIndices(submesh);
        var parent = Enumerable.Range(0, positions.Length).ToArray();
        var used = new HashSet<int>(indices);
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        var weld = new Dictionary<Vector3Int, int>();
        foreach (int index in used)
        {
            var p = positions[index];
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 2000), Mathf.RoundToInt(p.y * 2000), Mathf.RoundToInt(p.z * 2000));
            if (weld.TryGetValue(key, out int other)) Join(index, other); else weld[key] = index;
        }
        for (int i = 0; i + 2 < indices.Length; i += 3) { Join(indices[i], indices[i + 1]); Join(indices[i], indices[i + 2]); }
        var bounds = new Dictionary<int, Bounds>(); var counts = new Dictionary<int, int>();
        foreach (int index in used)
        {
            int key = Find(index); var p = transform.TransformPoint(positions[index]);
            if (bounds.TryGetValue(key, out var b)) { b.Encapsulate(p); bounds[key] = b; counts[key]++; }
            else { bounds[key] = new Bounds(p, Vector3.zero); counts[key] = 1; }
        }
        return bounds.Select(x => new Part { center = x.Value.center, size = x.Value.size, vertices = counts[x.Key] })
            .OrderBy(x => x.center.y).ThenBy(x => x.center.z).ThenBy(x => x.center.x).ToArray();
    }
}
