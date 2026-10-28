#pragma warning disable CS0618
using lilToon.URP.Extensions.SurfaceBuffer;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.RT
{
    // Per-pass borrowed handles + a camera snapshot. No output allocation or lighting model.
    internal readonly struct HoRTPrimarySurfaceInputs
    {
        private static readonly int ColorId = Shader.PropertyToID("_HoRTPrimaryColor");
        private static readonly int NormalId = Shader.PropertyToID("_HoRTPrimaryNormal");
        private static readonly int MaterialId = Shader.PropertyToID("_HoRTPrimaryMaterial");
        private static readonly int ReflectionId = Shader.PropertyToID("_HoRTPrimaryReflection");
        private static readonly int OwnerId = Shader.PropertyToID("_HoRTPrimaryOwner");
        private static readonly int StateId = Shader.PropertyToID("_HoRTPrimaryState");
        private static readonly int SizeId = Shader.PropertyToID("_HoRTPrimarySize");
        private static readonly int InverseVPId = Shader.PropertyToID("_HoRTPrimaryInverseViewProjection");
        private static readonly int CameraPositionId = Shader.PropertyToID("_HoRTPrimaryCameraPosition");
        private static readonly int TowardsEyeId = Shader.PropertyToID("_HoRTPrimaryTowardsEye");
        private static readonly int ZBufferId = Shader.PropertyToID("_HoRTPrimaryZBufferParameters");
        private static readonly int ClipId = Shader.PropertyToID("_HoRTPrimaryClipParameters");

        private readonly HoSurfaceAssociationInputs association;
        private readonly TextureHandle color, normal, material, reflection, owner;
        private readonly CameraSnapshot camera;
        private readonly Vector4 state, size;

        private HoRTPrimarySurfaceInputs(HoSurfaceAssociationInputs association, HoSurfaceBufferRenderGraphResources sb,
            CameraSnapshot camera, Vector4 state, Vector4 size, TextureHandle fallback)
        {
            this.association = association; this.camera = camera; this.state = state; this.size = size;
            color = state.x > .5f ? sb.colorTexture : fallback; normal = state.x > .5f ? sb.normalTexture : fallback;
            material = state.x > .5f ? sb.materialTexture : fallback; reflection = state.x > .5f ? sb.reflectionTexture : fallback;
            owner = state.z > .5f ? sb.ownerTexture : fallback;
        }

        internal static HoRTPrimarySurfaceInputs Read(RenderGraph graph, ContextContainer frame, IBaseRenderGraphBuilder builder, bool includeOwner = false)
        {
            var association = HoSurfaceAssociationInputs.Read(graph, frame, builder);
            var sb = frame.GetOrCreate<HoSurfaceBufferRenderGraphResources>();
            var camera = new CameraSnapshot(frame.Get<UniversalCameraData>());
            bool attributes = sb.colorTexture.IsValid() && sb.normalTexture.IsValid() && sb.materialTexture.IsValid() && sb.reflectionTexture.IsValid();
            bool hasOwner = includeOwner && sb.ownerTexture.IsValid();
            Vector4 size = Vector4.zero;
            if (sb.depthTexture.IsValid())
            {
                var desc = graph.GetTextureDesc(sb.depthTexture);
                size = TextureSize(desc.width, desc.height);
                camera.supported &= desc.dimension == TextureDimension.Tex2D;
            }
            if (attributes)
            {
                builder.UseTexture(sb.colorTexture); builder.UseTexture(sb.normalTexture);
                builder.UseTexture(sb.materialTexture); builder.UseTexture(sb.reflectionTexture);
            }
            if (hasOwner) builder.UseTexture(sb.ownerTexture);
            if (!attributes || !hasOwner) builder.UseTexture(graph.defaultResources.blackTexture);
            return new HoRTPrimarySurfaceInputs(association, sb, camera,
                new Vector4(attributes ? 1 : 0, camera.supported ? 1 : 0, hasOwner ? 1 : 0, 0), size, graph.defaultResources.blackTexture);
        }

        internal void Bind(RasterCommandBuffer cmd)
        {
            association.Bind(cmd); camera.Bind(cmd);
            cmd.SetGlobalVector(StateId, state); cmd.SetGlobalVector(SizeId, size);
            // Native raygeneration needs every declared texture slot even
            // when its validity branch returns before sampling that slot.
            cmd.SetGlobalTexture(ColorId, color); cmd.SetGlobalTexture(NormalId, normal);
            cmd.SetGlobalTexture(MaterialId, material); cmd.SetGlobalTexture(ReflectionId, reflection);
            cmd.SetGlobalTexture(OwnerId, owner);
        }

        internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader, int kernel)
        {
            association.Bind(cmd, shader, kernel); camera.Bind(cmd, shader);
            cmd.SetComputeVectorParam(shader, StateId, state); cmd.SetComputeVectorParam(shader, SizeId, size);
            cmd.SetComputeTextureParam(shader, kernel, ColorId, color); cmd.SetComputeTextureParam(shader, kernel, NormalId, normal);
            cmd.SetComputeTextureParam(shader, kernel, MaterialId, material); cmd.SetComputeTextureParam(shader, kernel, ReflectionId, reflection);
            cmd.SetComputeTextureParam(shader, kernel, OwnerId, owner);
        }

        internal static void BindCompatibility(CommandBuffer cmd, CameraData data, bool includeOwner = false)
        {
            HoSurfaceAssociationInputs.BindCompatibility(cmd);
            var camera = new CameraSnapshot(data); var source = CompatibilitySource(ref camera, includeOwner, out var state, out var size);
            camera.Bind(CommandBufferHelpers.GetRasterCommandBuffer(cmd));
            cmd.SetGlobalVector(StateId, state); cmd.SetGlobalVector(SizeId, size);
            var fallback = new RenderTargetIdentifier(Texture2D.blackTexture);
            cmd.SetGlobalTexture(ColorId, state.x > .5f ? source.ColorTexture.nameID : fallback);
            cmd.SetGlobalTexture(NormalId, state.x > .5f ? source.NormalTexture.nameID : fallback);
            cmd.SetGlobalTexture(MaterialId, state.x > .5f ? source.MaterialTexture.nameID : fallback);
            cmd.SetGlobalTexture(ReflectionId, state.x > .5f ? source.ReflectionTexture.nameID : fallback);
            cmd.SetGlobalTexture(OwnerId, state.z > .5f ? source.OwnerTexture.nameID : fallback);
        }

        internal static void BindCompatibilityCompute(CommandBuffer cmd, ComputeShader shader, int kernel, CameraData data, bool includeOwner = false)
        {
            HoSurfaceAssociationInputs.BindCompatibilityCompute(cmd, shader, kernel);
            var camera = new CameraSnapshot(data); var source = CompatibilitySource(ref camera, includeOwner, out var state, out var size);
            camera.Bind(CommandBufferHelpers.GetComputeCommandBuffer(cmd), shader);
            cmd.SetComputeVectorParam(shader, StateId, state); cmd.SetComputeVectorParam(shader, SizeId, size);
            var fallback = new RenderTargetIdentifier(Texture2D.blackTexture);
            cmd.SetComputeTextureParam(shader, kernel, ColorId, state.x > .5f ? source.ColorTexture.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, NormalId, state.x > .5f ? source.NormalTexture.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, MaterialId, state.x > .5f ? source.MaterialTexture.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, ReflectionId, state.x > .5f ? source.ReflectionTexture.nameID : fallback);
            cmd.SetComputeTextureParam(shader, kernel, OwnerId, state.z > .5f ? source.OwnerTexture.nameID : fallback);
        }

        private static HoSurfaceBufferPass CompatibilitySource(ref CameraSnapshot camera, bool includeOwner, out Vector4 state, out Vector4 size)
        {
            var source = HoSurfaceBufferPass.CompatibilitySource;
            bool attributes = HoSurfaceBufferPass.CompatibilityProduced && source?.ColorTexture?.rt != null && source.NormalTexture?.rt != null
                && source.MaterialTexture?.rt != null && source.ReflectionTexture?.rt != null;
            var depth = HoSurfaceBufferPass.CompatibilityDepthTexture;
            camera.supported &= depth?.rt != null && depth.rt.dimension == TextureDimension.Tex2D;
            size = depth?.rt != null ? TextureSize(depth.rt.width, depth.rt.height) : Vector4.zero;
            state = new Vector4(attributes ? 1 : 0, camera.supported ? 1 : 0, includeOwner && source?.OwnerTexture?.rt != null ? 1 : 0, 0);
            return source;
        }

        private static Vector4 TextureSize(int width, int height) => new Vector4(width, height, 1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height));

        private struct CameraSnapshot
        {
            internal bool supported;
            private readonly Matrix4x4 inverseVP;
            private readonly Vector4 position, towardsEye, zBuffer, clip;
            internal CameraSnapshot(UniversalCameraData data) : this(data.camera, data.GetViewMatrix(), data.GetProjectionMatrix(), data.xr.enabled) { }
            internal CameraSnapshot(CameraData data) : this(data.camera, data.GetViewMatrix(), data.GetProjectionMatrix(), data.xr.enabled) { }
            private CameraSnapshot(Camera camera, Matrix4x4 view, Matrix4x4 projection, bool xr)
            {
                float near = camera.nearClipPlane, far = camera.farClipPlane;
                bool reversed = SystemInfo.usesReversedZBuffer;
                clip = new Vector4(near, far, camera.orthographic ? 1 : 0, reversed ? 1 : 0);
                float ratio = far / near;
                zBuffer = reversed ? new Vector4(ratio - 1, 1, (ratio - 1) / far, 1 / far)
                    : new Vector4(1 - ratio, ratio, (1 - ratio) / far, ratio / far);
                inverseVP = (GL.GetGPUProjectionMatrix(projection, true) * view).inverse;
                position = view.inverse.GetColumn(3); towardsEye = view.GetRow(2); towardsEye.w = 0;
                // Initial bridge covers mono standard perspective/ortho, including lens shift and jitter.
                var standard = camera.orthographic ? Matrix4x4.Ortho(-1, 1, -1, 1, near, far)
                    : Matrix4x4.Perspective(60, 1, near, far);
                supported = !xr && near > 0 && far > near && Mathf.Abs(projection.m20) < 1e-6f && Mathf.Abs(projection.m21) < 1e-6f
                    && Mathf.Approximately(projection.m22, standard.m22) && Mathf.Approximately(projection.m23, standard.m23)
                    && Mathf.Approximately(projection.m32, standard.m32) && Mathf.Approximately(projection.m33, standard.m33);
            }
            internal void Bind(RasterCommandBuffer cmd)
            {
                cmd.SetGlobalMatrix(InverseVPId, inverseVP); cmd.SetGlobalVector(CameraPositionId, position);
                cmd.SetGlobalVector(TowardsEyeId, towardsEye); cmd.SetGlobalVector(ZBufferId, zBuffer); cmd.SetGlobalVector(ClipId, clip);
            }
            internal void Bind(ComputeCommandBuffer cmd, ComputeShader shader)
            {
                cmd.SetComputeMatrixParam(shader, InverseVPId, inverseVP); cmd.SetComputeVectorParam(shader, CameraPositionId, position);
                cmd.SetComputeVectorParam(shader, TowardsEyeId, towardsEye); cmd.SetComputeVectorParam(shader, ZBufferId, zBuffer); cmd.SetComputeVectorParam(shader, ClipId, clip);
            }
        }
    }
}
