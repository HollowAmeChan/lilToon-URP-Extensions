#pragma warning disable CS0618, CS0672
using System.Collections.Generic;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.SurfaceBuffer
{
    /// <summary>Producer-side sample association. Screen consumers receive single-sample W/V ordered by OB ranked owner.</summary>
    internal sealed class HoSurfaceBufferCorrelatedSemanticPass : ScriptableRenderPass
    {
        internal const string PassName = "HO_SURFACE_CORRELATED_V1";
        internal const string LightMode = "HoSurfaceCorrelatedV1";
        internal const string ReduceShader = "Hidden/lilToon/URP/SurfaceBuffer/CorrelatedReduce";
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("Ho-Surface Semantic Association");
        private readonly List<Renderer> assigned = new List<Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly HashSet<Material> checkedMaterials = new HashSet<Material>();
        private HoSurfaceBufferSettings settings;
        private FilteringSettings filtering;
        private Material reduce;
        private bool writersSupported;
        private string writerStatus;
        #if UNITY_EDITOR
        private int preparedCameraId;
        #endif
        private static readonly int IdentitySamplesId = Shader.PropertyToID("_HoCorrelatedIdentitySamples");
        private static readonly int SurfaceSamplesId = Shader.PropertyToID("_HoCorrelatedSurfaceSamples");
        private static readonly int Id0Id = Shader.PropertyToID("_HoCorrelatedId0");
        private static readonly int Id1Id = Shader.PropertyToID("_HoCorrelatedId1");
        private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private sealed class CaptureData { public RendererListHandle list; }
        private sealed class ReduceData
        {
            public TextureHandle identity, surface, id0, id1;
            public Material material;
            public int samples;
        }
        public void Setup(HoSurfaceBufferSettings active, FilteringSettings filter, Camera camera)
        {
            settings = active; filtering = filter; renderPassEvent = active.passEvent;
            ConfigureInput(ScriptableRenderPassInput.None);
            #if UNITY_EDITOR
            // Keep a cold camera approximate even if compilation finishes during its culling.
            if (camera != null && preparedCameraId == camera.GetInstanceID()) return;
            #endif
            writersSupported = WritersSupported(out writerStatus);
        }
        public void Dispose() { CoreUtils.Destroy(reduce); reduce = null; }
        #if UNITY_EDITOR
        internal void PrepareWriterVariants(HoSurfaceBufferSettings active, Camera camera)
        {
            settings = active;
            preparedCameraId = camera != null ? camera.GetInstanceID() : 0;
            writersSupported = WritersSupported(out writerStatus);
        }
        #endif
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            HoSurfaceSemanticPrecisionDiagnostics.Publish(renderingData.cameraData.camera, 0, "Compatibility path uses pixel semantics");
        }
        private bool WritersSupported(out string status)
        {
            status = "Legacy-only semantic writer";
            checkedMaterials.Clear();
            foreach (HoObjectBufferGroup group in HoObjectBufferGroup.GetActiveGroups())
            {
                if (group == null) continue;
                group.GetAssignedRenderers(assigned);
                foreach (Renderer renderer in assigned)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (settings.layerMask.value & (1 << renderer.gameObject.layer)) == 0) continue;
                    renderer.GetSharedMaterials(materials);
                    foreach (Material material in materials)
                    {
                        if (material == null || material.renderQueue < settings.minRenderQueue || material.renderQueue > settings.maxRenderQueue || !checkedMaterials.Add(material)) continue;
                        bool legacy = false;
                        for (int i = 0; i < material.passCount; i++)
                            if (material.shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name == "HoSurfaceSemantic") legacy = true;
                        int correlatedPass = material.FindPass(PassName);
                        if (legacy && (correlatedPass < 0 || !material.GetShaderPassEnabled(LightMode))) return false;
                        #if UNITY_EDITOR
                        // Cold asynchronous variants may draw a temporary fallback. Never label that capture exact.
                        if (correlatedPass >= 0 && !UnityEditor.ShaderUtil.IsPassCompiled(material, correlatedPass))
                        {
                            UnityEditor.ShaderUtil.CompilePass(material, correlatedPass, !UnityEditor.ShaderUtil.allowAsyncCompilation);
                            if (!UnityEditor.ShaderUtil.IsPassCompiled(material, correlatedPass))
                            {
                                status = "Semantic writer variant compiling";
                                return false;
                            }
                        }
                        #endif
                    }
                }
            }
            return true;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            HoSurfaceBufferRenderGraphResources output = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            UniversalCameraData camera = frame.Get<UniversalCameraData>();
            if (settings == null || !settings.enableSemanticLanes || !settings.enableCorrelatedSemantics)
            { SetStatus(output, camera, 0, "Correlated semantics disabled"); return; }
            HoObjectBufferRenderGraphResources ob = frame.GetOrCreate<HoObjectBufferRenderGraphResources>();
            var descriptor = camera.cameraTargetDescriptor;
            if (!ob.HasRequiredTextures || !ob.identitySampleTexture.IsValid() || !ob.sampleDomain.IsValid)
            { SetStatus(output, camera, 0, "OB sample domain unavailable"); return; }
            if (descriptor.dimension != TextureDimension.Tex2D || descriptor.volumeDepth != 1 || descriptor.useDynamicScale || camera.camera.rect != new Rect(0,0,1,1) ||
                (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12) || SystemInfo.graphicsShaderLevel < 50)
            { SetStatus(output, camera, 0, "Sample domain/platform requires legacy path"); return; }
            HoSemanticSampleDomain domain = new HoSemanticSampleDomain
            {
                cameraId = camera.camera.GetInstanceID(), frame = Time.frameCount, width = descriptor.width, height = descriptor.height,
                slices = descriptor.volumeDepth, dimension = descriptor.dimension, dynamicScale = descriptor.useDynamicScale,
                samples = ob.sampleDomain.samples, view = camera.GetViewMatrix(), projection = camera.GetProjectionMatrix(),
                viewport = camera.camera.rect
            };
            if (!domain.Matches(ob.sampleDomain)) { SetStatus(output, camera, 0, "OB/SB sample domain mismatch"); return; }
            if (!writersSupported) { SetStatus(output, camera, 0, writerStatus); return; }
            var packetDescriptor = descriptor;
            packetDescriptor.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm; packetDescriptor.depthStencilFormat = GraphicsFormat.None;
            packetDescriptor.depthBufferBits = 0; packetDescriptor.msaaSamples = domain.samples;
            if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(packetDescriptor) != domain.samples ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render) || SystemInfo.supportedRenderTargetCount < 3)
            { SetStatus(output, camera, 0, "Association attachment format unsupported"); return; }
            if (reduce == null)
            {
                Shader shader = Shader.Find(ReduceShader);
                if (shader == null || !shader.isSupported) { SetStatus(output, camera, 0, "Association shader unavailable"); return; }
                reduce = CoreUtils.CreateEngineMaterial(shader);
            }
            TextureHandle packet = graph.CreateTexture(new TextureDesc(domain.width, domain.height)
            {
                name = "_HoSurfaceCorrelatedSamples", format = GraphicsFormat.R8G8B8A8_UNorm,
                msaaSamples = (MSAASamples)domain.samples, bindTextureMS = domain.samples > 1,
                clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Point
            });
            // Private depth: do not expose or alter OB's depth ownership.
            var depthDescriptor = descriptor; depthDescriptor.graphicsFormat = GraphicsFormat.None;
            depthDescriptor.depthStencilFormat = HoObjectBufferFormatUtility.GetDepthStencilFormat(descriptor);
            depthDescriptor.msaaSamples = domain.samples; depthDescriptor.bindMS = false;
            TextureHandle depth = UniversalRenderer.CreateRenderGraphTexture(graph, depthDescriptor, "_HoSurfaceCorrelatedDepth", true, FilterMode.Point, TextureWrapMode.Clamp);
            var drawing = RenderingUtils.CreateDrawingSettings(new ShaderTagId(LightMode), frame.Get<UniversalRenderingData>(), camera,
                frame.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
            using (var builder = graph.AddRasterRenderPass<CaptureData>("Ho-SB Correlated Capture", out var data, Sampler))
            {
                data.list = graph.CreateRendererList(new RendererListParams(frame.Get<UniversalRenderingData>().cullResults, drawing, filtering));
                builder.UseRendererList(data.list); builder.SetRenderAttachment(packet, 0, AccessFlags.WriteAll);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.WriteAll); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (CaptureData d, RasterGraphContext c) =>
                {
                    // WriteAll skips the graph's load clear. Pooled MSAA stencil must not retain another frame's material writes.
                    c.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1f, 0);
                    c.cmd.DrawRendererList(d.list);
                });
            }
            TextureHandle written = Statistics(graph, domain, "_HoSurfaceSemanticWrittenCoverage");
            TextureHandle weighted = Statistics(graph, domain, "_HoSurfaceSemanticWeightedCoverage");
            TextureHandle status = Statistics(graph, domain, "_HoSurfaceSemanticAssociationStatus", true);
            using (var builder = graph.AddRasterRenderPass<ReduceData>("Ho-SB Owner Association", out var data, Sampler))
            {
                data.identity = ob.identitySampleTexture; data.surface = packet; data.id0 = ob.id0Texture; data.id1 = ob.id1Texture;
                data.material = reduce; data.samples = domain.samples;
                builder.UseTexture(data.identity, AccessFlags.Read); builder.UseTexture(packet, AccessFlags.Read);
                builder.UseTexture(data.id0, AccessFlags.Read); builder.UseTexture(data.id1, AccessFlags.Read);
                builder.SetRenderAttachment(written, 0, AccessFlags.WriteAll); builder.SetRenderAttachment(weighted, 1, AccessFlags.WriteAll);
                builder.SetRenderAttachment(status, 2, AccessFlags.WriteAll); builder.AllowGlobalStateModification(true); builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (ReduceData d, RasterGraphContext c) =>
                {
                    d.material.DisableKeyword("HO_CORRELATED_2"); d.material.DisableKeyword("HO_CORRELATED_4");
                    if (d.samples == 2) d.material.EnableKeyword("HO_CORRELATED_2");
                    if (d.samples == 4) d.material.EnableKeyword("HO_CORRELATED_4");
                    c.cmd.SetGlobalTexture(IdentitySamplesId, d.identity); c.cmd.SetGlobalTexture(SurfaceSamplesId, d.surface);
                    c.cmd.SetGlobalTexture(Id0Id, d.id0); c.cmd.SetGlobalTexture(Id1Id, d.id1);
                    c.cmd.SetGlobalVector(ScaleBiasId, new Vector4(1,1,0,0));
                    c.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1);
                });
            }
            output.semanticWrittenCoverageTexture = written; output.semanticWeightedCoverageTexture = weighted;
            output.semanticAssociationStatusTexture = status; output.semanticSampleDomain = domain;
            SetStatus(output, camera, domain.samples, "Correlated Scalar V1", true);
        }
        private static void SetStatus(HoSurfaceBufferRenderGraphResources output, UniversalCameraData camera, int samples, string status, bool correlated = false)
        {
            output.semanticPrecisionStatus = status;
            HoSurfaceSemanticPrecisionDiagnostics.Publish(camera.camera, samples, status, correlated);
        }
        private static TextureHandle Statistics(RenderGraph graph, HoSemanticSampleDomain d, string name, bool status = false) =>
            graph.CreateTexture(new TextureDesc(d.width, d.height) { name = name, format = status ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R16G16B16A16_SFloat,
                clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Point });
    }
}
