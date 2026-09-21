#pragma warning disable CS0618, CS0672
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.CharacterShadow
{
    [DisallowMultipleRendererFeature("Ho-CharacterShadow")]
    public sealed class HoCharacterShadowRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private HoCharacterShadowSettings settings = new HoCharacterShadowSettings();
        public HoCharacterShadowSettings Settings => settings;

        /// <summary>
        /// Last per-slice cull result ("slice0: lights=1 index=0 bounds=true list=true"), for the
        /// component status line and for the batch validation sweep. Diagnostic only.
        /// </summary>
        public static string LastCullStatus = "";

        private HoCharacterShadowPass pass;
        private HoCharacterShadowDebugPass debugPass;
        private Material debugMaterial;

        public override void Create()
        {
            pass?.Dispose();
            pass = new HoCharacterShadowPass();
            debugPass = new HoCharacterShadowDebugPass();
            RenderPipelineManager.beginCameraRendering -= ResetCamera;
            RenderPipelineManager.beginCameraRendering += ResetCamera;
        }

        private static void ResetCamera(ScriptableRenderContext context, Camera camera)
        { Shader.SetGlobalFloat("_HoCSActive", 0); }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            LastCullStatus = "add:";
            if (settings == null || !settings.enabled || pass == null) { LastCullStatus += "noSettings"; return; }
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) { LastCullStatus += "camType"; return; }
            int main = renderingData.lightData.mainLightIndex;
            Light light = main >= 0 ? renderingData.lightData.visibleLights[main].light : null;
            if (light == null || light.type != LightType.Directional || light.shadows == LightShadows.None
                || !renderingData.shadowData.supportsMainLightShadows || HoCharacterShadow.Active.Count == 0)
            { LastCullStatus += $"light(main={main},null={light == null},dir={(light != null && light.type == LightType.Directional)},shadows={(light != null && light.shadows != LightShadows.None)},supports={renderingData.shadowData.supportsMainLightShadows},active={HoCharacterShadow.Active.Count})"; return; }
            var frame = HoCharacterShadowFrame.Build(camera, light, renderingData.cameraData.GetViewMatrix(),
                renderingData.cameraData.GetProjectionMatrix(), settings);
            if (frame.slices.Count == 0) { LastCullStatus += "noSlices"; return; }
            pass.Setup(frame);
            renderer.EnqueuePass(pass);
            if (settings.debugMode == HoCharacterShadowDebugMode.Off) return;
            if (debugMaterial == null)
            {
                Shader shader = settings.debugShader != null ? settings.debugShader : Shader.Find("Hidden/Ho-CharacterShadow/Debug");
                if (shader != null) debugMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
            if (debugMaterial != null)
            {
                debugPass.Setup(debugMaterial, settings.debugMode, settings.debugCharacter);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= ResetCamera;
            pass?.Dispose();
            CoreUtils.Destroy(debugMaterial);
            Shader.SetGlobalFloat("_HoCSActive", 0);
        }
    }

    internal sealed class HoCharacterShadowResources : ContextItem
    {
        internal TextureHandle atlas;
        public override void Reset() { atlas = TextureHandle.nullHandle; }
    }

    internal sealed class HoCharacterShadowPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("Ho-CharacterShadow");
        private static readonly GlobalKeyword Punctual = GlobalKeyword.Create("_CASTING_PUNCTUAL_LIGHT_SHADOW");
        internal static readonly int AtlasId = Shader.PropertyToID("_HoCSAtlas");
        private HoCharacterShadowFrame frame;
        private RTHandle compatibilityAtlas;
        private RTHandle persistentAtlas;
        private Camera cullingCamera;
        private sealed class PassData
        {
            internal HoCharacterShadowFrame frame;
            internal RendererListHandle[] lists;
        }

        internal HoCharacterShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses; }
        internal void Setup(HoCharacterShadowFrame value) { frame = value; }
        internal void Dispose()
        {
            compatibilityAtlas?.Release(); compatibilityAtlas = null;
            persistentAtlas?.Release(); persistentAtlas = null;
            if (cullingCamera != null) CoreUtils.Destroy(cullingCamera.gameObject);
            cullingCamera = null;
        }

        private static RenderTextureDescriptor Descriptor(int size)
        {
            GraphicsFormat format = GraphicsFormat.D32_SFloat;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)
                || !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
                format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
            return new RenderTextureDescriptor(size, size)
            {
                graphicsFormat = GraphicsFormat.None, depthStencilFormat = format,
                msaaSamples = 1, volumeDepth = 1, dimension = TextureDimension.Tex2D,
                shadowSamplingMode = ShadowSamplingMode.RawDepth, useMipMap = false
            };
        }

        // A separate CullingResults per local projection keeps native shadow split state isolated
        // from URP's main CSM and from other CS characters. No HoURP modifications required.
        private bool Cull(CullContextData context, Camera camera, HoCharacterShadowFrame frame,
            HoCharacterShadowSlice slice, out ShadowDrawingSettings drawing)
        {
            drawing = default;
            if (cullingCamera == null)
            {
                var cameraObject = new GameObject("Ho-CS Culling Camera", typeof(Camera)) { hideFlags = HideFlags.HideAndDontSave };
                cullingCamera = cameraObject.GetComponent<Camera>();
                cullingCamera.enabled = false;
            }
            cullingCamera.transform.SetPositionAndRotation(slice.origin, frame.light.transform.rotation);
            cullingCamera.orthographic = true;
            cullingCamera.orthographicSize = 1f / slice.projection.m00;
            cullingCamera.aspect = 1;
            cullingCamera.nearClipPlane = slice.nearPlane;
            cullingCamera.farClipPlane = slice.farPlane;
            cullingCamera.cullingMask = frame.light.cullingMask;
            cullingCamera.useOcclusionCulling = false;
            // Build the culling parameters from the light-space camera alone. Seeding them from
            // the player camera and then patching individual fields leaks viewing-camera state
            // (lodParameters, isOrthographic, culling options) into the local projection, which
            // made the atlas content depend on how far the player camera happened to be.
            if (!cullingCamera.TryGetCullingParameters(false, out ScriptableCullingParameters parameters))
            { HoCharacterShadowRendererFeature.LastCullStatus += "|lightParams=false"; return false; }
            parameters.cullingMatrix = slice.projection * slice.view;
            parameters.origin = slice.origin;
            parameters.cullingMask = (uint)frame.light.cullingMask;
            parameters.isOrthographic = true;
            float width = 1f / slice.projection.m00;
            parameters.shadowDistance = Mathf.Sqrt(slice.farPlane * slice.farPlane + 2 * width * width) + 1;
            parameters.cullingOptions &= ~(CullingOptions.OcclusionCull | CullingOptions.Stereo);
            parameters.cullingOptions |= CullingOptions.ShadowCasters;
            parameters.cullingPlaneCount = 6;
            for (int i = 0; i < 6; i++) parameters.SetCullingPlane(i, slice.planes[i]);
            for (int i = 0; i < 32; i++) parameters.SetLayerCullingDistance(i, 0);
            // Keep the viewing camera's LOD decision: a long upstream search must not lower
            // character mesh detail merely because the virtual light camera is far away.
            CullingResults results = context.Cull(ref parameters);

            int lightIndex = -1;
            for (int i = 0; i < results.visibleLights.Length; i++)
                if (results.visibleLights[i].light == frame.light) { lightIndex = i; break; }
            if (lightIndex < 0)
            { HoCharacterShadowRendererFeature.LastCullStatus += $"|noLight(lights={results.visibleLights.Length})"; return false; }
            if (!results.GetShadowCasterBounds(lightIndex, out _))
            {
                // A completed, empty cull is valid visibility=1, not a failed query.
                // Do not construct a native shadow renderer list without any casters.
                slice.valid = true;
                HoCharacterShadowRendererFeature.LastCullStatus += $"|emptyOri={slice.origin.x:F1},{slice.origin.y:F1},{slice.origin.z:F1},far={slice.farPlane:F2}";
                return false;
            }
            var split = new ShadowSplitData { cullingPlaneCount = 6, shadowCascadeBlendCullingFactor = 1 };
            for (int i = 0; i < 6; i++) split.SetCullingPlane(i, slice.planes[i]);
            // Enclose the entire light volume, including off-screen upstream casters.
            float halfDepth = (slice.farPlane - slice.nearPlane) * 0.5f;
            float halfWidth = 1f / slice.projection.m00;
            Vector3 sphereCenter = slice.origin + frame.light.transform.forward * (slice.nearPlane + halfDepth);
            split.cullingSphere = new Vector4(sphereCenter.x, sphereCenter.y, sphereCenter.z,
                Mathf.Sqrt(2 * halfWidth * halfWidth + halfDepth * halfDepth));
            var splits = new NativeArray<ShadowSplitData>(1, Allocator.Temp);
            var lights = new NativeArray<LightShadowCasterCullingInfo>(results.visibleLights.Length, Allocator.Temp);
            splits[0] = split;
            lights[lightIndex] = new LightShadowCasterCullingInfo
            { splitRange = new RangeInt(0, 1), projectionType = BatchCullingProjectionType.Orthographic };
            context.CullShadowCasters(results, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = lights });
            drawing = new ShadowDrawingSettings(results, lightIndex)
            {
                splitIndex = 0,
                useRenderingLayerMaskTest = UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.useRenderingLayers
            };
            slice.valid = true;
            HoCharacterShadowRendererFeature.LastCullStatus +=
                $"|ok(lights={results.visibleLights.Length},ori={slice.origin.x:F1},{slice.origin.y:F1},{slice.origin.z:F1},near={slice.nearPlane:F2},far={slice.farPlane:F2})"
                + $"|p(origin={parameters.origin.x:F1},{parameters.origin.y:F1},{parameters.origin.z:F1}"
                + $",shadowDist={parameters.shadowDistance:F1}"
                + $",isOrtho={parameters.isOrthographic}"
                + $",opts={(int)parameters.cullingOptions}"
                + $",lodCam={parameters.lodParameters.cameraPosition.x:F1},{parameters.lodParameters.cameraPosition.y:F1},{parameters.lodParameters.cameraPosition.z:F1}"
                + $",cm={parameters.cullingMatrix.m22:F4},{parameters.cullingMatrix.m23:F2})";
            return true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer data)
        {
            if (frame == null) return;
            var camera = data.Get<UniversalCameraData>().camera;
            var cullContext = data.Get<CullContextData>();
            HoCharacterShadowRendererFeature.LastCullStatus = $"slices={frame.slices.Count}";
            // The atlas is consumed by opaque materials through a global binding, which
            // RenderGraph cannot see. A transient (graph-owned) texture is therefore free to be
            // aliased away right after this pass and the materials then sample another pass's
            // memory - the symptom is shadows that come and go with camera state. Keep the atlas
            // in a feature-owned RTHandle and import it instead, so its lifetime covers the frame.
            RenderingUtils.ReAllocateIfNeeded(ref persistentAtlas, Descriptor(frame.atlasSize),
                FilterMode.Point, TextureWrapMode.Clamp, name: "_HoCSAtlas");
            var atlas = graph.ImportTexture(persistentAtlas);
            using (var builder = graph.AddRasterRenderPass<PassData>("Ho-CS Shadow Atlas", out var passData, Sampler))
            {
                passData.frame = frame;
                passData.lists = new RendererListHandle[frame.slices.Count];
                int listCount = 0;
                for (int i = 0; i < frame.slices.Count; i++)
                {
                    if (!Cull(cullContext, camera, frame, frame.slices[i], out var drawing)) continue;
                    passData.lists[i] = graph.CreateShadowRendererList(ref drawing);

                    builder.UseRendererList(passData.lists[i]);
                    listCount++;
                }
                HoCharacterShadowRendererFeature.LastCullStatus += $"|lists={listCount}/{frame.slices.Count}";
                builder.SetRenderAttachmentDepth(atlas, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(atlas, AtlasId);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    var cmd = ctx.cmd;
                    cmd.SetGlobalFloat("_HoCSActive", 0);

                    // Unity remaps the API-independent far clear (1) for reversed-Z devices.
                    cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1, 0);
                    for (int i = 0; i < d.frame.slices.Count; i++)
                    {
                        if (!d.lists[i].IsValid()) continue;
                        SetupSlice(cmd, d.frame, d.frame.slices[i]);
                        cmd.DrawRendererList(d.lists[i]);
                    }
                    RestoreAndPublish(cmd, d.frame);
                });
            }
            data.GetOrCreate<HoCharacterShadowResources>().atlas = atlas;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (frame == null) return;
            RenderingUtils.ReAllocateIfNeeded(ref compatibilityAtlas, Descriptor(frame.atlasSize),
                FilterMode.Point, TextureWrapMode.Clamp, name: "_HoCSAtlas");
            ConfigureTarget(compatibilityAtlas);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (frame == null || compatibilityAtlas == null) return;
            var cullContext = new CullContextData();
            cullContext.SetRenderContext(context);
            var lists = new RendererList[frame.slices.Count];
            for (int i = 0; i < frame.slices.Count; i++)
                if (Cull(cullContext, renderingData.cameraData.camera, frame, frame.slices[i], out var drawing))
                    lists[i] = context.CreateShadowRendererList(ref drawing);
            CommandBuffer buffer = CommandBufferPool.Get("Ho-CS Shadow Atlas");
            var cmd = CommandBufferHelpers.GetRasterCommandBuffer(buffer);
            buffer.SetRenderTarget(compatibilityAtlas);
            cmd.SetGlobalFloat("_HoCSActive", 0);
            cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1, 0);
            for (int i = 0; i < frame.slices.Count; i++)
            {
                if (!lists[i].isValid) continue;
                SetupSlice(cmd, frame, frame.slices[i]);
                cmd.DrawRendererList(lists[i]);
            }
            RestoreAndPublish(cmd, frame);
            buffer.SetGlobalTexture(AtlasId, compatibilityAtlas);
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }

        private static void SetupSlice(RasterCommandBuffer cmd, HoCharacterShadowFrame f, HoCharacterShadowSlice slice)
        {
            cmd.SetViewport(slice.viewport);
            cmd.SetViewProjectionMatrices(slice.view, slice.projection);
            cmd.SetGlobalVector("_WorldSpaceCameraPos", f.cameraPosition);
            Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * slice.view;
            cmd.SetGlobalMatrix("unity_WorldToCamera", worldToCamera);
            cmd.SetGlobalMatrix("unity_CameraToWorld", worldToCamera.inverse);
            cmd.SetGlobalVector("_ShadowBias", slice.bias);
            cmd.SetGlobalVector("_LightDirection", -f.light.transform.forward);
            cmd.SetGlobalVector("_LightPosition", f.light.transform.position);
            cmd.SetKeyword(Punctual, false);
            cmd.SetGlobalDepthBias(1, 2.5f);
        }

        private static void RestoreAndPublish(RasterCommandBuffer cmd, HoCharacterShadowFrame f)
        {
            cmd.SetGlobalDepthBias(0, 0);
            cmd.DisableScissorRect();
            cmd.SetViewProjectionMatrices(f.cameraView, f.cameraProjection);
            Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * f.cameraView;
            cmd.SetGlobalMatrix("unity_WorldToCamera", worldToCamera);
            cmd.SetGlobalMatrix("unity_CameraToWorld", worldToCamera.inverse);
            cmd.SetGlobalVector("_WorldSpaceCameraPos", f.cameraPosition);
            cmd.SetGlobalVector("_ShadowBias", Vector4.zero);
            for (int i = 0; i < f.slices.Count; i++) f.parameters[i].z = f.slices[i].valid ? 1 : 0;
            cmd.SetGlobalMatrixArray("_HoCSWorldToShadow", f.worldToShadow);
            cmd.SetGlobalMatrixArray("_HoCSWorldToBounds", f.worldToBounds);
            cmd.SetGlobalVectorArray("_HoCSGroupSlices", f.groupSlices);
            cmd.SetGlobalVectorArray("_HoCSPartMasks", f.partMasks);
            cmd.SetGlobalVectorArray("_HoCSTileRects", f.tileRects);
            cmd.SetGlobalVectorArray("_HoCSParameters", f.parameters);
            cmd.SetGlobalVector("_HoCSAtlasSize", new Vector4(1f / f.atlasSize, 1f / f.atlasSize, f.atlasSize, f.atlasSize));
            cmd.SetGlobalFloat("_HoCSFilterRadius", f.filterRadius);
            cmd.SetGlobalInt("_HoCSCount", f.slices.Count);
            cmd.SetGlobalFloat("_HoCSActive", 1);
        }
    }

    internal sealed class HoCharacterShadowDebugPass : ScriptableRenderPass
    {
        private Material material;
        private int mode, character;
        private sealed class PassData { internal Material material; internal TextureHandle atlas; internal int mode, character; }
        internal HoCharacterShadowDebugPass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; }
        internal void Setup(Material value, HoCharacterShadowDebugMode debugMode, int slice)
        { material = value; mode = (int)debugMode; character = slice; }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            TextureHandle atlas = frameData.GetOrCreate<HoCharacterShadowResources>().atlas;
            if (!atlas.IsValid()) return;
            var resources = frameData.Get<UniversalResourceData>();
            using (var builder = graph.AddRasterRenderPass<PassData>("Ho-CS Atlas Debug", out var data))
            {
                data.atlas = atlas; data.material = material; data.mode = mode; data.character = character;
                builder.UseTexture(atlas, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    d.material.SetInt("_HoCSDebugMode", d.mode);
                    d.material.SetInt("_HoCSDebugCharacter", d.character);
                    Blitter.BlitTexture(ctx.cmd, d.atlas, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var atlas = Shader.GetGlobalTexture("_HoCSAtlas");
            if (atlas == null || Shader.GetGlobalFloat("_HoCSActive") < 0.5f) return;
            var cmd = CommandBufferPool.Get("Ho-CS Atlas Debug");
            CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle);
            material.SetInt("_HoCSDebugMode", mode);
            material.SetInt("_HoCSDebugCharacter", character);
            cmd.SetGlobalTexture("_BlitTexture", atlas);
            cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
