#pragma warning disable CS0618, CS0672
using System;
using System.Collections.Generic;
using lilToon.URP.Extensions.ObjectBuffer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterSpecialization
{
    /// <summary>Dedicated source publication and exact-member original ShadowCaster drawing.</summary>
    public sealed class HoCharacterRimPass : ScriptableRenderPass, IDisposable
    {
        private static readonly int ActiveId = Shader.PropertyToID("_HoRimActive");
        private static readonly int CountId = Shader.PropertyToID("_HoRimLightCount");
        private static readonly int LightsId = Shader.PropertyToID("_HoRimLights");
        private static readonly int RangesId = Shader.PropertyToID("_HoRimGroupRanges");
        private static readonly int AtlasId = Shader.PropertyToID("_HoRimShadowAtlas");
        private static readonly int AtlasSizeId = Shader.PropertyToID("_HoRimAtlasSize");
        private static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");
        private static readonly ShaderTagId ShadowTag = new ShaderTagId("ShadowCaster");
        private static readonly GlobalKeyword PunctualKeyword = GlobalKeyword.Create("_CASTING_PUNCTUAL_LIGHT_SHADOW");
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("Ho Character Rim Sources and Shadows");

        private sealed class CameraResources
        {
            internal Camera camera;
            internal GraphicsBuffer lights;
            internal RTHandle atlas;
            internal int capacity;
            internal double lastUsed;
            internal void Dispose() { lights?.Dispose(); lights = null; atlas?.Release(); atlas = null; capacity = 0; }
        }
        private struct Caster
        {
            internal Renderer renderer;
            internal Material material;
            internal int submesh, pass;
            internal bool twoSided;
        }
        private sealed class Source
        {
            internal HoCharacterRimLight component;
            internal HoObjectBufferGroup group;
            internal HoCharacterRimGpuLight gpu;
            internal Quaternion rotation;
            internal string key;
        }
        private sealed class Slice
        {
            internal Matrix4x4 view, projection;
            internal Rect viewport;
            internal Vector3 direction;
            internal Caster[] casters;
        }
        private sealed class Frame
        {
            internal CameraResources resources;
            internal HoCharacterRimGpuLight[] lights;
            internal Vector4[] ranges = new Vector4[256];
            internal Slice[] slices = Array.Empty<Slice>();
            internal Matrix4x4 cameraView, cameraProjection;
            internal Matrix4x4 cameraGPUProjection;
            internal Rect cameraViewport;
            internal Vector3 cameraPosition;
            internal Vector4 atlasSize;
            internal Vector4 previousLightDirection, previousLightPosition, previousShadowBias;
            internal bool punctualKeyword;
            internal int width, height;
        }
        private sealed class PassData { internal Frame frame; }

        private readonly Dictionary<int, CameraResources> cameras = new Dictionary<int, CameraResources>();
        private HoCharacterRimConfiguration configuration;
        private Camera setupCamera;
        private bool currentXr;
        public string Status { get; private set; } = "未启用。";
        public int LastLightCount { get; private set; }
        public int LastShadowCount { get; private set; }
        public int LastCasterDrawCount { get; private set; }
        public int LastRejectedCount { get; private set; }
        public GraphicsFormat LastShadowFormat { get; private set; }

        public HoCharacterRimPass()
        {
            // CPU membership only; never waits for the screen OB/SB/AC texture chain.
            renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
            ConfigureInput(ScriptableRenderPassInput.None);
        }

        public void Setup(HoCharacterRimConfiguration value, Camera camera)
        {
            configuration = value.Sanitized();
            setupCamera = camera;
            currentXr = camera != null && camera.stereoEnabled;
        }

        /// <summary>Host calls unconditionally at beginCameraRendering, including cameras without this feature.</summary>
        public static void ResetCameraState()
        {
            HoCharacterRimShaderState.Reset();
        }

        private Frame Prepare(Camera camera, Matrix4x4 view, Matrix4x4 projection, int width, int height)
        {
            LastLightCount = LastShadowCount = LastCasterDrawCount = LastRejectedCount = 0;
            LastShadowFormat = GraphicsFormat.None;
            var frame = new Frame { cameraView = view, cameraProjection = projection,
                cameraPosition = camera.transform.position, width = width, height = height,
                cameraGPUProjection = GL.GetGPUProjectionMatrix(projection, true),
                cameraViewport = ResolveCameraViewport(camera, null, false, width, height),
                punctualKeyword = Shader.IsKeywordEnabled(PunctualKeyword),
                previousLightDirection = Shader.GetGlobalVector("_LightDirection"),
                previousLightPosition = Shader.GetGlobalVector("_LightPosition"),
                previousShadowBias = Shader.GetGlobalVector("_ShadowBias") };
            if (!configuration.enabled || camera != setupCamera
                || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            { Status = "当前相机未启用专用 Rim。"; return frame; }
            if (SystemInfo.graphicsShaderLevel < 45)
            { Status = "专用 Rim 的对象身份与光源表需要 Shader Model 4.5。"; return frame; }
            HoObjectBufferRegistry.EnsureBuilt();
            var sources = new List<Source>();
            var memberCache = new Dictionary<HoObjectBufferGroup, List<Renderer>>();
            // Use the actual URP matrices, including base-camera aspect inherited by an overlay.
            var cameraPlanes = GeometryUtility.CalculateFrustumPlanes(projection * view);
            foreach (var component in HoCharacterRimLight.Active)
            {
                if (component == null) continue;
                if (!component.TryResolve(out var owner, out var query)) { LastRejectedCount++; continue; }
                // A parallel source's position is irrelevant: qualify the character, not the empty object's layer/radius.
                if (!memberCache.TryGetValue(owner, out var members))
                { members = new List<Renderer>(); owner.GetAssignedRenderers(members); memberCache.Add(owner, members); }
                bool cameraReceives = false;
                bool hasReceiverBounds = false;
                Bounds receiverBounds = default;
                foreach (var member in members)
                    if (member != null && member.enabled && member.gameObject.activeInHierarchy
                        && (camera.cullingMask & (1 << member.gameObject.layer)) != 0 && FiniteBounds(member.bounds))
                    {
                        cameraReceives = true;
                        if (!hasReceiverBounds) { receiverBounds = member.bounds; hasReceiverBounds = true; }
                        else receiverBounds.Encapsulate(member.bounds);
                    }
                if (!cameraReceives || (!currentXr && !GeometryUtility.TestPlanesAABB(cameraPlanes, receiverBounds)))
                { component.Status = "所属组不在当前相机接收范围内。"; continue; }
                if (!component.TryGetLinearRadiance(out Color radiance)) continue;
                var source = new Source { component = component, group = owner,
                    rotation = component.transform.rotation, key = StablePath(component.transform) };
                source.gpu.directionWidth = new Vector4(component.Direction.x, component.Direction.y, component.Direction.z,
                    Mathf.Clamp01(component.width));
                source.gpu.colorSoftness = new Vector4(radiance.r, radiance.g, radiance.b,
                    Mathf.Clamp01(component.softness));
                source.gpu.region = query.ShaderValue;
                sources.Add(source);
            }
            sources.Sort((a, b) =>
            {
                int result = a.group.groupId.CompareTo(b.group.groupId);
                if (result == 0) result = string.CompareOrdinal(a.key, b.key);
                return result != 0 ? result : a.component.GetInstanceID().CompareTo(b.component.GetInstanceID());
            });
            var selected = new List<Source>();
            int lastGroup = 0, groupCount = 0;
            foreach (var source in sources)
            {
                if (source.group.groupId != lastGroup) { lastGroup = source.group.groupId; groupCount = 0; }
                if (++groupCount > configuration.maxLightsPerCharacter)
                { source.component.Status = "超过所属角色活动源预算。"; LastRejectedCount++; continue; }
                selected.Add(source);
            }
            int requestedShadows = 0;
            foreach (var source in selected)
                if (source.component.castShadows && source.component.shadowStrength > 0) requestedShadows++;
            // The budget is an upper bound. A small atlas still publishes the
            // tiles it can hold instead of choosing <16px tiles and rejecting all.
            int columnsAtMinimumResolution = Mathf.Max(1, configuration.atlasSize / 16);
            int shadowCount = Mathf.Min(requestedShadows,
                Mathf.Min(configuration.shadowBudget, columnsAtMinimumResolution * columnsAtMinimumResolution));
            int side = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(shadowCount)));
            int tileSize = configuration.atlasSize / side;
            GraphicsFormat shadowFormat = SelectShadowFormat();
            LastShadowFormat = shadowFormat;
            bool depthSupported = shadowFormat != GraphicsFormat.None;
            var slices = new List<Slice>();
            var output = new List<HoCharacterRimGpuLight>();
            foreach (var source in selected)
            {
                bool shadow = source.component.castShadows && source.component.shadowStrength > 0;
                if (shadow && currentXr)
                {
                    source.component.Status = "当前 XR 相机暂不支持专用 Rim 阴影；请求投影的源已跳过，关闭投影可用。";
                    LastRejectedCount++; continue;
                }
                if (shadow && (slices.Count >= shadowCount || tileSize < 16 || !depthSupported))
                {
                    source.component.Status = !depthSupported ? "阴影深度格式不可用；请求阴影的源已跳过。"
                        : "超过当前相机阴影预算或图集容量；请求阴影的源已跳过。";
                    LastRejectedCount++; continue;
                }
                if (shadow)
                {
                    if (!memberCache.TryGetValue(source.group, out var members))
                    { members = new List<Renderer>(); source.group.GetAssignedRenderers(members); memberCache.Add(source.group, members); }
                    if (!BuildSlice(source, members, slices.Count, side, tileSize, configuration, out Slice slice, out string reason))
                    { source.component.Status = reason; LastRejectedCount++; continue; }
                    slices.Add(slice);
                    foreach (var caster in slice.casters)
                        LastCasterDrawCount += caster.twoSided ? 2 : 1;
                }
                int group = source.group.groupId;
                if (frame.ranges[group].y == 0) frame.ranges[group].x = output.Count;
                frame.ranges[group].y++;
                output.Add(source.gpu);
                source.component.Status = shadow ? "已发布：所属角色局部阴影。" : "已发布：显式无遮挡补边。";
            }
            LastLightCount = output.Count; LastShadowCount = slices.Count;
            frame.lights = output.ToArray(); frame.slices = slices.ToArray();
            if (output.Count == 0)
            {
                Status = currentXr && LastRejectedCount > 0
                    ? "XR 相机仅支持关闭投影的 Rim 源；当前请求投影的源均已跳过。"
                    : "没有可发布的有效 Rim 源。";
                return frame;
            }
            int id = camera.GetInstanceID();
            if (!cameras.TryGetValue(id, out var resource))
            { resource = new CameraResources { camera = camera }; cameras.Add(id, resource); }
            resource.lastUsed = Time.realtimeSinceStartupAsDouble;
            if (resource.lights == null || resource.capacity < output.Count)
            {
                resource.lights?.Dispose(); resource.capacity = Mathf.NextPowerOfTwo(output.Count);
                resource.lights = new GraphicsBuffer(GraphicsBuffer.Target.Structured, resource.capacity, HoCharacterRimGpuLight.Stride);
            }
            resource.lights.SetData(frame.lights);
            if (slices.Count > 0)
            {
                var descriptor = new RenderTextureDescriptor(configuration.atlasSize, configuration.atlasSize)
                {
                    graphicsFormat = GraphicsFormat.None, depthStencilFormat = shadowFormat,
                    msaaSamples = 1, shadowSamplingMode = ShadowSamplingMode.RawDepth, useMipMap = false
                };
                RenderingUtils.ReAllocateIfNeeded(ref resource.atlas, descriptor, FilterMode.Point, TextureWrapMode.Clamp,
                    name: "Ho Character Rim Shadow Atlas");
            }
            frame.resources = resource;
            frame.atlasSize = new Vector4(1f / configuration.atlasSize, 1f / configuration.atlasSize,
                configuration.atlasSize, configuration.atlasSize);
            Status = $"Rim {LastLightCount}，阴影 {LastShadowCount}，原材质 ShadowCaster 绘制 {LastCasterDrawCount}，预算/无效跳过 {LastRejectedCount}。"
                + (LastShadowCount > 0 ? $" 深度 {shadowFormat}。" : string.Empty)
                + (currentXr ? " XR 相机仅支持关闭投影的源。" : string.Empty);
            Prune(camera);
            return frame;
        }

        private static string StablePath(Transform value)
        {
            string result = value.GetSiblingIndex().ToString("D5") + ":" + value.name;
            for (Transform parent = value.parent; parent != null; parent = parent.parent)
                result = parent.GetSiblingIndex().ToString("D5") + ":" + parent.name + "/" + result;
            return value.gameObject.scene.path + "/" + result;
        }

        private static bool BuildSlice(Source source, List<Renderer> members, int index, int columns, int tileSize,
            HoCharacterRimConfiguration config, out Slice slice, out string reason)
        {
            slice = null; reason = "所属组没有有效几何 bounds。";
            bool hasBounds = false;
            Bounds bounds = default;
            var casters = new List<Caster>();
            var lodGroups = new Dictionary<LODGroup, HashSet<Renderer>>();
            // A fixed highest available LOD for the local shadow avoids drawing all LODs at once.
            // It is independent of the viewing camera, including off-screen upstream geometry.
            foreach (var renderer in members)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!FiniteBounds(renderer.bounds))
                { reason = "所属角色包含无效 bounds；该阴影源已跳过。"; return false; }
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds);
                if (renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                var lod = renderer.GetComponentInParent<LODGroup>();
                if (lod != null && lod.enabled)
                {
                    if (!lodGroups.TryGetValue(lod, out var keep))
                    {
                        keep = new HashSet<Renderer>();
                        foreach (var level in lod.GetLODs())
                        {
                            foreach (var candidate in level.renderers)
                                if (candidate != null && members.Contains(candidate)) keep.Add(candidate);
                            if (keep.Count > 0) break;
                        }
                        lodGroups.Add(lod, keep);
                    }
                    if (keep.Count > 0 && !keep.Contains(renderer)) continue;
                }
                int submeshCount;
                if (renderer is SkinnedMeshRenderer skinned)
                    submeshCount = skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
                else if (renderer is MeshRenderer)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    submeshCount = filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
                }
                else { reason = "所属组含不支持的投射 Renderer；该阴影源已跳过。"; return false; }
                var materials = renderer.sharedMaterials;
                for (int submesh = 0; submesh < submeshCount; submesh++)
                {
                    Material material = materials.Length > 0 ? materials[Mathf.Min(submesh, materials.Length - 1)] : null;
                    if (material == null) continue;
                    int pass = FindShadowPass(material);
                    // A material without a ShadowCaster does not cast in Unity either.
                    if (pass < 0) continue;
                    casters.Add(new Caster { renderer = renderer, material = material, submesh = submesh, pass = pass,
                        twoSided = renderer.shadowCastingMode == ShadowCastingMode.TwoSided });
                }
                // Unity draws surplus material slots over the final submesh.
                for (int materialIndex = submeshCount; submeshCount > 0 && materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    int pass = material != null ? FindShadowPass(material) : -1;
                    if (pass >= 0) casters.Add(new Caster { renderer = renderer, material = material,
                        submesh = submeshCount - 1, pass = pass, twoSided = renderer.shadowCastingMode == ShadowCastingMode.TwoSided });
                }
            }
            if (!hasBounds) return false;
            if (!FiniteBounds(bounds)) { reason = "角色合并 bounds 超出有限数值范围；该阴影源已跳过。"; return false; }
            Quaternion rotation = source.rotation;
            Matrix4x4 basis = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
            Vector3 lo = bounds.min, hi = bounds.max, min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = basis.MultiplyPoint3x4(new Vector3((corner & 1) == 0 ? lo.x : hi.x,
                    (corner & 2) == 0 ? lo.y : hi.y, (corner & 4) == 0 ? lo.z : hi.z));
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            if (!FiniteVector(min) || !FiniteVector(max))
            { reason = "角色光视角 bounds 无效；该阴影源已跳过。"; return false; }
            float extent = Mathf.Max(.01f, Mathf.Max(max.x * .5f - min.x * .5f, max.y * .5f - min.y * .5f));
            extent *= tileSize / Mathf.Max(1f, tileSize - 8f);
            float texel = 2f * extent / tileSize;
            Vector3 center = min * .5f + max * .5f;
            if (!HoCharacterRimLight.Finite(extent) || !HoCharacterRimLight.Finite(texel) || texel <= 0 || !FiniteVector(center))
            { reason = "阴影投影尺寸或 texel 超出有限数值范围；该源已跳过。"; return false; }
            center.x = Mathf.Round(center.x / texel) * texel;
            center.y = Mathf.Round(center.y / texel) * texel;
            float padding = Mathf.Max(.01f, texel * (config.depthBias + config.normalBias + 4));
            Vector3 origin = rotation * new Vector3(center.x, center.y, min.z - padding);
            float depth = Mathf.Max(.02f, max.z - min.z + 2 * padding);
            if (!HoCharacterRimLight.Finite(padding) || !HoCharacterRimLight.Finite(depth) || !FiniteVector(origin)
                || !FiniteVector(center) || depth <= .001f)
            { reason = "阴影偏移或深度范围溢出；该源已跳过。"; return false; }
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(origin, rotation, Vector3.one).inverse;
            Matrix4x4 projection = Matrix4x4.Ortho(-extent, extent, -extent, extent, .001f, depth);
            Matrix4x4 shadowProjection = projection;
            if (SystemInfo.usesReversedZBuffer)
                for (int c = 0; c < 4; c++) shadowProjection[2, c] = -shadowProjection[2, c];
            Matrix4x4 scaleBias = Matrix4x4.identity;
            scaleBias.m00 = scaleBias.m11 = scaleBias.m22 = .5f;
            scaleBias.m03 = scaleBias.m13 = scaleBias.m23 = .5f;
            source.gpu.worldToShadow = scaleBias * shadowProjection * view;
            int x = index % columns * tileSize, y = index / columns * tileSize;
            float atlas = config.atlasSize;
            source.gpu.tile = new Vector4(x / atlas, y / atlas, tileSize / atlas, tileSize / atlas);
            source.gpu.shadow = new Vector4(1, Mathf.Clamp01(source.component.shadowStrength), config.normalBias * texel,
                config.depthBias * texel / depth);
            if (!FiniteMatrix(view) || !FiniteMatrix(projection) || !FiniteMatrix(source.gpu.worldToShadow)
                || !HoCharacterRimLight.Finite(source.gpu.shadow.z) || !HoCharacterRimLight.Finite(source.gpu.shadow.w))
            { reason = "阴影矩阵或接收偏移不是有限数值；该源已跳过。"; return false; }
            slice = new Slice { view = view, projection = projection, direction = source.gpu.directionWidth,
                viewport = new Rect(x, y, tileSize, tileSize), casters = casters.ToArray() };
            reason = null;
            return true;
        }

        private static int FindShadowPass(Material material)
        {
            if (!material.GetShaderPassEnabled("ShadowCaster")) return -1;
            for (int i = 0; i < material.passCount; i++)
                if (material.shader.FindPassTagValue(i, LightModeTag) == ShadowTag) return i;
            return -1;
        }

        private static bool FiniteBounds(Bounds bounds)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            return FiniteVector(c) && FiniteVector(e) && e.x >= 0 && e.y >= 0 && e.z >= 0
                && FiniteVector(bounds.min) && FiniteVector(bounds.max);
        }

        private static bool FiniteVector(Vector3 value) => HoCharacterRimLight.Finite(value.x)
            && HoCharacterRimLight.Finite(value.y) && HoCharacterRimLight.Finite(value.z);

        private static bool FiniteMatrix(Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++) if (!HoCharacterRimLight.Finite(value[i])) return false;
            return true;
        }

        private static GraphicsFormat SelectShadowFormat()
        {
            // Raw shadowmap sampling has its own capability. D3D11 can report D32 Sample=false
            // while supporting its raw depth SRV. Do not treat ordinary texture-format Sample
            // as the depth-view test. URP ShadowUtils likewise derives a depth/stencil format.
            // https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SystemInfo-supportsRawShadowDepthSampling.html
            // Unity's legacy shadowmap flag can be false on D3D12. This atlas
            // uses the ordinary depth-resource SRV path, supported by that
            // backend when depth RenderTextures are available. Qualify it by
            // actual depth readback/receiver pixels, not the legacy flag alone.
            bool depthSrv = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
                && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth);
            if (!SystemInfo.supportsRawShadowDepthSampling && !depthSrv) return GraphicsFormat.None;
            GraphicsFormat[] candidates =
            {
                GraphicsFormatUtility.GetDepthStencilFormat(32, 0),
                SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                GraphicsFormatUtility.GetDepthStencilFormat(24, 0),
                GraphicsFormatUtility.GetDepthStencilFormat(16, 0)
            };
            foreach (GraphicsFormat format in candidates)
                if (format != GraphicsFormat.None && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)) return format;
            return GraphicsFormat.None;
        }

        private static Rect ResolveCameraViewport(Camera camera, Camera baseCamera, bool directOutput, int width, int height)
        {
            // URP's intermediate descriptor already contains the scaled viewport size, with zero offset.
            // The final/backbuffer target retains the actual pixel rectangle (overlays inherit their base).
            if (!directOutput) return new Rect(0, 0, width, height);
            return (baseCamera != null ? baseCamera : camera).pixelRect;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            currentXr = camera.xr != null && camera.xr.enabled;
            Frame frame = Prepare(camera.camera, camera.GetViewMatrix(), camera.GetProjectionMatrix(),
                camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
            frame.cameraViewport = ResolveCameraViewport(camera.camera,
                camera.renderType == CameraRenderType.Overlay ? camera.baseCamera : null,
                resources.isActiveTargetBackBuffer, frame.width, frame.height);
            frame.cameraGPUProjection = GL.GetGPUProjectionMatrix(frame.cameraProjection,
                camera.targetTexture != null || !resources.isActiveTargetBackBuffer);
            using (var builder = graph.AddRasterRenderPass<PassData>("Ho Character Rim Sources and Shadows", out var data, Sampler))
            {
                data.frame = frame;
                // Imported persistent storage survives the opaque material's implicit global read.
                if (frame.slices.Length > 0)
                {
                    TextureHandle atlas = graph.ImportTexture(frame.resources.atlas);
                    builder.SetRenderAttachmentDepth(atlas, AccessFlags.WriteAll);
                    builder.SetGlobalTextureAfterPass(atlas, AtlasId);
                }
                else if (frame.lights != null && frame.lights.Length > 0)
                {
                    // A no-shadow source never samples the atlas, but the receiver still declares its SRV.
                    builder.SetGlobalTextureAfterPass(graph.defaultResources.blackTexture, AtlasId);
                }
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => DrawAndPublish(context.cmd, d.frame));
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData;
            currentXr = camera.xr != null && camera.xr.enabled;
            Frame frame = Prepare(camera.camera, camera.GetViewMatrix(), camera.GetProjectionMatrix(),
                camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
            RTHandle colorTarget = camera.renderer.cameraColorTargetHandle;
            bool directOutput = colorTarget == null || colorTarget.rt == null || colorTarget.rt == camera.targetTexture;
            frame.cameraViewport = ResolveCameraViewport(camera.camera,
                camera.renderType == CameraRenderType.Overlay ? camera.baseCamera : null,
                directOutput, frame.width, frame.height);
            frame.cameraGPUProjection = GL.GetGPUProjectionMatrix(frame.cameraProjection, camera.targetTexture != null || !directOutput);
            var command = CommandBufferPool.Get("Ho Character Rim Sources and Shadows");
            if (frame.slices.Length > 0) CoreUtils.SetRenderTarget(command, frame.resources.atlas);
            DrawAndPublish(CommandBufferHelpers.GetRasterCommandBuffer(command), frame);
            if (frame.slices.Length > 0) command.SetGlobalTexture(AtlasId, frame.resources.atlas);
            else if (frame.lights != null && frame.lights.Length > 0) command.SetGlobalTexture(AtlasId, Texture2D.blackTexture);
            if (frame.slices.Length > 0)
                CoreUtils.SetRenderTarget(command, camera.renderer.cameraColorTargetHandle, camera.renderer.cameraDepthTargetHandle,
                    ClearFlag.None, Color.clear);
            if (frame.slices.Length > 0) command.SetViewport(frame.cameraViewport);
            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
        }

        private static void DrawAndPublish(RasterCommandBuffer cmd, Frame frame)
        {
            cmd.SetKeyword(HoCharacterRimShaderState.HairReceiverKeyword, false);
            cmd.SetGlobalFloat(ActiveId, 0); cmd.SetGlobalInt(CountId, 0);
            var emptyLights = HoCharacterRimShaderState.EmptyLights;
            if (emptyLights != null) cmd.SetGlobalBuffer(LightsId, emptyLights);
            if (frame.resources == null || frame.lights == null || frame.lights.Length == 0) return;
            if (frame.slices.Length > 0)
            {
                cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1, 0);
                cmd.SetKeyword(PunctualKeyword, false);
                cmd.SetGlobalDepthBias(0, 0);
                foreach (var slice in frame.slices)
                {
                    cmd.SetViewport(slice.viewport); cmd.EnableScissorRect(slice.viewport);
                    cmd.SetViewProjectionMatrices(slice.view, slice.projection);
                    Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * slice.view;
                    cmd.SetGlobalMatrix("unity_WorldToCamera", worldToCamera);
                    cmd.SetGlobalMatrix("unity_CameraToWorld", worldToCamera.inverse);
                    cmd.SetGlobalVector("_LightDirection", slice.direction);
                    cmd.SetGlobalVector("_LightPosition", Vector4.zero);
                    // Receiver offsets own the quality settings; do not apply the same bias again in caster vertices.
                    cmd.SetGlobalVector("_ShadowBias", Vector4.zero);
                    foreach (var caster in slice.casters)
                    {
                        if (caster.renderer == null || caster.material == null) continue;
                        cmd.DrawRenderer(caster.renderer, caster.material, caster.submesh, caster.pass);
                        if (caster.twoSided)
                        {
                            cmd.SetInvertCulling(true);
                            cmd.DrawRenderer(caster.renderer, caster.material, caster.submesh, caster.pass);
                            cmd.SetInvertCulling(false);
                        }
                    }
                }
                cmd.DisableScissorRect(); cmd.SetInvertCulling(false);
                cmd.SetKeyword(PunctualKeyword, frame.punctualKeyword);
                cmd.SetViewProjectionMatrices(frame.cameraView, frame.cameraProjection);
                Matrix4x4 cameraWorldToView = Matrix4x4.Scale(new Vector3(1, 1, -1)) * frame.cameraView;
                cmd.SetGlobalMatrix("unity_WorldToCamera", cameraWorldToView);
                cmd.SetGlobalMatrix("unity_CameraToWorld", cameraWorldToView.inverse);
                Matrix4x4 inverseView = frame.cameraView.inverse;
                Matrix4x4 inverseProjection = frame.cameraGPUProjection.inverse;
                cmd.SetGlobalMatrix("unity_MatrixInvV", inverseView);
                cmd.SetGlobalMatrix("unity_MatrixInvP", inverseProjection);
                cmd.SetGlobalMatrix("unity_MatrixInvVP", inverseView * inverseProjection);
                cmd.SetGlobalVector("_WorldSpaceCameraPos", frame.cameraPosition);
                cmd.SetGlobalVector("_ShadowBias", frame.previousShadowBias);
                cmd.SetGlobalVector("_LightDirection", frame.previousLightDirection);
                cmd.SetGlobalVector("_LightPosition", frame.previousLightPosition);
                cmd.SetViewport(frame.cameraViewport);
            }
            cmd.SetGlobalBuffer(LightsId, frame.resources.lights);
            cmd.SetGlobalVectorArray(RangesId, frame.ranges);
            cmd.SetGlobalVector(AtlasSizeId, frame.atlasSize);
            cmd.SetGlobalInt(CountId, frame.lights.Length);
            cmd.SetGlobalFloat(ActiveId, 1);
            cmd.SetKeyword(HoCharacterRimShaderState.HairReceiverKeyword, true);
        }

        private void Prune(Camera current)
        {
            var expired = new List<int>();
            foreach (var item in cameras)
                if (item.Value.camera == null || (item.Value.camera != current
                    && Time.realtimeSinceStartupAsDouble - item.Value.lastUsed > 10)) expired.Add(item.Key);
            foreach (int key in expired) { cameras[key].Dispose(); cameras.Remove(key); }
        }

        public void Dispose()
        {
            ResetCameraState();
            foreach (var item in cameras.Values) item.Dispose();
            cameras.Clear();
        }
    }
}
