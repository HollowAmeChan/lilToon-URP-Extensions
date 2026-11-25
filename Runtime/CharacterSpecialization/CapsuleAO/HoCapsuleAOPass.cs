#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    internal sealed class HoCapsuleAOPass : ScriptableRenderPass
    {
        internal const int MaxCapsules = 64;
        private static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int VisibilityId = Shader.PropertyToID("_HoCapsuleVisibility");
        private static readonly int SizeId = Shader.PropertyToID("_HoCapsuleScreenSize");
        private static readonly int VPId = Shader.PropertyToID("_HoCapsuleViewProjection");
        private static readonly int CountId = Shader.PropertyToID("_HoCapsuleCount");
        private static readonly int AId = Shader.PropertyToID("_HoCapsuleA");
        private static readonly int BId = Shader.PropertyToID("_HoCapsuleB");
        private static readonly int ParamsId = Shader.PropertyToID("_HoCapsuleParams");
        private static readonly int CrossId = Shader.PropertyToID("_HoCapsuleCross");
        private static readonly int RotationId = Shader.PropertyToID("_HoCapsuleRotation");

        private struct Surface { internal MeshRenderer renderer; internal int submeshCount; }
        private struct Source { internal Vector3 a, b; internal float radius, sort; internal int id; internal Bounds bounds; }
        private sealed class Inputs
        {
            internal Surface[] surfaces;
            internal Vector4[] a, b;
            internal int count;
            internal Rect scissor;
            internal Matrix4x4 vp;
            internal Vector4 screenSize;
            internal HoCapsuleAOConfiguration configuration;
        }
        private sealed class ReceiverData
        {
            internal TextureHandle depth;
            internal Material material;
            internal Inputs inputs;
        }
        private sealed class CompositeData
        {
            internal TextureHandle visibility;
            internal Material material;
            internal Inputs inputs;
        }

        private Material material;
        private readonly Dictionary<int, RTHandle> compatibilityMasks = new Dictionary<int, RTHandle>();
        private HoCapsuleAOConfiguration configuration;
        internal string Status { get; private set; } = "未启用。";
        internal int LastCapsuleCount { get; private set; }
        internal int LastReceiverCount { get; private set; }

        internal HoCapsuleAOPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        internal void Setup(HoCharacterSpecializationEffects effects) => configuration = HoCapsuleAOConfiguration.From(effects);
        internal void SetInactive()
        {
            LastCapsuleCount = LastReceiverCount = 0;
            Status = "未启用。";
        }

        private bool Prepare(Camera camera, int width, int height, Matrix4x4 vp, out Inputs inputs)
        {
            inputs = null;
            LastCapsuleCount = LastReceiverCount = 0;
            var candidates = new List<Source>();
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (var emitter in HoCapsuleAOEmitter.Active)
            {
                if (emitter == null || (camera.cullingMask & (1 << emitter.gameObject.layer)) == 0
                    || !emitter.TryGetWorldCapsule(out Vector3 a, out Vector3 b, out float radius)) continue;
                var bounds = new Bounds((a + b) * .5f, Abs(b - a));
                float extent = radius + (configuration.mode == HoCapsuleAOMode.Cross
                    ? Mathf.Max(configuration.distance, configuration.crossLength + configuration.crossSoftness) : configuration.distance);
                if (!float.IsFinite(extent) || !HoCapsuleAOEmitter.Finite(bounds.size + Vector3.one * (extent * 2))) continue;
                bounds.Expand(extent * 2);
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                candidates.Add(new Source { a = a, b = b, radius = radius, bounds = bounds,
                    sort = (bounds.center - camera.transform.position).sqrMagnitude, id = emitter.GetInstanceID() });
            }
            candidates.Sort((x, y) => { int order = x.sort.CompareTo(y.sort); return order != 0 ? order : x.id.CompareTo(y.id); });
            int count = Mathf.Min(configuration.budget, candidates.Count);
            if (count == 0) { Status = "没有有效且影响范围可见的胶囊。"; return false; }
            var surfaces = new List<Surface>();
            Bounds union = candidates[0].bounds;
            for (int i = 1; i < count; i++) union.Encapsulate(candidates[i].bounds);
            foreach (var receiver in HoCapsuleAOReceiver.Active)
            {
                if (receiver == null || !receiver.TryGetSurface(camera, out MeshRenderer renderer, out Mesh mesh)
                    || !GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) continue;
                bool intersects = false;
                for (int i = 0; i < count && !intersects; i++) intersects = candidates[i].bounds.Intersects(renderer.bounds);
                if (!intersects) continue;
                // Static opaque receivers only. Preserve each original material's queue qualification.
                var materials = renderer.sharedMaterials;
                bool opaque = materials.Length > 0;
                foreach (Material m in materials) opaque &= m != null && m.renderQueue <= (int)RenderQueue.GeometryLast;
                if (!opaque) continue;
                surfaces.Add(new Surface { renderer = renderer, submeshCount = Mathf.Min(mesh.subMeshCount, materials.Length) });
            }
            if (surfaces.Count == 0) { Status = "没有受影响的普通不透明地板 Receiver。"; return false; }
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend))
            { Status = "设备不支持接收覆盖的 R8 混合。"; return false; }
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("HoCapsuleAO");
                if (shader == null || !shader.isSupported) { Status = "胶囊 AO 内部 Shader 不可用。"; return false; }
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            inputs = new Inputs { surfaces = surfaces.ToArray(), a = new Vector4[MaxCapsules], b = new Vector4[MaxCapsules],
                count = count, configuration = configuration, vp = vp,
                screenSize = new Vector4(width, height, 1f / width, 1f / height), scissor = ProjectBounds(camera, union, width, height) };
            for (int i = 0; i < count; i++)
            {
                Source source = candidates[i];
                inputs.a[i] = new Vector4(source.a.x, source.a.y, source.a.z, source.radius);
                inputs.b[i] = new Vector4(source.b.x, source.b.y, source.b.z, 0);
            }
            LastCapsuleCount = count; LastReceiverCount = surfaces.Count;
            Status = count < candidates.Count ? $"胶囊预算：{count}/{candidates.Count}，按相机距离选择。" : $"胶囊 {count}，接收面 {surfaces.Count}。";
            return true;
        }

        private static Vector3 Abs(Vector3 p) => new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z));

        private static Rect ProjectBounds(Camera camera, Bounds bounds, int width, int height)
        {
            Vector3 lo = bounds.min, hi = bounds.max;
            Vector2 min = Vector2.one, max = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = camera.WorldToViewportPoint(new Vector3((i & 1) == 0 ? lo.x : hi.x,
                    (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
                if (p.z <= camera.nearClipPlane) return new Rect(0, 0, width, height);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            float x = Mathf.Max(0, Mathf.Floor(min.x * width) - 1), y = Mathf.Max(0, Mathf.Floor(min.y * height) - 1);
            return new Rect(x, y, Mathf.Max(0, Mathf.Min(width, Mathf.Ceil(max.x * width) + 1) - x),
                Mathf.Max(0, Mathf.Min(height, Mathf.Ceil(max.y * height) + 1) - y));
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            LastCapsuleCount = LastReceiverCount = 0;
            var camera = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            var descriptor = camera.cameraTargetDescriptor;
            if (!Supported(camera.camera, camera.renderType, descriptor)
                || !resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid())
            { Status = "当前相机配置或相机深度不可用。"; return; }
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), true) * camera.GetViewMatrix();
            if (!Prepare(camera.camera, descriptor.width, descriptor.height, vp, out Inputs inputs)) return;
            TextureHandle visibility = graph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
            {
                name = "Ho Capsule AO Receiver Visibility", colorFormat = GraphicsFormat.R8_UNorm,
                clearBuffer = true, clearColor = Color.white, filterMode = FilterMode.Point,
                msaaSamples = MSAASamples.None, depthBufferBits = DepthBits.None
            });
            using (var builder = graph.AddRasterRenderPass<ReceiverData>("Ho Capsule AO Receiver Redraw", out var data))
            {
                data.material = material; data.inputs = inputs; data.depth = resources.cameraDepthTexture;
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.SetRenderAttachment(visibility, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (ReceiverData d, RasterGraphContext context) =>
                {
                    var i = d.inputs; var c = i.configuration;
                    context.cmd.SetGlobalTexture(DepthId, d.depth);
                    context.cmd.SetGlobalVector(SizeId, i.screenSize);
                    context.cmd.SetGlobalMatrix(VPId, i.vp);
                    context.cmd.SetGlobalInt(CountId, i.count);
                    context.cmd.SetGlobalVectorArray(AId, i.a); context.cmd.SetGlobalVectorArray(BId, i.b);
                    context.cmd.SetGlobalVector(ParamsId, new Vector4(c.strength, c.distance, (float)c.mode, 0));
                    context.cmd.SetGlobalVector(CrossId, new Vector4(c.crossLength, c.crossWidth, c.crossAngle * Mathf.Deg2Rad, c.crossSoftness));
                    context.cmd.SetGlobalFloat(RotationId, c.crossRotation * Mathf.Deg2Rad);
                    context.cmd.EnableScissorRect(i.scissor);
                    foreach (Surface surface in i.surfaces)
                        for (int submesh = 0; submesh < surface.submeshCount; submesh++)
                            context.cmd.DrawRenderer(surface.renderer, d.material, submesh, 0);
                    context.cmd.DisableScissorRect(); context.cmd.SetGlobalInt(CountId, 0);
                });
            }
            using (var builder = graph.AddRasterRenderPass<CompositeData>("Ho Capsule AO Color Overlay", out var data))
            {
                data.material = material; data.inputs = inputs; data.visibility = visibility;
                builder.UseTexture(visibility, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(VisibilityId, d.visibility);
                    context.cmd.EnableScissorRect(d.inputs.scissor);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 1, MeshTopology.Triangles, 3);
                    context.cmd.DisableScissorRect();
                });
            }
        }

        private static bool Supported(Camera camera, CameraRenderType type, RenderTextureDescriptor descriptor)
            => (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView)
                && !camera.stereoEnabled && type == CameraRenderType.Base && camera.rect == new Rect(0, 0, 1, 1)
                && !descriptor.useDynamicScale && descriptor.dimension == TextureDimension.Tex2D;

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            LastCapsuleCount = LastReceiverCount = 0;
            var cameraData = renderingData.cameraData;
            Camera camera = cameraData.camera;
            var descriptor = cameraData.cameraTargetDescriptor;
            if (!Supported(camera, cameraData.renderType, descriptor))
            { Status = "当前相机配置不支持接收面叠加。"; return; }
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * cameraData.GetViewMatrix();
            if (!Prepare(camera, descriptor.width, descriptor.height, vp, out Inputs inputs)) return;
            compatibilityMasks.TryGetValue(camera.GetInstanceID(), out RTHandle visibility);
            descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            descriptor.depthStencilFormat = GraphicsFormat.None; descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1; descriptor.bindMS = false; descriptor.sRGB = false;
            descriptor.useMipMap = descriptor.autoGenerateMips = descriptor.enableRandomWrite = false;
            descriptor.memoryless = RenderTextureMemoryless.None;
            RenderingUtils.ReAllocateIfNeeded(ref visibility, descriptor, FilterMode.Point, TextureWrapMode.Clamp,
                name: "Ho Capsule AO Receiver Visibility");
            compatibilityMasks[camera.GetInstanceID()] = visibility;
            var cmd = CommandBufferPool.Get("Ho Capsule AO Receiver Overlay");
            CoreUtils.SetRenderTarget(cmd, visibility, ClearFlag.Color, Color.white);
            cmd.SetGlobalVector(SizeId, inputs.screenSize); cmd.SetGlobalMatrix(VPId, inputs.vp);
            cmd.SetGlobalInt(CountId, inputs.count);
            cmd.SetGlobalVectorArray(AId, inputs.a); cmd.SetGlobalVectorArray(BId, inputs.b);
            var c = inputs.configuration;
            cmd.SetGlobalVector(ParamsId, new Vector4(c.strength, c.distance, (float)c.mode, 0));
            cmd.SetGlobalVector(CrossId, new Vector4(c.crossLength, c.crossWidth, c.crossAngle * Mathf.Deg2Rad, c.crossSoftness));
            cmd.SetGlobalFloat(RotationId, c.crossRotation * Mathf.Deg2Rad);
            cmd.EnableScissorRect(inputs.scissor);
            foreach (Surface surface in inputs.surfaces)
                for (int submesh = 0; submesh < surface.submeshCount; submesh++)
                    cmd.DrawRenderer(surface.renderer, material, submesh, 0);
            cmd.DisableScissorRect(); cmd.SetGlobalInt(CountId, 0);
            CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle, ClearFlag.None);
            cmd.SetGlobalTexture(VisibilityId, visibility);
            cmd.EnableScissorRect(inputs.scissor);
            cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
            cmd.DisableScissorRect();
            context.ExecuteCommandBuffer(cmd); CommandBufferPool.Release(cmd);
        }

        internal void Dispose()
        {
            CoreUtils.Destroy(material); material = null;
            foreach (RTHandle mask in compatibilityMasks.Values) mask.Release();
            compatibilityMasks.Clear();
        }
    }
}
