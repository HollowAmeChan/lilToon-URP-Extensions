#pragma warning disable CS0618, CS0672

using System.Collections.Generic;
using lilToon.URP.Extensions.GeometryBuffer;
using lilToon.URP.Extensions.GTAO;
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
        private static readonly int DepthId = Shader.PropertyToID("_HoCapsuleGeometryDepth");
        private static readonly int NormalId = Shader.PropertyToID("_HoCapsuleGeometryNormal");
        private static readonly int MaskId = Shader.PropertyToID("_HoCapsuleReceiverMask");
        private static readonly int SizeId = Shader.PropertyToID("_HoCapsuleScreenSize");
        private static readonly int VPId = Shader.PropertyToID("_HoCapsuleViewProjection");
        private static readonly int InvVPId = Shader.PropertyToID("_HoCapsuleInverseViewProjection");
        private static readonly int CountId = Shader.PropertyToID("_HoCapsuleCount");
        private static readonly int AId = Shader.PropertyToID("_HoCapsuleA");
        private static readonly int BId = Shader.PropertyToID("_HoCapsuleB");
        private static readonly int ParamsId = Shader.PropertyToID("_HoCapsuleParams");
        private static readonly int CrossId = Shader.PropertyToID("_HoCapsuleCross");
        private static readonly int RotationId = Shader.PropertyToID("_HoCapsuleRotation");

        private struct Surface { internal Mesh mesh; internal Matrix4x4 matrix; internal int submeshCount; }
        private struct Source { internal Vector3 a, b; internal float radius, sort; internal int id; internal Bounds bounds; }
        private sealed class Inputs
        {
            internal Surface[] surfaces;
            internal Vector4[] a, b;
            internal int count;
            internal Rect scissor;
            internal Matrix4x4 vp, inverseVP;
            internal Vector4 screenSize;
            internal HoCapsuleAOConfiguration configuration;
        }
        private sealed class MaskData
        {
            internal TextureHandle depth;
            internal Material material;
            internal Inputs inputs;
        }
        private sealed class AppendData
        {
            internal TextureHandle depth, normal, mask;
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
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        internal void Setup(HoCharacterSpecializationEffects effects) => configuration = HoCapsuleAOConfiguration.From(effects);

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
                if (!opaque || renderer.isPartOfStaticBatch) continue;
                surfaces.Add(new Surface { mesh = mesh, matrix = renderer.localToWorldMatrix, submeshCount = mesh.subMeshCount });
            }
            if (surfaces.Count == 0) { Status = "没有受影响的普通不透明地板 Receiver（静态合批接收面暂不支持）。"; return false; }
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend))
            { Status = "设备不支持接收覆盖的 R8 混合。"; return false; }
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("HoCapsuleAO");
                if (shader == null || !shader.isSupported) { Status = "胶囊 AO 内部 Shader 不可用。"; return false; }
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            inputs = new Inputs { surfaces = surfaces.ToArray(), a = new Vector4[MaxCapsules], b = new Vector4[MaxCapsules],
                count = count, configuration = configuration, vp = vp, inverseVP = vp.inverse,
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
            var camera = frameData.Get<UniversalCameraData>();
            var descriptor = camera.cameraTargetDescriptor;
            var ao = frameData.GetOrCreate<HoGTAORenderGraphResources>();
            if (!ao.CanAppend(camera.camera, descriptor.width, descriptor.height))
            { Status = "当前相机没有已发布的可写 HoAO；请检查 HoGTAO 与 Feature 顺序。"; return; }
            var geometry = frameData.GetOrCreate<HoGeometryBufferRenderGraphResources>();
            if (!geometry.depthTexture.IsValid() || !geometry.normalDepthTexture.IsValid())
            { Status = "当前相机没有 GB 物理几何。"; return; }
            var aoDesc = graph.GetTextureDesc(ao.aoTexture);
            var depthDesc = graph.GetTextureDesc(geometry.depthTexture);
            if (camera.xr != null && camera.xr.enabled || !camera.isDefaultViewport || aoDesc.dimension != TextureDimension.Tex2D
                || depthDesc.width != aoDesc.width || depthDesc.height != aoDesc.height
                || aoDesc.msaaSamples != MSAASamples.None
                || !SystemInfo.IsFormatSupported(aoDesc.colorFormat, GraphicsFormatUsage.Blend))
            { Status = "当前 AO／GB 尺寸、采样、混合格式或 XR／viewport 不受支持。"; return; }
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), true) * camera.GetViewMatrix();
            if (!Prepare(camera.camera, descriptor.width, descriptor.height, vp, out Inputs inputs)) return;

            var maskDesc = aoDesc;
            maskDesc.name = "Ho Capsule AO Receiver Coverage";
            maskDesc.colorFormat = GraphicsFormat.R8_UNorm;
            maskDesc.depthBufferBits = DepthBits.None;
            maskDesc.clearBuffer = true; maskDesc.clearColor = Color.black;
            maskDesc.filterMode = FilterMode.Point;
            TextureHandle mask = graph.CreateTexture(maskDesc);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Ho Capsule AO Receiver Coverage", out var data))
            {
                data.material = material; data.inputs = inputs; data.depth = geometry.depthTexture;
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthId, d.depth);
                    context.cmd.SetGlobalVector(SizeId, d.inputs.screenSize);
                    context.cmd.SetGlobalMatrix(VPId, d.inputs.vp);
                    context.cmd.EnableScissorRect(d.inputs.scissor);
                    foreach (Surface surface in d.inputs.surfaces)
                        for (int submesh = 0; submesh < surface.submeshCount; submesh++)
                            context.cmd.DrawMesh(surface.mesh, surface.matrix, d.material, submesh, 0);
                    context.cmd.DisableScissorRect();
                });
            }
            using (var builder = graph.AddRasterRenderPass<AppendData>("Ho Capsule AO Append", out var data))
            {
                data.material = material; data.inputs = inputs; data.mask = mask;
                data.depth = geometry.depthTexture; data.normal = geometry.normalDepthTexture;
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.UseTexture(data.normal, AccessFlags.Read);
                builder.SetRenderAttachment(ao.aoTexture, 0, AccessFlags.ReadWrite);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (AppendData d, RasterGraphContext context) =>
                {
                    var c = d.inputs.configuration;
                    context.cmd.SetGlobalTexture(MaskId, d.mask);
                    context.cmd.SetGlobalTexture(DepthId, d.depth);
                    context.cmd.SetGlobalTexture(NormalId, d.normal);
                    context.cmd.SetGlobalVector(SizeId, d.inputs.screenSize);
                    context.cmd.SetGlobalMatrix(InvVPId, d.inputs.inverseVP);
                    context.cmd.SetGlobalInt(CountId, d.inputs.count);
                    context.cmd.SetGlobalVectorArray(AId, d.inputs.a);
                    context.cmd.SetGlobalVectorArray(BId, d.inputs.b);
                    context.cmd.SetGlobalVector(ParamsId, new Vector4(c.strength, c.distance, (float)c.mode, 0));
                    context.cmd.SetGlobalVector(CrossId, new Vector4(c.crossLength, c.crossWidth, c.crossAngle * Mathf.Deg2Rad, c.crossSoftness));
                    context.cmd.SetGlobalFloat(RotationId, c.crossRotation * Mathf.Deg2Rad);
                    context.cmd.EnableScissorRect(d.inputs.scissor);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, c.blend == HoCapsuleAOBlendMode.Min ? 1 : 2, MeshTopology.Triangles, 3);
                    context.cmd.DisableScissorRect();
                    context.cmd.SetGlobalInt(CountId, 0);
                });
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            RTHandle ao = HoAOCompatibilityPublication.Camera == camera ? HoAOCompatibilityPublication.Texture : null;
            var geometry = HoGeometryBufferPass.CompatibilityCamera == camera ? HoGeometryBufferPass.CompatibilityTargets : null;
            if (ao == null || ao.rt == null || geometry == null || geometry.DepthTexture == null || geometry.NormalDepthTexture == null)
            { Status = "当前相机没有 Compatibility HoAO／GB 发布。"; return; }
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            if (camera.stereoEnabled || camera.rect != new Rect(0, 0, 1, 1)
                || ao.rt.width != descriptor.width || ao.rt.height != descriptor.height
                || geometry.DepthTexture.rt.width != descriptor.width || geometry.DepthTexture.rt.height != descriptor.height)
            { Status = "Compatibility AO／GB 尺寸或相机配置不匹配。"; return; }
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(renderingData.cameraData.GetProjectionMatrix(), true)
                * renderingData.cameraData.GetViewMatrix();
            if (!Prepare(camera, descriptor.width, descriptor.height, vp, out Inputs inputs)) return;
            compatibilityMasks.TryGetValue(camera.GetInstanceID(), out RTHandle mask);
            descriptor = HoGTAOPublishOnlyPass.Descriptor(descriptor);
            descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref mask, descriptor, FilterMode.Point, TextureWrapMode.Clamp,
                name: "Ho Capsule AO Receiver Coverage");
            compatibilityMasks[camera.GetInstanceID()] = mask;
            var cmd = CommandBufferPool.Get("Ho Capsule AO Append");
            CoreUtils.SetRenderTarget(cmd, mask, ClearFlag.Color, Color.black);
            cmd.SetGlobalTexture(DepthId, geometry.DepthTexture);
            cmd.SetGlobalVector(SizeId, inputs.screenSize);
            cmd.SetGlobalMatrix(VPId, inputs.vp);
            cmd.EnableScissorRect(inputs.scissor);
            foreach (Surface surface in inputs.surfaces)
                for (int submesh = 0; submesh < surface.submeshCount; submesh++)
                    cmd.DrawMesh(surface.mesh, surface.matrix, material, submesh, 0);
            cmd.DisableScissorRect();
            CoreUtils.SetRenderTarget(cmd, ao, ClearFlag.None);
            cmd.SetGlobalTexture(MaskId, mask);
            cmd.SetGlobalTexture(NormalId, geometry.NormalDepthTexture);
            cmd.SetGlobalMatrix(InvVPId, inputs.inverseVP);
            cmd.SetGlobalInt(CountId, inputs.count);
            cmd.SetGlobalVectorArray(AId, inputs.a); cmd.SetGlobalVectorArray(BId, inputs.b);
            var c = inputs.configuration;
            cmd.SetGlobalVector(ParamsId, new Vector4(c.strength, c.distance, (float)c.mode, 0));
            cmd.SetGlobalVector(CrossId, new Vector4(c.crossLength, c.crossWidth, c.crossAngle * Mathf.Deg2Rad, c.crossSoftness));
            cmd.SetGlobalFloat(RotationId, c.crossRotation * Mathf.Deg2Rad);
            cmd.EnableScissorRect(inputs.scissor);
            cmd.DrawProcedural(Matrix4x4.identity, material, c.blend == HoCapsuleAOBlendMode.Min ? 1 : 2, MeshTopology.Triangles, 3);
            cmd.DisableScissorRect(); cmd.SetGlobalInt(CountId, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        internal void Dispose()
        {
            CoreUtils.Destroy(material); material = null;
            foreach (RTHandle mask in compatibilityMasks.Values) mask.Release();
            compatibilityMasks.Clear();
        }
    }
}
