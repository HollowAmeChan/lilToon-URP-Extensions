using System;
using System.IO;
using System.Linq;
using Hollow.HoMmdStages;
using Hollow.HoUnityTools.Editor.Pmx;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.Editor.MmdMotion;
using Object = UnityEngine.Object;

/// <summary>Generic PMX starter and capture-only review. Existing H2Cu recipes are not involved.</summary>
public static class HoMmdSceneWorkshop
{
    // Closed-editor CLI: -executeMethod HoMmdSceneWorkshop.RunBatch -hoRequest <json>, without -quit for capture.
    public static void RunBatch()
    {
        try
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-hoRequest");
            if (index < 0) throw new ArgumentException("Provide -hoRequest <JSON file>.");
            var request = JsonUtility.FromJson<HoMmdStageWork.Request>(File.ReadAllText(args[index + 1]));
            if (args.Contains("-hoLightProbe")) HoMmdPracticalLighting.EnableLightProbe();
            Action<string> done = path => { Debug.Log("HO_WORKSHOP_RESULT=" + path); EditorApplication.Exit(0); };
            Action<Exception> fail = e => { Debug.LogException(e); EditorApplication.Exit(1); };
            if (request.action == "review") EditorApplication.delayCall += () => { try { Review(request, done, fail); } catch (Exception e) { fail(e); } };
            else if (request.action == "render") EditorApplication.delayCall += () => { try { RenderOne(request, done, fail); } catch (Exception e) { fail(e); } };
            else if (request.action == "create") done(Create(request));
            else if (request.action == "logo") done(HoMmdStageBranding.SetLogos(
                string.IsNullOrEmpty(request.stageFolder) ? "Assets/00动画/MMD场景" : request.stageFolder, request.logoY, request.logoSize));
            else if (request.action == "practicals") done(HoMmdPracticalLighting.Apply(AssetPath(request.scenePath)));
            else if (request.action == "stage-survey") done(HoMmdPracticalLighting.Inventory(AssetPath(request.scenePath), true));
            else if (request.action == "post-inventory" || request.action == "post-migrate" ||
                     request.action == "post-tonemap" || request.action == "post-tune")
                done(HoMmdHoPostReview.Execute(request));
            else throw new ArgumentException("Unsupported workshop action " + request.action);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static string Create(HoMmdStageWork.Request request)
    {
        string folder = AssetPath(request.stageFolder).TrimEnd('/');
        string name = Path.GetFileName(folder);
        string scenePath = folder + "/" + name + ".unity";
        if (File.Exists(scenePath)) throw new IOException("Scene exists; edit it directly: " + scenePath);
        var paths = request.pmxPaths?.Select(AssetPath).Distinct().ToArray();
        if (paths == null || paths.Length == 0) throw new ArgumentException("Provide one or more --pmx assets under this stage's Source folder.");
        foreach (string path in paths)
            if (!path.StartsWith(folder + "/Source/", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ArgumentException("Copy the complete package into " + folder + "/Source first: " + path);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HoMmdStageBuilder.FixedPipelinePath);
        if (pipeline == null) throw new IOException("Missing fixed project pipeline: " + HoMmdStageBuilder.FixedPipelinePath);
        foreach (string child in new[] { "Materials", "Rendering", "Renders" }) Directory.CreateDirectory(folder + "/" + child);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string path in paths)
        {
            var importer = AssetImporter.GetAtPath(path) as HoPmxImporter;
            if (importer == null) throw new InvalidOperationException("Ho PMX importer unavailable: " + path);
            if (importer.generateOutline) { importer.generateOutline = false; importer.SaveAndReimport(); }
            HoPmxMaterialExtraction.Extract(path, folder + "/Materials");
        }
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject(name);
            var models = Child(root.transform, "01 场景模型");
            foreach (string path in paths)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) throw new InvalidOperationException("PMX has no imported model: " + path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                instance.transform.SetParent(models, false);
            }
            var renderers = models.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one * 5);
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            }
            float radius = Mathf.Max(2, bounds.extents.magnitude);
            var lights = Child(root.transform, "02 灯光");
            var key = Child(lights, "主光 · 初始日光").gameObject.AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.5f; key.color = new Color(1, .92f, .82f);
            key.transform.rotation = Quaternion.Euler(40, -35, 0); key.shadows = LightShadows.Soft;
            var cameras = Child(root.transform, "03 相机");
            Camera main = null;
            for (int i = 0; i < 3; i++)
            {
                var camera = Child(cameras, i == 0 ? "主相机" : i == 1 ? "左侧观察相机" : "右侧观察相机").gameObject.AddComponent<Camera>();
                camera.transform.position = bounds.center + Quaternion.Euler(0, i == 1 ? -25 : i == 2 ? 25 : 0, 0) * new Vector3(0, radius * .3f, -radius * 2.1f);
                camera.transform.LookAt(bounds.center); camera.fieldOfView = 50;
                camera.nearClipPlane = .05f; camera.farClipPlane = Mathf.Max(250, radius * 8); camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << 31;
                camera.enabled = i == 0;
                if (i == 0) { main = camera; camera.tag = "MainCamera"; }
            }
            var environment = Child(root.transform, "04 环境与后处理").gameObject.AddComponent<HoMmdStageEnvironment>();
            environment.gameObject.layer = 31; environment.pipeline = pipeline; environment.sun = key; environment.fog = false;
            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "初始天空" };
            AssetDatabase.CreateAsset(sky, folder + "/Rendering/天空.mat"); environment.skybox = sky;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, folder + "/Rendering/后处理.asset");
            Add<ScreenProcessStackVolume>(profile);
            var image = Add<ImageProcessStackVolume>(profile);
            image.Enable.Override(true);
            image.ReplaceBuiltinPostProcessing.Override(true);
            image.layers.overrideState = true;
            image.layers.value.Add(new ImageProcessLayer {
                name = "Ho 发光 · 初始柔和", effect = ImageProcessEffect.Glow,
                parameters0 = new Vector4(1, .5f, 2, 0),
                parameters1 = new Vector4(.15f, 0, 0, 1),
                parameters2 = new Vector4(3, 180, 0, 0)
            });
            EditorUtility.SetDirty(image);
            if (!string.IsNullOrEmpty(request.logoPath))
            {
                string logoPath = folder + "/Rendering/Hollow Logo.png";
                if (!AssetDatabase.CopyAsset(AssetPath(request.logoPath), logoPath)) throw new IOException("Cannot copy logo: " + request.logoPath);
                image.layers.overrideState = true;
                image.layers.value.Add(new ImageProcessLayer { name = "电影画幅", effect = ImageProcessEffect.CinematicBars,
                    color = Color.black, parameters0 = new Vector4(2.39f, 0, 0, 0) });
                image.layers.value.Add(new ImageProcessLayer { name = "Hollow · 底部正中", effect = ImageProcessEffect.LogoOverlay,
                    logoTexture0 = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath), color = Color.white,
                    parameters0 = new Vector4(.5f, .1f, .3f, 1), parameters8 = new Vector4(0,1,2,3), parameters9 = new Vector4(4,5,6,7),
                    parameters10 = Vector4.one, parameters11 = Vector4.one, parameters12 = new Vector4(-12347,0,0,0) });
                EditorUtility.SetDirty(image);
            }
            var volume = environment.gameObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 50; volume.sharedProfile = profile;
            main.clearFlags = CameraClearFlags.Skybox;
            environment.ApplyNow(); AssetDatabase.SaveAssetIfDirty(profile);
            EditorSceneManager.SaveScene(scene, scenePath);
            HoMmdRTShadowDiagnostics.ConfigureStageReceivers(new HoMmdVideoWorkshop.Request { scene = scenePath, folder = folder });
            return scenePath;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    public static void Review(HoMmdStageWork.Request request, Action<string> completed, Action<Exception> failed)
    {
        string scene = !string.IsNullOrEmpty(request.scenePath) ? AssetPath(request.scenePath)
            : HoMmdStageBuilder.Root + request.stage + "/" + request.stage + ".unity";
        string output = string.IsNullOrEmpty(request.outputPath)
            ? Path.Combine(HoMmdStageWork.Work, "Review", Path.GetFileNameWithoutExtension(scene), DateTime.Now.ToString("yyyyMMdd-HHmmss"))
            : Path.GetFullPath(request.outputPath);
        Directory.CreateDirectory(output);
        int view = 0;
        Action next = null;
        next = () =>
        {
            if (view == 3) { completed(output); return; }
            int current = view++;
            try
            {
                HoMmdStageCapture.Begin(scene, Path.Combine(output, current == 0 ? "main.png" : current == 1 ? "left.png" : "right.png"),
                    request.width, request.height, _ => NextEditorUpdate(next), failed,
                    camera =>
                    {
                        ApplyMotionAtFrame(request, camera);
                        if (current == 0) return;
                        string cameraName = current == 1 ? "左侧观察相机" : "右侧观察相机";
                        var other = camera.gameObject.scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).FirstOrDefault(x => x.name == cameraName);
                        if (other == null) throw new InvalidOperationException("Create/reposition the review camera: " + cameraName);
                        // Copy the whole camera, including lens/physical-camera settings, into the temporary capture camera.
                        camera.CopyFrom(other); camera.enabled = false;
                        camera.transform.SetPositionAndRotation(other.transform.position, other.transform.rotation);
                    }, request.warmup);
            }
            catch (Exception e) { failed(e); }
        };
        next();
    }

    static void NextEditorUpdate(Action action)
    {
        // delayCall can wait for an Inspector update indefinitely when this
        // Editor is unfocused or a capture restored an empty scene.
        EditorApplication.CallbackFunction once = null;
        once = () => { EditorApplication.update -= once; action(); };
        EditorApplication.update += once;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    public static void RenderOne(HoMmdStageWork.Request request, Action<string> completed, Action<Exception> failed)
    {
        string scene = AssetPath(request.scenePath);
        string output = string.IsNullOrEmpty(request.outputPath)
            ? Path.Combine(HoMmdStageWork.Work, "Review", Path.GetFileNameWithoutExtension(scene), DateTime.Now.ToString("yyyyMMdd-HHmmss"), "main.png")
            : Path.GetFullPath(request.outputPath);
        HoMmdStageCapture.Begin(scene, output, request.width, request.height, completed, failed,
            camera => ApplyMotionAtFrame(request, camera), request.warmup);
    }

    private static void ApplyMotionAtFrame(HoMmdStageWork.Request request, Camera captureCamera)
    {
        if (request == null || string.IsNullOrEmpty(request.motionAsset) || request.motionFrame < 0.0f)
            return;
        string assetPath = AssetPath(request.motionAsset);
        HoVmdAnimation animation = AssetDatabase.LoadAssetAtPath<HoVmdAnimation>(assetPath);
        if (animation == null)
            throw new InvalidOperationException("VMD asset could not be loaded: " + assetPath);
        GameObject sceneRoot = captureCamera != null ? captureCamera.transform.root.gameObject : null;
        if (sceneRoot == null) return;
        HoVmdMotionPlayer[] players = sceneRoot.GetComponentsInChildren<HoVmdMotionPlayer>(true);
        foreach (HoVmdMotionPlayer player in players)
        {
            player.Animation = animation;
            player.FrameRate = request.motionRate;
            player.RebuildBindings();
            player.ApplyAtFrame(request.motionFrame);
        }
        string auditStem = Path.GetFileNameWithoutExtension(request.scenePath ?? sceneRoot.scene.name) + "-" + animation.name;
        for (int i = 0; i < players.Length; i++)
        {
            HoVmdMotionPlayer motionPlayer = players[i];
            if (motionPlayer == null) continue;
            GameObject target = motionPlayer.ModelRoot != null ? motionPlayer.ModelRoot.gameObject : motionPlayer.gameObject;
            HoVmdBindingProfile profile = motionPlayer.BindingProfile;
            string suffix = players.Length == 1 ? string.Empty : "-" + SafeFilePart(motionPlayer.name);
            string auditPath = Path.Combine(HoMmdStageWork.Work, "MorphAudit", auditStem + suffix + ".json");
            HoVmdMorphAuditResult audit = HoVmdMorphAudit.Audit(animation, target, profile);
            HoVmdMorphAudit.WriteJson(animation, target, profile, auditPath);
            if (audit.missing > 0 || audit.conflicts > 0)
                Debug.LogWarning("Ho VMD morph audit found missing=" + audit.missing + " conflicts=" + audit.conflicts + ": " + auditPath, target);
        }
        foreach (HoVmdCameraPlayer player in sceneRoot.GetComponentsInChildren<HoVmdCameraPlayer>(true))
        {
            player.animation = animation;
            player.frameRate = request.motionRate;
            player.ApplyAtFrame(request.motionFrame);
        }
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    { var component = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(component, profile); return component; }
    static Transform Child(Transform parent, string name)
    { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
    static string AssetPath(string path)
    {
        path = (path ?? "").Replace('\\', '/');
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Contains(".."))
            throw new ArgumentException("Use a project Assets/... path: " + path);
        return path;
    }

    static string SafeFilePart(string value)
    {
        if (string.IsNullOrEmpty(value)) return "player";
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }
}
