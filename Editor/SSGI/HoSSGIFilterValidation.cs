#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.SSGI
{
    public static class HoSSGIFilterValidation
    {
        public static void RunBaseline() => Run(true);
        public static void Run() => Run(false);
        private static void Run(bool baseline)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor");
            var objects = new List<Object>();
            var oldGraphics = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            int exitCode = 0;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); objects.Add(renderer);
                var probe = ScriptableObject.CreateInstance<FilterProbe>(); objects.Add(probe);
                renderer.rendererFeatures.Add(probe);
                var pipeline = UniversalRenderPipelineAsset.Create(renderer); objects.Add(pipeline);
                pipeline.msaaSampleCount = 1;
                GraphicsSettings.defaultRenderPipeline = QualitySettings.renderPipeline = pipeline;
                var go = new GameObject("SSGI filter validation"); objects.Add(go);
                var camera = go.AddComponent<Camera>(); camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.nearClipPlane = 0.3f; camera.farClipPlane = 100;
                var target = new RenderTexture(160, 128, 24, RenderTextureFormat.ARGBHalf); objects.Add(target); target.Create();
                foreach (int pattern in new[] { 0, 1, 2 })
                {
                    probe.Initialize(pattern);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    var oldTarget = RenderTexture.active; RenderTexture.active = target;
                    var image = new Texture2D(160, 128, TextureFormat.RGBAFloat, false, true);
                    image.ReadPixels(new Rect(0, 0, 160, 128), 0, 0); image.Apply();
                    RenderTexture.active = oldTarget;
                    float minimum = 100, maximum = -100;
                    for (int y = 32; y < 96; y++) for (int x = 32; x < 128; x++)
                    {
                        float value = image.GetPixel(x, y).r;
                        minimum = Mathf.Min(minimum, value); maximum = Mathf.Max(maximum, value);
                    }
                    Object.DestroyImmediate(image);
                    float contrast = maximum - minimum;
                    Debug.Log($"[HoSSGIFilterValidation] {(pattern == 2 ? "geometry-edge" : pattern == 1 ? "stripes" : "checker")} contrast={contrast:F8} min={minimum:F6} max={maximum:F6}");
                    if (pattern == 2 ? contrast < 0.75f : baseline ? contrast < 0.5f : contrast > 0.05f)
                        throw new Exception("Periodic input was not handled as expected: " + contrast);
                }
                Debug.Log("[HoSSGIFilterValidation] PASS");
            }
            catch (Exception e) { Debug.LogException(e); exitCode = 1; }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = oldGraphics; QualitySettings.renderPipeline = oldQuality;
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            EditorApplication.Exit(exitCode);
        }

        private sealed class FilterProbe : ScriptableRendererFeature
        {
            private Material material;
            private RTHandle source, geometry, guidance;
            private ProbePass pass;
            public override void Create() { pass = new ProbePass(); }
            public void Initialize(int pattern)
            {
                Release();
                material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/lilToon/URP/HoSSGI"));
                source = MakeTexture((x, y) => (pattern == 2 ? x < 80 : ((x / 2 + (pattern == 1 ? 0 : y / 2)) & 1) == 0) ? new Color(0.1f, 0.1f, 0.1f, 1) : new Color(0.9f, 0.9f, 0.9f, 1));
                geometry = MakeTexture((x, y) => pattern == 2 && x >= 80 ? new Color(0.5f, 1, 0.5f, 10) : new Color(0.5f, 0.5f, 0, 5));
                guidance = MakeTexture((x, y) => Color.white);
            }
            private static RTHandle MakeTexture(Func<int, int, Color> pixel)
            {
                var image = new Texture2D(160, 128, TextureFormat.RGBAFloat, false, true);
                var pixels = new Color[160 * 128];
                for (int y = 0; y < 128; y++) for (int x = 0; x < 160; x++) pixels[y * 160 + x] = pixel(x, y);
                image.SetPixels(pixels); image.Apply();
                var handle = RTHandles.Alloc(160, 128, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Bilinear);
                Graphics.Blit(image, handle.rt); Object.DestroyImmediate(image);
                return handle;
            }
            public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
            {
                if (material == null) return;
                pass.material = material; pass.source = source; pass.geometry = geometry; pass.guidance = guidance;
                renderer.EnqueuePass(pass);
            }
            private void Release()
            {
                CoreUtils.Destroy(material); source?.Release(); geometry?.Release(); guidance?.Release();
            }
            protected override void Dispose(bool disposing) { Release(); }
        }

        private sealed class ProbePass : ScriptableRenderPass
        {
            public Material material;
            public RTHandle source, geometry, guidance;
            public ProbePass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; requiresIntermediateTexture = true; }
            private sealed class Data
            {
                public Material material;
                public TextureHandle source, geometry, guidance;
                public float radius;
                public Vector4 axis;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var input = graph.ImportTexture(source);
                var normalDepth = graph.ImportTexture(geometry);
                var guide = graph.ImportTexture(guidance);
                var descriptor = graph.GetTextureDesc(resources.activeColorTexture);
                descriptor.format = GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.depthBufferBits = DepthBits.None;
                for (int iteration = 0; iteration < 2; iteration++)
                {
                    descriptor.name = "SSGI periodic filter probe " + iteration;
                    var output = graph.CreateTexture(descriptor);
                    using (var builder = graph.AddRasterRenderPass<Data>(descriptor.name, out var data))
                    {
                        data.material = material; data.source = input; data.geometry = normalDepth; data.guidance = guide;
                        data.radius = iteration == 0 ? 4 : 8;
                        data.axis = iteration == 0 ? new Vector4(1, 0, 0, 0) : new Vector4(0, 1, 0, 0);
                        builder.UseTexture(input); builder.UseTexture(normalDepth); builder.UseTexture(guide);
                        builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
                        builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static (Data pass, RasterGraphContext context) =>
                        {
                            context.cmd.SetGlobalFloat("_HoSSGISpatialRadius", pass.radius);
                            context.cmd.SetGlobalVector("_HoSSGIFilterAxis", pass.axis);
                            context.cmd.SetGlobalFloat("_HoSSGIUseAO", 0);
                            context.cmd.SetGlobalVector("_HoSSGITextureSize", new Vector4(160, 128, 1f / 160, 1f / 128));
                            context.cmd.SetGlobalTexture("_HoSSGIRawGIInput", pass.source);
                            context.cmd.SetGlobalTexture("_HoSSGIGeometry", pass.geometry);
                            context.cmd.SetGlobalTexture("_HoSSGISpatialGuidance", pass.guidance);
                            Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1, 1, 0, 0), pass.material, 6);
                        });
                    }
                    input = output;
                }
                resources.cameraColor = input;
            }
        }
    }
}
