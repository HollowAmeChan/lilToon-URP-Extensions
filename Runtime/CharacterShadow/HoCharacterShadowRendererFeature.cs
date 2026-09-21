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
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void ResetCamera(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalFloat("_HoCSActive", 0);
            // Hide the CS-only light for this camera's cull; the pass re-enables it while it records.
            pass?.DisableLocalLight();
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            pass?.DisableLocalLight();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            LastCullStatus = "add:";
            if (settings == null || pass == null) { LastCullStatus += "noSettings"; return; }
            // Per-camera overrides live in Ho-CharacterShadow Volume; an un-overridden field keeps the
            // RendererFeature value as its fallback.
            HoCharacterShadowVolume volume = HoCharacterShadowVolume.Resolve();
            bool enabled = volume != null && volume.enable.overrideState ? volume.enable.value : settings.enabled;
            if (!enabled) { LastCullStatus += "disabled"; return; }
            Camera camera = renderingData.cameraData.camera;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) { LastCullStatus += "camType"; return; }
            int main = renderingData.lightData.mainLightIndex;
            Light light = main >= 0 ? renderingData.lightData.visibleLights[main].light : null;
            if (light == null || light.type != LightType.Directional || light.shadows == LightShadows.None
                || !renderingData.shadowData.supportsMainLightShadows || HoCharacterShadow.Active.Count == 0)
            { LastCullStatus += $"light(main={main},null={light == null},dir={(light != null && light.type == LightType.Directional)},shadows={(light != null && light.shadows != LightShadows.None)},supports={renderingData.shadowData.supportsMainLightShadows},active={HoCharacterShadow.Active.Count})"; return; }
            var config = HoCharacterShadowRenderConfig.Resolve(settings, volume);
            var frame = HoCharacterShadowFrame.Build(camera, light, renderingData.cameraData.GetViewMatrix(),
                renderingData.cameraData.GetProjectionMatrix(), config);
            if (frame.slices.Count == 0) { LastCullStatus += "noSlices"; return; }
            pass.Setup(frame);
            renderer.EnqueuePass(pass);

            HoCharacterShadowDebugMode debugMode = volume != null && volume.debugMode.overrideState ? volume.debugMode.value : settings.debugMode;
            if (debugMode == HoCharacterShadowDebugMode.Off) return;
            bool debugInSceneView = volume != null && volume.debugInSceneView.overrideState ? volume.debugInSceneView.value : settings.debugInSceneView;
            bool debugInGameView = volume != null && volume.debugInGameView.overrideState ? volume.debugInGameView.value : settings.debugInGameView;
            if (!((camera.cameraType == CameraType.SceneView && debugInSceneView)
                || (camera.cameraType == CameraType.Game && debugInGameView))) return;
            if (debugMaterial == null)
            {
                Shader shader = settings.debugShader != null ? settings.debugShader : Shader.Find("Hidden/Ho-CharacterShadow/Debug");
                if (shader != null) debugMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
            if (debugMaterial != null)
            {
                int debugCharacter = volume != null && volume.debugCharacter.overrideState ? volume.debugCharacter.value : settings.debugCharacter;
                debugPass.Setup(debugMaterial, debugMode, debugCharacter);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= ResetCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
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
        private Light localLight;
        private sealed class PassData
        {
            internal HoCharacterShadowFrame frame;
            internal RendererListHandle[] lists;
        }

        // 排在 URP 相机阴影阶段之前、任何材质消费之前。CS 用的是**自己的隐藏光**（见 EnsureLocalLight），
        // 所以不会和 URP 的相机级联抢同一个光源的 shadow renderer list；这里保留早于阴影阶段的时机，
        // 与 CS 最初的行为一致。
        internal HoCharacterShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingShadows; }
        internal void Setup(HoCharacterShadowFrame value) { frame = value; }
        internal void Dispose()
        {
            compatibilityAtlas?.Release(); compatibilityAtlas = null;
            persistentAtlas?.Release(); persistentAtlas = null;
            DestroyTemporary(cullingCamera != null ? cullingCamera.gameObject : null);
            cullingCamera = null;
            DestroyTemporary(localLight != null ? localLight.gameObject : null);
            localLight = null;
        }

        /// <summary>
        /// 销毁 temp GameObject（隐藏剔除相机 / 隐藏光）。
        /// 这里三条路都要照顾，谁都不能直接用：
        /// <list type="bullet">
        /// <item><c>CoreUtils.Destroy</c> 在编辑器里走 <c>DestroyImmediate</c>，而本方法会被 <c>Create()</c> 调到，
        /// 那条路可能发生在渲染回调 / Inspector 回调里 → "Destroying GameObjects immediately is not permitted
        /// during ... rendering callbacks or OnValidate"。</item>
        /// <item><c>Object.Destroy</c> 在编辑模式下非法 → "Destroy may not be called from edit mode!"。</item>
        /// <item><c>DestroyImmediate</c> 在上面那种回调里同样非法。</item>
        /// </list>
        /// 所以编辑模式统一推迟到下一个编辑器 tick（那时已经出了回调），播放模式用 <c>Destroy</c> 延迟销毁。
        /// 这两个对象都是 <c>HideFlags.HideAndDontSave</c>，晚一帧销毁没有任何副作用。
        /// </summary>
        private static void DestroyTemporary(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(value);
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (value != null) Object.DestroyImmediate(value);
            };
#else
            Object.Destroy(value);
#endif
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

        // 每个 slice 占光源自己的一个 split 索引（0 起）。
        // 注意：**不能借场景主光**。Unity 的 shadow renderer list 是按“光源一帧一份”提交的
        // （和传进去的 CullingResults 无关），对主光调 CreateShadowRendererList 会让 URP 的相机阴影图
        // 拿到我们的局部盒列表：场景里没有 CS 组件的物体一起丢阴影
        // （ValidateSceneShadows 实测 without CS=0.008 / with CS=0.803），
        // 而把我们的 pass 挪到 URP 阴影阶段之后，我们自己又什么都拿不到（atlas 全 0）。
        // 隐藏光 + split 索引 0..N-1 的组合两件事都成立：URP 阴影图完好，局部图集也有内容。

        // CS 用自己的隐藏方向光做局部剔除/绘制，**不能借场景主光**：
        // Unity 的 shadow renderer list 是“每光源一帧一份”的提交式状态（CullingResults 不参与身份），
        // 在同一帧里对主光调 CreateShadowRendererList 会让 URP 的相机阴影图拿到我们的局部盒列表 ——
        // 实测：CS 开启后除角色外所有物体失去普通投影（ValidateSceneShadows: without CS=0.008 / with CS=0.803），
        // 而把我们的 pass 挪到 URP 阴影阶段之后，我们自己又什么都拿不到（atlas 全 0）。
        // 用的隐藏光：方向/剔除层跟随主光，但**不贡献任何光照**（color 黑 + 极小强度），也不动 RenderSettings.sun，
        // 因此 URP 的主光选择（优先 RenderSettings.sun）与场景明暗都不受影响。
        // 强度必须 > 0：实测 intensity = 0 的灯**不会出现在 visibleLights 里**，我们自己也就找不到它；
        // 取一个远小于任何真实太阳的强度，既保证可见，又保证万一没设 RenderSettings.sun 也争不到主光。
        private void EnsureLocalLight(Light source)
        {
            if (localLight == null)
            {
                var lightObject = new GameObject("Ho-CS Local Light", typeof(Light)) { hideFlags = HideFlags.HideAndDontSave };
                localLight = lightObject.GetComponent<Light>();
                localLight.type = LightType.Directional;
                localLight.shadows = LightShadows.Hard;
                localLight.color = Color.black;
            }

            // 强度必须 > 0：实测 intensity = 0 的灯**不会出现在 visibleLights 里**，我们自己也就找不到它；
            // 取一个远小于任何真实太阳的强度，既保证可见，又保证万一没设 RenderSettings.sun 也争不到主光。
            localLight.intensity = 0.001f;
            localLight.transform.rotation = source.transform.rotation;
            localLight.cullingMask = source.cullingMask;
            localLight.renderingLayerMask = source.renderingLayerMask;
            localLight.shadowStrength = source.shadowStrength;
            localLight.shadowBias = source.shadowBias;
            localLight.shadowNormalBias = source.shadowNormalBias;
            // 只在“本相机剔除完之后、本相机画完之前”这段窗口里开着：beginCameraRendering 早于
            // URP 的 context.Cull（UniversalRenderPipeline.cs:857 在 CameraRenderingScope 之内），
            // 所以相机永远看不到这盏灯；而我们的 Cull() 在 AddRenderPasses→RecordRenderGraph 里，
            // 晚于相机剔除，能看到它。
            localLight.enabled = true;
        }

        internal void DisableLocalLight()
        {
            if (localLight != null) localLight.enabled = false;
        }

        // A separate CullingResults per local projection keeps native shadow split state isolated
        // from URP's main CSM and from other CS characters. No HoURP modifications required.
        private bool Cull(CullContextData context, Camera camera, HoCharacterShadowFrame frame,
            HoCharacterShadowSlice slice, int sliceIndex, int sliceCount, out ShadowDrawingSettings drawing)
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
                if (results.visibleLights[i].light == localLight) { lightIndex = i; break; }
            if (lightIndex < 0)
            {
                HoCharacterShadowRendererFeature.LastCullStatus +=
                    $"|noLocalLight(lights={results.visibleLights.Length},local={(localLight != null)},intensity={(localLight != null ? localLight.intensity : -1)})";
                return false;
            }
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
            var splits = new NativeArray<ShadowSplitData>(Mathf.Max(1, sliceCount), Allocator.Temp);
            var lights = new NativeArray<LightShadowCasterCullingInfo>(results.visibleLights.Length, Allocator.Temp);
            int splitIndex = sliceIndex;
            splits[splitIndex] = split;
            lights[lightIndex] = new LightShadowCasterCullingInfo
            { splitRange = new RangeInt(0, sliceCount), projectionType = BatchCullingProjectionType.Orthographic };
            context.CullShadowCasters(results, new ShadowCastersCullingInfos { splitBuffer = splits, perLightInfos = lights });
            drawing = new ShadowDrawingSettings(results, lightIndex)
            {
                splitIndex = splitIndex,
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
            EnsureLocalLight(frame.light);
            var lightData = data.Get<UniversalLightData>();
            // camLights/add 是给“隐藏光有没有泄漏进相机灯光列表”留的哨兵：CS 的灯只在
            // beginCameraRendering→本 pass 之间开着，正常情况下相机看到的灯光数与 CS 无关。
            HoCharacterShadowRendererFeature.LastCullStatus =
                $"slices={frame.slices.Count}|camLights={lightData.visibleLights.Length},add={lightData.additionalLightsCount}";
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
                    if (!Cull(cullContext, camera, frame, frame.slices[i], i, frame.slices.Count, out var drawing)) continue;
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
            EnsureLocalLight(frame.light);
            var cullContext = new CullContextData();
            cullContext.SetRenderContext(context);
            var lists = new RendererList[frame.slices.Count];
            for (int i = 0; i < frame.slices.Count; i++)
                if (Cull(cullContext, renderingData.cameraData.camera, frame, frame.slices[i], i, frame.slices.Count, out var drawing))
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
            {
                cmd.SetViewProjectionMatrices(slice.view, slice.projection);
                cmd.SetGlobalVector("_WorldSpaceCameraPos", f.cameraPosition);
                Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * slice.view;
                cmd.SetGlobalMatrix("unity_WorldToCamera", worldToCamera);
                cmd.SetGlobalMatrix("unity_CameraToWorld", worldToCamera.inverse);
            }
            {
                cmd.SetGlobalVector("_ShadowBias", slice.bias);
                cmd.SetGlobalVector("_LightDirection", -f.light.transform.forward);
                cmd.SetGlobalVector("_LightPosition", f.light.transform.position);
                cmd.SetKeyword(Punctual, false);
                cmd.SetGlobalDepthBias(1, 2.5f);
            }
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
            // PCSS：(enabled, softness, blocker 搜索半径, 半影半径上限) + (深度偏移, blocker 采样数, filter 采样数, 0)。
            cmd.SetGlobalVector("_HoCSPcssParams", f.pcssParams);
            cmd.SetGlobalVector("_HoCSPcssParams2", f.pcssParams2);
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
