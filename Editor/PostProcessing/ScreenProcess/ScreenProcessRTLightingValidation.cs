#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.PostProcessing;
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    // Tests the real consumer/GB/SB pipeline with known typed HDR products.
    // This is not an RT estimator or denoising quality test.
    public static class ScreenProcessRTLightingValidation
    {
        static readonly List<Object> owned = new();
        static T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
        [Serializable] sealed class Check { public string name; public Vector3 actual, expected; public float maxError; }
        [Serializable] sealed class Report { public bool pass, compatibility; public string error, graphics, colorSpace; public List<Check> checks = new(); }
        static string diagnostic;
        static void OnLog(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) diagnostic ??= text; }
        public static void RunCompatibility() => Run(true);
        public static void Run() => Run(false);
        static void Run(bool compatibility)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch-only validation.");
            var report = new Report { compatibility = compatibility, graphics = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString() };
            var oldDefault = GraphicsSettings.defaultRenderPipeline; var oldQuality = QualitySettings.renderPipeline;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            int warmup = 0;
            EditorApplication.CallbackFunction tick = null;
            Publisher publisher = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); RenderSettings.fog = false;
                var renderer = Keep(ScriptableObject.CreateInstance<UniversalRendererData>());
                renderer.rendererFeatures.Add(Keep(ScriptableObject.CreateInstance<HoGeometryBufferRendererFeature>()));
                var sb = Keep(ScriptableObject.CreateInstance<HoSurfaceBufferRendererFeature>());
                sb.Settings.enableSemanticLanes = sb.Settings.enableCorrelatedSemantics = false;
                renderer.rendererFeatures.Add(sb);
                publisher = Keep(ScriptableObject.CreateInstance<Publisher>()); renderer.rendererFeatures.Add(publisher);
                var sp = Keep(ScriptableObject.CreateInstance<ScreenProcessRendererFeature>()); renderer.rendererFeatures.Add(sp);
                var pipeline = Keep(UniversalRenderPipelineAsset.Create(renderer)); pipeline.supportsHDR = true; pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
                #if URP_COMPATIBILITY_MODE
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = compatibility;
                #else
                if (compatibility) throw new InvalidOperationException("Compatibility symbol missing.");
                #endif
                var camera = Keep(new GameObject("RT consumer camera")).AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = new Vector3(0, 0, -3); camera.orthographic = true; camera.orthographicSize = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = 50; camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().volumeLayerMask = -1;
                var target = Keep(new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); target.Create();
                var image = Keep(new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true));
                var quad = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad)); quad.transform.localScale = new Vector3(2, 2, 1);
                var mat = Keep(new Material(Shader.Find("lilToon")));
                mat.EnableKeyword("UNITY_UI_ALPHACLIP"); mat.SetFloat("_Cull", 0); mat.SetFloat("_AsUnlit", 1);
                mat.SetColor("_Color", new Color(.6f, .4f, .2f, 1)); mat.SetTexture("_MainTex", Texture2D.whiteTexture);
                mat.SetFloat("_Metallic", 0); mat.SetFloat("_Reflectance", 0);
                mat.SetFloat("_UseEmission", 0);
                quad.GetComponent<Renderer>().sharedMaterial = mat;
                var holder = Keep(new GameObject("RT consumer Volume")).AddComponent<Volume>(); holder.isGlobal = true;
                holder.sharedProfile = Keep(ScriptableObject.CreateInstance<VolumeProfile>());
                var stack = holder.sharedProfile.Add<ScreenProcessStackVolume>(); stack.Enable.Override(true);
                var layers = new List<ScreenProcessLayer>(); stack.layers.Override(layers);
                Application.logMessageReceived += OnLog;
                Vector3 Render()
                {
                    if (diagnostic != null) throw new InvalidOperationException(diagnostic);
                    VolumeManager.instance.Update(camera.transform, -1);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); image.Apply(); Color c = image.GetPixel(16, 16);
                        if (!float.IsFinite(c.r) || !float.IsFinite(c.g) || !float.IsFinite(c.b) || !float.IsFinite(c.a))
                            throw new InvalidOperationException("Invalid camera color/alpha.");
                        return new Vector3(c.r, c.g, c.b);
                    }
                    finally { RenderTexture.active = previous; }
                }
                bool gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
                Vector3 Decode(Vector3 c) => gamma ? (Vector3)(Vector4)new Color(c.x, c.y, c.z, 1).linear : c;
                Vector3 Encode(Vector3 c) => gamma ? (Vector3)(Vector4)new Color(c.x, c.y, c.z, 1).gamma : c;
                void Check(string name, Vector3 actual, Vector3 expected, float tolerance = .0075f)
                {
                    float error = Mathf.Max(Mathf.Abs(actual.x - expected.x), Mathf.Abs(actual.y - expected.y), Mathf.Abs(actual.z - expected.z));
                    report.checks.Add(new Check { name = name, actual = actual, expected = expected, maxError = error });
                    if (!float.IsFinite(error) || error > tolerance) throw new InvalidOperationException(name + ": actual=" + actual + " expected=" + expected + " error=" + error);
                }
                tick = () =>
                {
                    try
                    {
                        if (warmup++ < 3) { Render(); return; }
                        Vector3 baseline = Render(), linear = Decode(baseline);
                        Vector3 di = Publisher.Diffuse + Publisher.Specular, gi = Publisher.Indirect;
                        ScreenProcessLayer Layer(ScreenProcessEffect effect, ScreenProcessRTLightingMode mode) => new ScreenProcessLayer
                        { effect = effect, rtLightingMode = mode, rtIndirectMode = mode, intensity = 1 };
                        foreach (var effect in new[] { ScreenProcessEffect.RTDI, ScreenProcessEffect.RTGI })
                        foreach (ScreenProcessRTLightingMode mode in Enum.GetValues(typeof(ScreenProcessRTLightingMode)))
                        {
                            layers.Clear(); var layer = Layer(effect, mode); layer.intensity = .4f; layers.Add(layer);
                            Vector3 light = effect == ScreenProcessEffect.RTDI ? di : gi;
                            Vector3 result = mode switch
                            {
                                ScreenProcessRTLightingMode.Replace => light,
                                ScreenProcessRTLightingMode.Additive => linear + light,
                                ScreenProcessRTLightingMode.Lighten => Vector3.Max(linear, light),
                                ScreenProcessRTLightingMode.Multiply => Vector3.Scale(linear, light),
                                _ => linear + light / (1 + Vector3.Dot(light, new Vector3(.2126f, .7152f, .0722f)))
                            };
                            Check(effect + "/" + mode, Render(), Encode(Vector3.Lerp(linear, result, .4f)));
                        }
                        layers.Clear(); var tuned = Layer(ScreenProcessEffect.RTDI, ScreenProcessRTLightingMode.Additive);
                        tuned.rtDiffuseWeight = .5f; tuned.rtSpecularWeight = 2; tuned.rtExposure = 1; tuned.rtGain = 1.5f;
                        tuned.rtTint = new Color(1, .5f, .25f, 1); layers.Add(tuned);
                        Color tint = tuned.rtTint.linear;
                        Check("gain/EV/tint/components", Render(), Encode(linear + Vector3.Scale(Publisher.Diffuse * .5f + Publisher.Specular * 2, new Vector3(tint.r, tint.g, tint.b)) * 3));
                        tuned.rtReceiver = ScreenProcessRTReceiver.Metallic;
                        Check("nonmetal receiver excluded by metallic gate", Render(), baseline);
                        tuned.rtReceiver = ScreenProcessRTReceiver.NonMetallic; tuned.rtDepthRange = new Vector2(0, 1);
                        Check("receiver excluded by depth gate", Render(), baseline);
                        tuned.rtDepthRange = Vector2.zero;
                        Check("depth range disabled restores receiving", Render(), Encode(linear + Vector3.Scale(Publisher.Diffuse * .5f + Publisher.Specular * 2, new Vector3(tint.r, tint.g, tint.b)) * 3));
                        layers.Clear(); layers.Add(Layer(ScreenProcessEffect.RTDI, ScreenProcessRTLightingMode.Replace)); layers.Add(Layer(ScreenProcessEffect.RTGI, ScreenProcessRTLightingMode.Additive));
                        Check("DI replace then GI add", Render(), Encode(di + gi));
                        layers.Reverse(); Check("GI add then DI replace", Render(), Encode(di));
                        layers.Clear(); layers.Add(Layer(ScreenProcessEffect.RTGI, ScreenProcessRTLightingMode.Additive)); layers.Add(Layer(ScreenProcessEffect.RTGI, ScreenProcessRTLightingMode.Additive));
                        Check("repeated GI layers retain both contributions", Render(), Encode(linear + gi * 2));
                        publisher.publishIndirect = false;
                        Check("missing GI product is identity", Render(), baseline);
                        publisher.publishIndirect = true; layers[0].enabled = false; layers[1].intensity = 0;
                        Check("disabled layers are identity", Render(), baseline);
                        layers.Clear(); RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                        RenderSettings.fogStartDistance = 0; RenderSettings.fogEndDistance = 6;
                        RenderSettings.fogColor = new Color(.1f, .2f, .3f, 1);
                        Vector3 foggedBaseline = Render();
                        var fogAdd = Layer(ScreenProcessEffect.RTDI, ScreenProcessRTLightingMode.Additive); layers.Add(fogAdd);
                        Check("fog attenuates added radiance only", Render(), Encode(Decode(foggedBaseline) + di * .5f), .012f);
                        fogAdd.rtLightingMode = ScreenProcessRTLightingMode.Replace;
                        Color fogColor = Shader.GetGlobalColor("unity_FogColor");
                        Vector3 fogRgb = new Vector3(fogColor.r, fogColor.g, fogColor.b);
                        Check("replacement retains fog in linear domain", Render(), Encode(di * .5f + Decode(fogRgb) * .5f), .012f);
                        RenderSettings.fog = false;
                        report.pass = diagnostic == null; Finish(null);
                    }
                    catch (Exception error) { Finish(error); }
                };
                void Finish(Exception error)
                {
                    EditorApplication.update -= tick; Application.logMessageReceived -= OnLog;
                    if (error != null) { report.error = error.ToString(); report.pass = false; }
                    string path = Path.Combine(Application.dataPath, "../consumer-" + (compatibility ? "compat" : "rg") + "-" + report.colorSpace + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(report, true));
                    sp.Dispose(); publisher.Dispose(); GraphicsSettings.defaultRenderPipeline = oldDefault; QualitySettings.renderPipeline = oldQuality; ShaderUtil.allowAsyncCompilation = oldAsync;
                    foreach (var obj in owned.AsEnumerable().Reverse()) if (obj) Object.DestroyImmediate(obj);
                    owned.Clear(); Debug.Log("[HoRTConsumer] " + (report.pass ? "PASS " : "FAIL ") + report.error); EditorApplication.Exit(report.pass ? 0 : 1);
                }
                EditorApplication.update += tick;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        // Editor-only fake producer publishes real borrowed resources, never globals as a substitute for RG handles.
        sealed class Publisher : ScriptableRendererFeature
        {
            internal static readonly Vector3 Diffuse = new Vector3(.35f, .12f, .07f), Specular = new Vector3(.04f, .08f, .12f), Indirect = new Vector3(.18f, .07f, .03f);
            internal bool publishIndirect = true;
            RTHandle diffuse, specular, indirect;
            PublishPass pass;
            RTHandle Texture(Vector3 color)
            {
                var texture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); texture.Create();
                using (var cmd = new CommandBuffer()) { cmd.SetRenderTarget(texture); cmd.ClearRenderTarget(false, true, new Color(color.x, color.y, color.z, 1)); Graphics.ExecuteCommandBuffer(cmd); }
                return RTHandles.Alloc(texture);
            }
            public override void Create()
            {
                diffuse ??= Texture(Diffuse); specular ??= Texture(Specular); indirect ??= Texture(Indirect);
                pass = new PublishPass { owner = this, renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
            }
            public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data) { renderer.EnqueuePass(pass); }
            protected override void Dispose(bool disposing) { diffuse?.Release(); specular?.Release(); indirect?.Release(); diffuse = specular = indirect = null; ScreenProcessRTLightingCompatibility.Clear(); }
            sealed class PublishPass : ScriptableRenderPass
            {
                internal Publisher owner;
                public override void Execute(ScriptableRenderContext context, ref RenderingData data)
                {
                    ScreenProcessRTLightingCompatibility.Reset(data.cameraData.camera);
                    ScreenProcessRTLightingCompatibility.PublishDirectProduct(data.cameraData.camera, owner.diffuse, owner.specular);
                    if (owner.publishIndirect) ScreenProcessRTLightingCompatibility.PublishIndirectProduct(data.cameraData.camera, owner.indirect);
                }
                public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
                {
                    var products = frame.GetOrCreate<ScreenProcessRTLightingResources>();
                    products.PublishDirectProduct(graph.ImportTexture(owner.diffuse), graph.ImportTexture(owner.specular));
                    if (owner.publishIndirect) products.PublishIndirectProduct(graph.ImportTexture(owner.indirect));
                }
            }
        }
    }
}
