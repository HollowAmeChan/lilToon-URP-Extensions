using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GTAO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class HoAODisableValidation
{
    private static readonly List<Object> owned = new();
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Camera camera;
    private static Material material;
    private static RenderTexture target, probe;
    private static HoGTAORendererFeature feature;
    private static HoGTAOVolume settings;
    private static bool baseline;
    private static int cases, checks;

    public static void Run()
    {
        var previous = GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        Texture oldAO = Shader.GetGlobalTexture("_HoAOTexture");
        float oldAvailable = Shader.GetGlobalFloat("_HoAOAvailable");
        try
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains("Temp~"))
                throw new InvalidOperationException("Use an isolated Temp~ batch project.");
            baseline = Environment.CommandLine.Contains("-aoDisableBaseline");
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var data = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
            var geometry = Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>());
            geometry.Create();
            geometry.Settings.enabled = false;
            data.rendererFeatures.Add(geometry);
            feature = Keep(ScriptableObject.CreateInstance<HoGTAORendererFeature>());
            feature.Create();
            feature.Settings.enabled = true;
            feature.Settings.publishAOTextureOnly = true;
            feature.Settings.sliceCount = 1;
            feature.Settings.stepCount = 8;
            feature.Settings.temporalFrameCount = 2;
            data.rendererFeatures.Add(feature);
            var pipeline = Keep(UniversalRenderPipelineAsset.Create(data));
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            var volume = Keep(new GameObject("AO Volume")).AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
            volume.sharedProfile = profile;
            settings = profile.Add<HoGTAOVolume>();
            settings.enable.overrideState = true;
            settings.enable.value = false;
            camera = Keep(new GameObject("AO Camera")).AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3);
            camera.transform.LookAt(Vector3.zero);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            target = Keep(new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat));
            target.Create();
            probe = Keep(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat));
            probe.Create();
            material = Keep(new Material(Shader.Find("Hidden/Ho-Validation/AODisableProbe")));
            var actor = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));
            actor.GetComponent<Renderer>().sharedMaterial = material;

            Check("fresh-volume-off", false, false);
            settings.enable.value = true;
            Check("volume-on", true, true);
            settings.enable.value = false;
            Check("volume-off-after-on", false, false);
            Check("volume-off-second-render", false, false);
            settings.enable.overrideState = false;
            Check("no-enable-override", true, true);
            feature.Settings.enabled = false;
            Check("feature-setting-off", false, false);
            settings.enable.overrideState = true;
            settings.enable.value = true;
            Check("volume-on-over-feature-off", true, true);
            settings.enable.value = false;
            Check("both-off", false, false);

            settings.enable.value = true;
            feature.Settings.publishAOTextureOnly = false;
            Check("normal-mode-without-geometry", true, false);
            geometry.Settings.enabled = true;
            Check("normal-mode-with-geometry", true, true, true);
            Require(HistoryValid(), "Normal GTAO did not produce valid history for the disable test.");
            settings.enable.value = false;
            Check("volume-off-after-normal-mode", false, false);
            Debug.Log("AO_DISABLE_HISTORY afterVolumeOff=" + HistoryValid());
            if (!baseline) Require(!HistoryValid(), "Volume disable retained stale GTAO history.");
            settings.enable.value = true;
            Check("normal-mode-resumed", true, true, true);
            feature.SetActive(false);
            Check("renderer-feature-inactive", true, false);
            feature.SetActive(true);
            feature.Settings.publishAOTextureOnly = true;
            Check("publish-only-resumed", true, true);

            Debug.Log($"AO_DISABLE_VALIDATION_{(baseline ? "BASELINE" : "PASSED")} api={SystemInfo.graphicsDeviceType} cases={cases} checks={checks}");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
            return;
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = previous;
            QualitySettings.renderPipeline = quality;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            Shader.SetGlobalTexture("_HoAOTexture", oldAO);
            Shader.SetGlobalFloat("_HoAOAvailable", oldAvailable);
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
        }
        EditorApplication.Exit(0);
    }

    private static void Check(string name, bool resolvedEnabled, bool published, bool traced = false)
    {
        // StandardRequest updates the actual Volume stack. SingleCameraRequest does not.
        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
        var cmd = CommandBufferPool.Get("AO validation after graph");
        try
        {
            cmd.SetRenderTarget(probe);
            cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        finally { CommandBufferPool.Release(cmd); }
        Color scene = Read(target), after = Read(probe);
        var runtime = (HoGTAOSettings)typeof(HoGTAORendererFeature).GetField("runtimeSettings", Private).GetValue(feature);
        Debug.Log($"AO_DISABLE_CASE {name} scene={scene} afterGraph={after} resolvedEnabled={runtime.enabled}");
        cases++;
        Require(runtime.enabled == resolvedEnabled, "Volume override resolved incorrectly: " + name);
        Require(Mathf.Abs(scene.g - (published ? 1 : 0)) < .001f, "Incorrect availability during camera: " + name);
        Require(scene.r >= 0 && scene.r <= 1 && Mathf.Abs(scene.b - scene.r) < .001f, "AO texture and effective visibility disagree: " + name);
        if (!traced) Require(Mathf.Abs(scene.r - 1) < .001f, "Neutral AO is not white during camera: " + name);
        if (!baseline || !published)
            Require(Neutral(after), "Camera left invalid/black AO state after rendering: " + name);
        else
            Require(after.r < .001f && after.g > .999f && after.b < .001f, "Baseline no longer reproduces graph-unbound AO: " + name);
    }

    private static bool HistoryValid()
    {
        var histories = (IDictionary)typeof(HoGTAORendererFeature).GetField("cameraHistories", Private).GetValue(feature);
        object history = histories[camera.GetInstanceID()];
        return history != null && (bool)history.GetType().GetProperty("Valid").GetValue(history);
    }

    private static bool Neutral(Color color) => Mathf.Abs(color.r - 1) < .001f && Mathf.Abs(color.g) < .001f && Mathf.Abs(color.b - 1) < .001f;

    private static Color Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var image = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
            image.Apply();
            return image.GetPixel(0, 0);
        }
        finally
        {
            Object.DestroyImmediate(image);
            RenderTexture.active = previous;
        }
    }

    private static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
    private static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
}
