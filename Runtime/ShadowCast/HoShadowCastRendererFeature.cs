#pragma warning disable CS0618, CS0672

using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    [DisallowMultipleRendererFeature("lilToon-HoShadowCast")]
    public sealed class HoShadowCastRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private HoShadowCastSettings settings = new HoShadowCastSettings();

        private readonly HoShadowCastRenderTargets renderTargets = new HoShadowCastRenderTargets();
        private HoShadowCastPass pass;
        private HoShadowCastDebugPass debugPass;
        private Material debugMaterial;
        private Shader debugShader;
        private bool registeredCameraReset;
        private bool warnedMissingDebugShader;

        public HoShadowCastSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            pass = new HoShadowCastPass();
            debugPass = new HoShadowCastDebugPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            EnsureMaterials();
            pass?.Setup(settings, renderTargets);
            debugPass?.Setup(renderTargets, renderer.cameraColorTargetHandle, debugMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            if (pass == null)
            {
                return;
            }

            EnsureMaterials();
            pass.SetupRenderGraph(settings);
            renderer.EnqueuePass(pass);

            if (debugPass != null && ShouldDebug())
            {
                debugPass.SetupRenderGraph(debugMaterial);
                renderer.EnqueuePass(debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            pass = null;
            debugPass?.Dispose();
            debugPass = null;
            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
            debugShader = null;
        }

        private bool ShouldRender(in RenderingData renderingData)
        {
            if (settings == null || !settings.enabled || HoShadowCastController.ActiveController == null)
            {
                return false;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            return cameraType == CameraType.Game || cameraType == CameraType.SceneView;
        }

        private static bool ShouldDebug()
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            return controller != null && controller.debugMode == HoShadowCastDebugMode.Atlas;
        }

        private void EnsureMaterials()
        {
            if (debugMaterial != null)
            {
                return;
            }

            if (debugShader == null)
            {
                debugShader = Shader.Find(HoShadowCastShaderConstants.DebugShaderName);
            }

            if (debugShader == null)
            {
                if (!warnedMissingDebugShader)
                {
                    Debug.LogWarning("[lilToon] HoShadowCast debug shader not found: " + HoShadowCastShaderConstants.DebugShaderName);
                    warnedMissingDebugShader = true;
                }

                return;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(debugShader);
        }

        private void RegisterCameraReset()
        {
            if (registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += ResetShadowCastState;
            registeredCameraReset = true;
        }

        private void UnregisterCameraReset()
        {
            if (!registeredCameraReset)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering -= ResetShadowCastState;
            registeredCameraReset = false;
        }

        private static void ResetShadowCastState(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, 0);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, 0);
        }
    }

    [System.Serializable]
    public sealed class HoShadowCastSettings
    {
        [InspectorName("启用")]
        public bool enabled = true;

        [InspectorName("渲染时机")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPrePasses;
    }

    internal sealed class HoShadowCastPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoShadowCast");
        private static readonly Vector4[] WorldToShadowRow0 = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly Vector4[] WorldToShadowRow1 = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly Vector4[] WorldToShadowRow2 = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly Vector4[] WorldToShadowRow3 = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly Vector4[] LightData0 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightData1 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightData2 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightAttenuation = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightColor = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] SliceData = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly ShaderTagId ShadowCasterShaderTagId = new ShaderTagId("ShadowCaster");
        private static int lastDebugLogFrame = -1000;
        private readonly HoShadowCastFrame frame = new HoShadowCastFrame();
        private HoShadowCastSettings settings;
        private HoShadowCastRenderTargets renderTargets;

        private sealed class PassData
        {
            public TextureHandle atlasTexture;
            public HoShadowCastFrame frame;
            public RendererListHandle[] rendererLists;
        }

        public HoShadowCastPass()
        {
            profilingSampler = ProfilingSampler;
        }

        public void Setup(HoShadowCastSettings settings, HoShadowCastRenderTargets renderTargets)
        {
            this.settings = settings;
            this.renderTargets = renderTargets;
            ConfigurePass();
        }

        public void SetupRenderGraph(HoShadowCastSettings settings)
        {
            this.settings = settings;
            ConfigurePass();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            if (settings == null || renderTargets == null || controller == null)
            {
                return;
            }

            renderTargets.ReAllocateIfNeeded(CreateAtlasDescriptor(controller));
            ConfigureTarget(renderTargets.AtlasTexture);
            ConfigureClear(ClearFlag.Depth, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            if (settings == null || renderTargets == null || controller == null || renderTargets.AtlasTexture == null)
            {
                return;
            }

            LightData lightData = renderingData.lightData;
            ShadowData shadowData = renderingData.shadowData;
            bool hasFrame = BuildFrameData(
                    controller,
                    ref renderingData.cullResults,
                    lightData,
                    ref shadowData,
                    lightData.mainLightIndex,
                    renderingData.cameraData.worldSpaceCameraPos,
                    renderingData.cameraData.GetViewMatrix(),
                    renderingData.cameraData.GetProjectionMatrix(),
                    frame);
            MaybeLogDebugFrame(controller, frame, "Compatibility", hasFrame);
            if (!hasFrame)
            {
                SetGlobalEmpty();
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetRenderTarget(renderTargets.AtlasTexture.nameID);
                cmd.ClearRenderTarget(true, false, Color.clear);
                ApplyGlobalData(cmd, frame, renderTargets.AtlasTexture.nameID);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                for (int i = 0; i < frame.sliceCount; i++)
                {
                    ShadowSliceInfo slice = frame.slices[i];
                    DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(renderingData.cameraData.camera);
                    FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(controller);

                    SetShadowCasterGlobals(cmd, frame.cameraPosition, slice);
                    RenderShadowSlice(cmd, ref context, ref renderingData.cullResults, ref slice.shadowSliceData, ref drawingSettings, ref filteringSettings, slice.projectionMatrix, slice.viewMatrix);
                }

                cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, false);
                RestoreCameraGlobals(cmd, frame);
                ApplyGlobalData(cmd, frame, renderTargets.AtlasTexture.nameID);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            HoShadowCastController controller = HoShadowCastController.ActiveController;
            if (settings == null || controller == null)
            {
                return;
            }

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalShadowData shadowData = frameData.Get<UniversalShadowData>();
            HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();

            HoShadowCastFrame renderGraphFrame = new HoShadowCastFrame();
            bool hasFrame = BuildFrameData(
                    controller,
                    ref renderingData.cullResults,
                    lightData,
                    shadowData,
                    lightData.mainLightIndex,
                    cameraData.worldSpaceCameraPos,
                    cameraData.GetViewMatrix(),
                    cameraData.GetProjectionMatrix(),
                    renderGraphFrame);
            MaybeLogDebugFrame(controller, renderGraphFrame, "RenderGraph", hasFrame);
            if (!hasFrame)
            {
                return;
            }

            TextureHandle atlasTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                CreateAtlasDescriptor(controller),
                HoShadowCastShaderConstants.AtlasTextureName,
                true,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoShadowCast ShadowMap", out PassData passData, ProfilingSampler))
            {
                passData.atlasTexture = atlasTexture;
                passData.frame = renderGraphFrame;
                passData.rendererLists = new RendererListHandle[renderGraphFrame.sliceCount];

                for (int i = 0; i < renderGraphFrame.sliceCount; i++)
                {
                    DrawingSettings drawingSettings = CreateShadowCasterDrawingSettings(cameraData.camera);
                    FilteringSettings filteringSettings = CreateShadowCasterFilteringSettings(controller);
                    RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                    passData.rendererLists[i] = renderGraph.CreateRendererList(rendererListParams);
                    builder.UseRendererList(passData.rendererLists[i]);
                }

                builder.SetRenderAttachmentDepth(atlasTexture, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(atlasTexture, HoShadowCastShaderConstants.AtlasTextureId);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RasterCommandBuffer cmd = context.cmd;
                    HoShadowCastFrame frame = data.frame;
                    cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1.0f, 0);
                    ApplyGlobalData(cmd, frame);

                    for (int i = 0; i < frame.sliceCount; i++)
                    {
                        ShadowSliceInfo slice = frame.slices[i];
                        SetShadowCasterGlobals(cmd, frame.cameraPosition, slice);
                        RenderShadowSlice(cmd, ref slice.shadowSliceData, data.rendererLists[i], slice.projectionMatrix, slice.viewMatrix);
                    }

                    cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, false);
                    RestoreCameraGlobals(cmd, frame);
                    ApplyGlobalData(cmd, frame);
                });

                shadowCastResources.atlasTexture = atlasTexture;
            }
        }

        private void ConfigurePass()
        {
            RenderPassEvent passEvent = settings != null ? settings.passEvent : RenderPassEvent.BeforeRenderingPrePasses;
            renderPassEvent = passEvent < RenderPassEvent.BeforeRenderingPrePasses ? RenderPassEvent.BeforeRenderingPrePasses : passEvent;
        }

        private static RenderTextureDescriptor CreateAtlasDescriptor(HoShadowCastController controller)
        {
            int size = Mathf.Max(1, controller != null ? controller.atlasSize : 1);
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(size, size, GraphicsFormat.None, GraphicsFormatUtility.GetDepthStencilFormat(32, 0));
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 32;
            descriptor.shadowSamplingMode = RenderingUtils.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap)
                ? ShadowSamplingMode.CompareDepths
                : ShadowSamplingMode.None;
            return descriptor;
        }

        private static bool BuildFrameData(
            HoShadowCastController controller,
            ref CullingResults cullResults,
            LightData lightData,
            ref ShadowData shadowData,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target)
        {
            return BuildFrameData(
                controller,
                ref cullResults,
                lightData.visibleLights,
                null,
                ref shadowData,
                true,
                mainLightIndex,
                cameraPosition,
                cameraViewMatrix,
                cameraProjectionMatrix,
                target);
        }

        private static bool BuildFrameData(
            HoShadowCastController controller,
            ref CullingResults cullResults,
            UniversalLightData lightData,
            UniversalShadowData shadowData,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target)
        {
            ShadowData unusedCompatibilityShadowData = default;
            return BuildFrameData(
                controller,
                ref cullResults,
                lightData.visibleLights,
                shadowData,
                ref unusedCompatibilityShadowData,
                false,
                mainLightIndex,
                cameraPosition,
                cameraViewMatrix,
                cameraProjectionMatrix,
                target);
        }

        private static bool BuildFrameData(
            HoShadowCastController controller,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            UniversalShadowData universalShadowData,
            ref ShadowData compatibilityShadowData,
            bool useCompatibilityShadowData,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target)
        {
            target.Clear();
            target.atlasSize = Mathf.Max(1, controller.atlasSize);
            target.cameraPosition = cameraPosition;
            target.cameraViewMatrix = cameraViewMatrix;
            target.cameraProjectionMatrix = cameraProjectionMatrix;

            int requestedSliceCount = CountRequestedSlices(controller, visibleLights, mainLightIndex);
            int maxSliceResolution = GetMaxResolutionForSliceCount(target.atlasSize, requestedSliceCount);
            HoShadowCastAtlasPacker packer = new HoShadowCastAtlasPacker(target.atlasSize);
            AddLightArray(controller.directionalLights, LightType.Directional, controller, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target);
            AddLightArray(controller.spotLights, LightType.Spot, controller, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target);
            AddLightArray(controller.pointLights, LightType.Point, controller, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target);

            target.FillUnused();
            return target.lightCount > 0 && target.sliceCount > 0;
        }

        private static void AddLightArray(
            Light[] lights,
            LightType requiredType,
            HoShadowCastController controller,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            int mainLightIndex,
            int maxSliceResolution,
            ref HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                AddLight(lights[i], requiredType, controller, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target);
            }
        }

        private static void AddLight(
            Light light,
            LightType requiredType,
            HoShadowCastController controller,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            int mainLightIndex,
            int maxSliceResolution,
            ref HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target)
        {
            if (light == null || light.type != requiredType || !light.isActiveAndEnabled || target.Contains(light))
            {
                return;
            }

            if (target.lightCount >= HoShadowCastShaderConstants.MaxLights)
            {
                return;
            }

            int visibleLightIndex = FindVisibleLightIndex(visibleLights, light, requiredType);
            if (visibleLightIndex >= 0 && visibleLightIndex == mainLightIndex)
            {
                return;
            }

            int firstSlice = target.sliceCount;
            int requestedSlices = requiredType == LightType.Point ? 6 : 1;
            if (firstSlice + requestedSlices > HoShadowCastShaderConstants.MaxShadowSlices)
            {
                return;
            }

            int resolution = GetResolution(controller, requiredType, maxSliceResolution);
            int writtenSlices = 0;
            bool completed = true;
            for (int face = 0; face < requestedSlices; face++)
            {
                if (!packer.TryAllocate(resolution, out int offsetX, out int offsetY))
                {
                    completed = false;
                    break;
                }

                if (!TryBuildSlice(
                        light,
                        ref cullResults,
                        visibleLightIndex,
                        requiredType,
                        face,
                        controller,
                        target.atlasSize,
                        resolution,
                        offsetX,
                        offsetY,
                        out ShadowSliceInfo slice))
                {
                    completed = false;
                    break;
                }

                target.slices[target.sliceCount++] = slice;
                writtenSlices++;
            }

            if (!completed || writtenSlices != requestedSlices)
            {
                target.sliceCount = firstSlice;
                return;
            }

            int lightIndex = target.lightCount++;
            target.sourceLights[lightIndex] = light;
            Vector3 position = light.transform.position;
            Vector3 direction = -light.transform.forward;
            Color finalColor = light.color * light.intensity;
            float lightShadowStrength = light.shadows == LightShadows.None ? 1.0f : light.shadowStrength;
            float controllerStrength = requiredType == LightType.Directional ? controller.shadowStrength : controller.punctualShadowStrength;
            target.lightData0[lightIndex] = new Vector4(GetLightTypeId(requiredType), firstSlice, writtenSlices, Mathf.Clamp01(controllerStrength * lightShadowStrength));
            target.lightData1[lightIndex] = new Vector4(position.x, position.y, position.z, light.range);
            target.lightData2[lightIndex] = new Vector4(direction.x, direction.y, direction.z, Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad));
            target.lightAttenuation[lightIndex] = ComputeLightAttenuation(light, requiredType);
            target.lightColor[lightIndex] = new Vector4(finalColor.r, finalColor.g, finalColor.b, 1.0f);
        }

        private static bool TryBuildSlice(
            Light light,
            ref CullingResults cullResults,
            int visibleLightIndex,
            LightType lightType,
            int face,
            HoShadowCastController controller,
            int atlasSize,
            int resolution,
            int offsetX,
            int offsetY,
            out ShadowSliceInfo slice)
        {
            slice = new ShadowSliceInfo
            {
                visibleLightIndex = visibleLightIndex,
                lightType = lightType,
                faceIndex = face
            };

            Matrix4x4 shadowMatrix;
            Matrix4x4 viewMatrix;
            Matrix4x4 projectionMatrix;
            ShadowSplitData splitData = default;

            if (!TryBuildLightMatrices(light, ref cullResults, visibleLightIndex, lightType, face, controller, out viewMatrix, out projectionMatrix, out splitData))
            {
                return false;
            }

            shadowMatrix = GetShadowTransform(projectionMatrix, viewMatrix);
            ShadowSliceData shadowSliceData = new ShadowSliceData
            {
                viewMatrix = viewMatrix,
                projectionMatrix = projectionMatrix,
                shadowTransform = shadowMatrix,
                splitData = splitData,
                offsetX = offsetX,
                offsetY = offsetY,
                resolution = resolution
            };
            ShadowUtils.ApplySliceTransform(ref shadowSliceData, atlasSize, atlasSize);

            slice.shadowSliceData = shadowSliceData;
            slice.viewMatrix = viewMatrix;
            slice.projectionMatrix = projectionMatrix;
            slice.splitData = splitData;
            slice.shadowBias = ComputeShadowBias(light, lightType, projectionMatrix, resolution);
            slice.lightDirection = -light.transform.forward;
            slice.lightPosition = light.transform.position;
            slice.worldToShadow = shadowMatrix;
            slice.sliceData = new Vector4((float)offsetX / atlasSize, (float)offsetY / atlasSize, (float)resolution / atlasSize, face);
            return true;
        }

        private static bool TryBuildLightMatrices(
            Light light,
            ref CullingResults cullResults,
            int visibleLightIndex,
            LightType lightType,
            int face,
            HoShadowCastController controller,
            out Matrix4x4 viewMatrix,
            out Matrix4x4 projectionMatrix,
            out ShadowSplitData splitData)
        {
            viewMatrix = Matrix4x4.identity;
            projectionMatrix = Matrix4x4.identity;
            splitData = default;
            if (light == null)
            {
                return false;
            }

            Transform lightTransform = light.transform;
            float nearPlane = Mathf.Max(0.001f, light.shadowNearPlane);
            if (lightType == LightType.Directional)
            {
                float size = Mathf.Max(0.01f, controller.directionalShadowSize);
                float depth = Mathf.Max(nearPlane + 0.01f, controller.directionalShadowDepth);
                Vector3 lightForward = lightTransform.forward;
                Vector3 lightPosition = controller.transform.position - lightForward * (depth * 0.5f);
                viewMatrix = CreateViewMatrix(lightPosition, lightForward, lightTransform.up);
                projectionMatrix = Matrix4x4.Ortho(-size, size, -size, size, nearPlane, depth);
                return true;
            }

            if (lightType == LightType.Spot)
            {
                if (visibleLightIndex >= 0 && cullResults.ComputeSpotShadowMatricesAndCullingPrimitives(visibleLightIndex, out viewMatrix, out projectionMatrix, out splitData))
                {
                    return true;
                }

                BuildManualSpotMatrix(lightTransform, light, nearPlane, out viewMatrix, out projectionMatrix);
                return true;
            }

            if (lightType == LightType.Point)
            {
                if (visibleLightIndex >= 0 && cullResults.ComputePointShadowMatricesAndCullingPrimitives(visibleLightIndex, (CubemapFace)face, 4.0f, out viewMatrix, out projectionMatrix, out splitData))
                {
                    // Match URP's point-light ShadowCaster convention.
                    viewMatrix.m10 = -viewMatrix.m10;
                    viewMatrix.m11 = -viewMatrix.m11;
                    viewMatrix.m12 = -viewMatrix.m12;
                    viewMatrix.m13 = -viewMatrix.m13;
                    return true;
                }

                BuildManualPointMatrix(lightTransform, light, face, nearPlane, out viewMatrix, out projectionMatrix);
                return true;
            }

            return false;
        }

        private static void BuildManualSpotMatrix(Transform lightTransform, Light light, float nearPlane, out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix)
        {
            float range = Mathf.Max(nearPlane + 0.01f, light.range);
            float fov = Mathf.Clamp(light.spotAngle, 0.1f, 179.0f);
            viewMatrix = CreateViewMatrix(lightTransform.position, lightTransform.forward, lightTransform.up);
            projectionMatrix = Matrix4x4.Perspective(fov, 1.0f, nearPlane, range);
        }

        private static void BuildManualPointMatrix(Transform lightTransform, Light light, int face, float nearPlane, out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix)
        {
            float range = Mathf.Max(nearPlane + 0.01f, light.range);
            GetPointLightFaceVectors(face, out Vector3 direction, out Vector3 up);
            viewMatrix = CreateViewMatrix(lightTransform.position, direction, up);
            viewMatrix.m10 = -viewMatrix.m10;
            viewMatrix.m11 = -viewMatrix.m11;
            viewMatrix.m12 = -viewMatrix.m12;
            viewMatrix.m13 = -viewMatrix.m13;
            projectionMatrix = Matrix4x4.Perspective(94.0f, 1.0f, nearPlane, range);
        }

        private static Matrix4x4 CreateViewMatrix(Vector3 position, Vector3 forward, Vector3 up)
        {
            Quaternion rotation = Quaternion.LookRotation(-forward, up);
            return Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        }

        private static void GetPointLightFaceVectors(int face, out Vector3 direction, out Vector3 up)
        {
            switch ((CubemapFace)face)
            {
                case CubemapFace.PositiveX:
                    direction = Vector3.right;
                    up = Vector3.down;
                    break;
                case CubemapFace.NegativeX:
                    direction = Vector3.left;
                    up = Vector3.down;
                    break;
                case CubemapFace.PositiveY:
                    direction = Vector3.up;
                    up = Vector3.forward;
                    break;
                case CubemapFace.NegativeY:
                    direction = Vector3.down;
                    up = Vector3.back;
                    break;
                case CubemapFace.PositiveZ:
                    direction = Vector3.forward;
                    up = Vector3.down;
                    break;
                case CubemapFace.NegativeZ:
                    direction = Vector3.back;
                    up = Vector3.down;
                    break;
                default:
                    direction = Vector3.forward;
                    up = Vector3.down;
                    break;
            }
        }

        private static void MaybeLogDebugFrame(HoShadowCastController controller, HoShadowCastFrame frame, string path, bool hasFrame)
        {
            if (controller == null || controller.debugMode != HoShadowCastDebugMode.Atlas)
            {
                return;
            }

            int currentFrame = Time.frameCount;
            if (currentFrame < lastDebugLogFrame + 60)
            {
                return;
            }

            lastDebugLogFrame = currentFrame;
            StringBuilder builder = new StringBuilder(512);
            builder.Append("[lilToon] HoShadowCast ");
            builder.Append(path);
            builder.Append(" debug: hasFrame=");
            builder.Append(hasFrame);
            builder.Append(", lights=");
            builder.Append(frame.lightCount);
            builder.Append(", slices=");
            builder.Append(frame.sliceCount);
            builder.Append(", atlas=");
            builder.Append(frame.atlasSize);
            builder.Append(", casterMask=0x");
            builder.Append(controller.casterLayerMask.value.ToString("X8"));
            builder.Append(", strength=");
            builder.Append(controller.shadowStrength.ToString("0.##"));
            builder.Append("/");
            builder.Append(controller.punctualShadowStrength.ToString("0.##"));
            builder.Append(", assigned D/S/P=");
            builder.Append(CountAssigned(controller.directionalLights));
            builder.Append('/');
            builder.Append(CountAssigned(controller.spotLights));
            builder.Append('/');
            builder.Append(CountAssigned(controller.pointLights));

            if (frame.lightCount > 0)
            {
                int debugLightCount = Mathf.Min(frame.lightCount, 4);
                builder.Append(", lightSlices=[");
                for (int i = 0; i < debugLightCount; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("; ");
                    }

                    builder.Append(frame.sourceLights[i] != null ? frame.sourceLights[i].name : "<null>");
                    builder.Append(":");
                    builder.Append(frame.lightData0[i].x.ToString("0"));
                    builder.Append("@");
                    builder.Append(frame.lightData0[i].y.ToString("0"));
                    builder.Append("+");
                    builder.Append(frame.lightData0[i].z.ToString("0"));
                    builder.Append("*");
                    builder.Append(frame.lightData0[i].w.ToString("0.##"));
                }
                builder.Append("]");
            }

            if (frame.sliceCount > 0)
            {
                ShadowSliceInfo slice = frame.slices[0];
                builder.Append(", firstSlice type=");
                builder.Append(slice.lightType);
                builder.Append(" face=");
                builder.Append(slice.faceIndex);
                builder.Append(" offset=");
                builder.Append(slice.shadowSliceData.offsetX);
                builder.Append(',');
                builder.Append(slice.shadowSliceData.offsetY);
                builder.Append(" res=");
                builder.Append(slice.shadowSliceData.resolution);
            }

            Debug.Log(builder.ToString(), controller);
        }

        private static int CountAssigned(Light[] lights)
        {
            if (lights == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static Matrix4x4 GetShadowTransform(Matrix4x4 projectionMatrix, Matrix4x4 viewMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }

            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;
            textureScaleAndBias.m23 = 0.5f;
            return textureScaleAndBias * projectionMatrix * viewMatrix;
        }

        private static Vector4 ComputeShadowBias(Light light, LightType lightType, Matrix4x4 lightProjectionMatrix, int resolution)
        {
            if (light == null)
            {
                return Vector4.zero;
            }

            float frustumSize;
            if (lightType == LightType.Directional)
            {
                frustumSize = Mathf.Abs(2.0f / lightProjectionMatrix.m00);
            }
            else if (lightType == LightType.Spot)
            {
                frustumSize = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad) * light.range;
            }
            else if (lightType == LightType.Point)
            {
                frustumSize = Mathf.Tan(94.0f * 0.5f * Mathf.Deg2Rad) * light.range;
            }
            else
            {
                frustumSize = 0.0f;
            }

            float texelSize = resolution > 0 ? frustumSize / resolution : 0.0f;
            float depthBias = -light.shadowBias * texelSize;
            float normalBias = lightType == LightType.Point ? 0.0f : -light.shadowNormalBias * texelSize;
            return new Vector4(depthBias, normalBias, (float)lightType, 0.0f);
        }

        private static int FindVisibleLightIndex(NativeArray<VisibleLight> visibleLights, Light light, LightType requiredType)
        {
            if (!visibleLights.IsCreated)
            {
                return -1;
            }

            for (int i = 0; i < visibleLights.Length; i++)
            {
                VisibleLight visibleLight = visibleLights[i];
                if (visibleLight.light == light && visibleLight.lightType == requiredType)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountRequestedSlices(HoShadowCastController controller, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            if (controller == null)
            {
                return 0;
            }

            int count = 0;
            count += CountRequestedSlices(controller.directionalLights, LightType.Directional, visibleLights, mainLightIndex);
            count += CountRequestedSlices(controller.spotLights, LightType.Spot, visibleLights, mainLightIndex);
            count += CountRequestedSlices(controller.pointLights, LightType.Point, visibleLights, mainLightIndex);
            return count;
        }

        private static int CountRequestedSlices(Light[] lights, LightType requiredType, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            if (lights == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type != requiredType || !light.isActiveAndEnabled)
                {
                    continue;
                }

                int visibleLightIndex = FindVisibleLightIndex(visibleLights, light, requiredType);
                if (visibleLightIndex >= 0 && visibleLightIndex == mainLightIndex)
                {
                    continue;
                }

                count += requiredType == LightType.Point ? 6 : 1;
            }

            return count;
        }

        private static int GetMaxResolutionForSliceCount(int atlasSize, int requestedSliceCount)
        {
            atlasSize = Mathf.Max(1, atlasSize);
            if (requestedSliceCount <= 1)
            {
                return atlasSize;
            }

            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(requestedSliceCount));
            return Mathf.Max(64, atlasSize / Mathf.Max(1, gridSize));
        }

        private static int GetResolution(HoShadowCastController controller, LightType type, int maxSliceResolution)
        {
            int atlasSize = Mathf.Max(1, controller.atlasSize);
            int resolution = type switch
            {
                LightType.Directional => controller.directionalResolution,
                LightType.Spot => controller.spotResolution,
                LightType.Point => controller.pointFaceResolution,
                _ => 64
            };

            return Mathf.Clamp(resolution, 64, Mathf.Min(atlasSize, maxSliceResolution));
        }

        private static float GetLightTypeId(LightType type)
        {
            return type switch
            {
                LightType.Directional => 0.0f,
                LightType.Spot => 1.0f,
                LightType.Point => 2.0f,
                _ => -1.0f
            };
        }

        private static Vector4 ComputeLightAttenuation(Light light, LightType lightType)
        {
            if (light == null || lightType == LightType.Directional)
            {
                return Vector4.zero;
            }

            float range = Mathf.Max(0.0001f, light.range);
            float oneOverRangeSqr = 1.0f / (range * range);
            float spotScale = 0.0f;
            float spotOffset = 0.0f;

            if (lightType == LightType.Spot)
            {
                float spotAngle = Mathf.Max(2.6f, light.spotAngle);
                float innerSpotAngle = Mathf.Clamp(light.innerSpotAngle, 0.0f, spotAngle);
                float cosOuterAngle = Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosInnerAngle = Mathf.Cos(innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                spotScale = 1.0f / smoothAngleRange;
                spotOffset = -cosOuterAngle * spotScale;
            }

            return new Vector4(oneOverRangeSqr, 0.0f, spotScale, spotOffset);
        }

        private static void SetShadowCasterGlobals(CommandBuffer cmd, Vector3 cameraPosition, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, slice.viewMatrix);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.ShadowBiasId, slice.shadowBias);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightDirectionId, new Vector4(slice.lightDirection.x, slice.lightDirection.y, slice.lightDirection.z, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightPositionId, new Vector4(slice.lightPosition.x, slice.lightPosition.y, slice.lightPosition.z, 1.0f));
            cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, slice.lightType != LightType.Directional);
        }

        private static void SetShadowCasterGlobals(RasterCommandBuffer cmd, Vector3 cameraPosition, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, slice.viewMatrix);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.ShadowBiasId, slice.shadowBias);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightDirectionId, new Vector4(slice.lightDirection.x, slice.lightDirection.y, slice.lightDirection.z, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightPositionId, new Vector4(slice.lightPosition.x, slice.lightPosition.y, slice.lightPosition.z, 1.0f));
            cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, slice.lightType != LightType.Directional);
        }

        private static void SetWorldToCameraMatrices(CommandBuffer cmd, Matrix4x4 viewMatrix)
        {
            Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * viewMatrix;
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.WorldToCameraMatrixId, worldToCameraMatrix);
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.CameraToWorldMatrixId, worldToCameraMatrix.inverse);
        }

        private static void SetWorldToCameraMatrices(RasterCommandBuffer cmd, Matrix4x4 viewMatrix)
        {
            Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * viewMatrix;
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.WorldToCameraMatrixId, worldToCameraMatrix);
            cmd.SetGlobalMatrix(HoShadowCastShaderConstants.CameraToWorldMatrixId, worldToCameraMatrix.inverse);
        }

        private static DrawingSettings CreateShadowCasterDrawingSettings(Camera camera)
        {
            SortingSettings sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.None
            };

            return new DrawingSettings(ShadowCasterShaderTagId, sortingSettings)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true
            };
        }

        private static FilteringSettings CreateShadowCasterFilteringSettings(HoShadowCastController controller)
        {
            int layerMask = controller != null ? controller.casterLayerMask.value : -1;
            return new FilteringSettings(RenderQueueRange.all, layerMask);
        }

        private static void RestoreCameraGlobals(CommandBuffer cmd, HoShadowCastFrame frame)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, frame.cameraPosition);
            SetWorldToCameraMatrices(cmd, frame.cameraViewMatrix);
            cmd.SetViewProjectionMatrices(frame.cameraViewMatrix, frame.cameraProjectionMatrix);
        }

        private static void RestoreCameraGlobals(RasterCommandBuffer cmd, HoShadowCastFrame frame)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, frame.cameraPosition);
            SetWorldToCameraMatrices(cmd, frame.cameraViewMatrix);
            cmd.SetViewProjectionMatrices(frame.cameraViewMatrix, frame.cameraProjectionMatrix);
        }

        private static void RenderShadowSlice(
            CommandBuffer cmd,
            ref ScriptableRenderContext context,
            ref CullingResults cullingResults,
            ref ShadowSliceData shadowSliceData,
            ref DrawingSettings drawingSettings,
            ref FilteringSettings filteringSettings,
            Matrix4x4 projectionMatrix,
            Matrix4x4 viewMatrix)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f);
            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);

            cmd.DisableScissorRect();
            cmd.SetGlobalDepthBias(0.0f, 0.0f);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private static void RenderShadowSlice(RasterCommandBuffer cmd, ref ShadowSliceData shadowSliceData, RendererListHandle rendererList, Matrix4x4 projectionMatrix, Matrix4x4 viewMatrix)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f);
            cmd.SetViewport(new Rect(shadowSliceData.offsetX, shadowSliceData.offsetY, shadowSliceData.resolution, shadowSliceData.resolution));
            cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            cmd.DrawRendererList(rendererList);
            cmd.DisableScissorRect();
            cmd.SetGlobalDepthBias(0.0f, 0.0f);
        }

        private static void ApplyGlobalData(CommandBuffer cmd, HoShadowCastFrame frame, RenderTargetIdentifier atlas)
        {
            CopyFrameArrays(frame);
            cmd.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, frame.lightCount > 0 ? 1.0f : 0.0f);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, frame.lightCount);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, frame.sliceCount);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.AtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / frame.atlasSize, 1.0f / frame.atlasSize));
            cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, atlas);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow0Id, WorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow1Id, WorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow2Id, WorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow3Id, WorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightAttenuationId, LightAttenuation);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightColorId, LightColor);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SliceDataId, SliceData);
        }

        private static void ApplyGlobalData(RasterCommandBuffer cmd, HoShadowCastFrame frame)
        {
            CopyFrameArrays(frame);
            cmd.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, frame.lightCount > 0 ? 1.0f : 0.0f);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, frame.lightCount);
            cmd.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, frame.sliceCount);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.AtlasSizeId, new Vector4(frame.atlasSize, frame.atlasSize, 1.0f / frame.atlasSize, 1.0f / frame.atlasSize));
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow0Id, WorldToShadowRow0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow1Id, WorldToShadowRow1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow2Id, WorldToShadowRow2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.WorldToShadowRow3Id, WorldToShadowRow3);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightAttenuationId, LightAttenuation);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightColorId, LightColor);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.SliceDataId, SliceData);
        }

        private static void SetGlobalEmpty()
        {
            Shader.SetGlobalFloat(HoShadowCastShaderConstants.ActiveId, 0.0f);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.LightCountId, 0);
            Shader.SetGlobalInt(HoShadowCastShaderConstants.SliceCountId, 0);
        }

        private static void CopyFrameArrays(HoShadowCastFrame frame)
        {
            for (int i = 0; i < HoShadowCastShaderConstants.MaxShadowSlices; i++)
            {
                WorldToShadowRow0[i] = frame.worldToShadow[i].GetRow(0);
                WorldToShadowRow1[i] = frame.worldToShadow[i].GetRow(1);
                WorldToShadowRow2[i] = frame.worldToShadow[i].GetRow(2);
                WorldToShadowRow3[i] = frame.worldToShadow[i].GetRow(3);
                SliceData[i] = frame.sliceData[i];
            }

            for (int i = 0; i < HoShadowCastShaderConstants.MaxLights; i++)
            {
                LightData0[i] = frame.lightData0[i];
                LightData1[i] = frame.lightData1[i];
                LightData2[i] = frame.lightData2[i];
                LightAttenuation[i] = frame.lightAttenuation[i];
                LightColor[i] = frame.lightColor[i];
            }
        }
    }

    internal sealed class HoShadowCastRenderTargets
    {
        private RTHandle atlasTexture;

        public RTHandle AtlasTexture => atlasTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor descriptor)
        {
            RenderingUtils.ReAllocateIfNeeded(ref atlasTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoShadowCastShaderConstants.AtlasTextureName);
        }

        public void Release()
        {
            atlasTexture?.Release();
            atlasTexture = null;
        }
    }

    internal sealed class HoShadowCastDebugPass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("lilToon-HoShadowCast Debug");

        private HoShadowCastRenderTargets renderTargets;
        private RTHandle cameraColorTarget;
        private RTHandle tempTexture;
        private Material debugMaterial;

        private sealed class PassData
        {
            public TextureHandle source;
            public TextureHandle atlasTexture;
            public Material debugMaterial;
        }

        public void Setup(HoShadowCastRenderTargets renderTargets, RTHandle cameraColorTarget, Material debugMaterial)
        {
            this.renderTargets = renderTargets;
            this.cameraColorTarget = cameraColorTarget;
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void SetupRenderGraph(Material debugMaterial)
        {
            this.debugMaterial = debugMaterial;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void Dispose()
        {
            tempTexture?.Release();
            tempTexture = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref tempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_HoShadowCastDebugSource");
            ConfigureTarget(cameraColorTarget);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (debugMaterial == null || renderTargets == null || renderTargets.AtlasTexture == null || cameraColorTarget == null || tempTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, ProfilingSampler))
            {
                cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, renderTargets.AtlasTexture.nameID);
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, tempTexture, 0, true);
                Blitter.BlitCameraTexture(cmd, tempTexture, cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, debugMaterial, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (debugMaterial == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            HoShadowCastRenderGraphResources shadowCastResources = frameData.GetOrCreate<HoShadowCastRenderGraphResources>();
            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle atlas = shadowCastResources.atlasTexture;
            if (!source.IsValid() || !atlas.IsValid())
            {
                return;
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_HoShadowCastDebugColor";
            destinationDesc.clearBuffer = false;
            destinationDesc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("lilToon-HoShadowCast Debug", out PassData passData, ProfilingSampler))
            {
                passData.source = source;
                passData.atlasTexture = atlas;
                passData.debugMaterial = debugMaterial;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(atlas, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(HoShadowCastShaderConstants.AtlasTextureId, data.atlasTexture);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.debugMaterial, 0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }

    internal sealed class HoShadowCastFrame
    {
        public int atlasSize;
        public int lightCount;
        public int sliceCount;
        public Vector3 cameraPosition;
        public Matrix4x4 cameraViewMatrix;
        public Matrix4x4 cameraProjectionMatrix;
        public readonly Light[] sourceLights = new Light[HoShadowCastShaderConstants.MaxLights];
        public readonly ShadowSliceInfo[] slices = new ShadowSliceInfo[HoShadowCastShaderConstants.MaxShadowSlices];
        public readonly Matrix4x4[] worldToShadow = new Matrix4x4[HoShadowCastShaderConstants.MaxShadowSlices];
        public readonly Vector4[] lightData0 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        public readonly Vector4[] lightData1 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        public readonly Vector4[] lightData2 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        public readonly Vector4[] lightAttenuation = new Vector4[HoShadowCastShaderConstants.MaxLights];
        public readonly Vector4[] lightColor = new Vector4[HoShadowCastShaderConstants.MaxLights];
        public readonly Vector4[] sliceData = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];

        public void Clear()
        {
            atlasSize = 1;
            lightCount = 0;
            sliceCount = 0;
            cameraPosition = Vector3.zero;
            cameraViewMatrix = Matrix4x4.identity;
            cameraProjectionMatrix = Matrix4x4.identity;

            for (int i = 0; i < sourceLights.Length; i++)
            {
                sourceLights[i] = null;
                lightData0[i] = Vector4.zero;
                lightData1[i] = Vector4.zero;
                lightData2[i] = Vector4.zero;
                lightAttenuation[i] = Vector4.zero;
                lightColor[i] = Vector4.zero;
            }

            for (int i = 0; i < slices.Length; i++)
            {
                slices[i] = default;
                worldToShadow[i] = Matrix4x4.identity;
                sliceData[i] = Vector4.zero;
            }
        }

        public bool Contains(Light light)
        {
            for (int i = 0; i < lightCount; i++)
            {
                if (sourceLights[i] == light)
                {
                    return true;
                }
            }

            return false;
        }

        public void FillUnused()
        {
            for (int i = 0; i < sliceCount; i++)
            {
                worldToShadow[i] = slices[i].worldToShadow;
                sliceData[i] = slices[i].sliceData;
            }

            for (int i = sliceCount; i < worldToShadow.Length; i++)
            {
                worldToShadow[i] = Matrix4x4.identity;
                sliceData[i] = Vector4.zero;
            }
        }
    }

    internal struct ShadowSliceInfo
    {
        public int visibleLightIndex;
        public int faceIndex;
        public LightType lightType;
        public Matrix4x4 viewMatrix;
        public Matrix4x4 projectionMatrix;
        public Matrix4x4 worldToShadow;
        public ShadowSplitData splitData;
        public ShadowSliceData shadowSliceData;
        public Vector4 shadowBias;
        public Vector3 lightDirection;
        public Vector3 lightPosition;
        public Vector4 sliceData;
    }

    internal struct HoShadowCastAtlasPacker
    {
        private readonly int atlasSize;
        private int cursorX;
        private int cursorY;
        private int rowHeight;

        public HoShadowCastAtlasPacker(int atlasSize)
        {
            this.atlasSize = Mathf.Max(1, atlasSize);
            cursorX = 0;
            cursorY = 0;
            rowHeight = 0;
        }

        public bool TryAllocate(int size, out int offsetX, out int offsetY)
        {
            size = Mathf.Clamp(size, 1, atlasSize);
            if (cursorX + size > atlasSize)
            {
                cursorX = 0;
                cursorY += rowHeight;
                rowHeight = 0;
            }

            if (cursorY + size > atlasSize)
            {
                offsetX = 0;
                offsetY = 0;
                return false;
            }

            offsetX = cursorX;
            offsetY = cursorY;
            cursorX += size;
            rowHeight = Mathf.Max(rowHeight, size);
            return true;
        }
    }
}
