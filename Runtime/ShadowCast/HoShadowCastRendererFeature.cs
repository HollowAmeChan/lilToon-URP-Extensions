#pragma warning disable CS0618, CS0672

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
        private bool registeredCameraReset;

        public HoShadowCastSettings Settings => settings;

        public override void Create()
        {
            RegisterCameraReset();
            pass = new HoShadowCastPass();
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (!ShouldRender(in renderingData))
            {
                return;
            }

            pass?.Setup(settings, renderTargets);
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

            pass.SetupRenderGraph(settings);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            UnregisterCameraReset();
            renderTargets.Release();
            pass = null;
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
        private static readonly Matrix4x4[] WorldToShadowMatrices = new Matrix4x4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly Vector4[] LightData0 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightData1 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightData2 = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] LightColor = new Vector4[HoShadowCastShaderConstants.MaxLights];
        private static readonly Vector4[] SliceData = new Vector4[HoShadowCastShaderConstants.MaxShadowSlices];
        private static readonly UniversalShadowData DirectionalShadowData = new UniversalShadowData
        {
            mainLightShadowCascadesCount = 1,
            mainLightShadowCascadesSplit = new Vector3(1.0f, 0.0f, 0.0f),
            mainLightShadowCascadeBorder = 0.0f
        };

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
            if (!BuildFrameData(
                    controller,
                    ref renderingData.cullResults,
                    lightData,
                    ref shadowData,
                    lightData.mainLightIndex,
                    renderingData.cameraData.worldSpaceCameraPos,
                    renderingData.cameraData.GetViewMatrix(),
                    renderingData.cameraData.GetProjectionMatrix(),
                    frame))
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
                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(renderingData.cullResults, slice.visibleLightIndex)
                    {
                        useRenderingLayerMaskTest = UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.useRenderingLayers
                    };

                    SetShadowCasterGlobals(cmd, frame.cameraPosition, frame.cameraViewMatrix, slice);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    ShadowUtils.RenderShadowSlice(cmd, ref context, ref slice.shadowSliceData, ref shadowDrawingSettings, slice.projectionMatrix, slice.viewMatrix);
                    cmd.SetGlobalDepthBias(0.0f, 0.0f);
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
            if (!BuildFrameData(
                    controller,
                    ref renderingData.cullResults,
                    lightData,
                    shadowData,
                    lightData.mainLightIndex,
                    cameraData.worldSpaceCameraPos,
                    cameraData.GetViewMatrix(),
                    cameraData.GetProjectionMatrix(),
                    renderGraphFrame))
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
                    ShadowSliceInfo slice = renderGraphFrame.slices[i];
                    ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(renderingData.cullResults, slice.visibleLightIndex)
                    {
                        useRenderingLayerMaskTest = UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.useRenderingLayers
                    };
                    passData.rendererLists[i] = renderGraph.CreateShadowRendererList(ref shadowDrawingSettings);
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
                        SetShadowCasterGlobals(cmd, frame.cameraPosition, frame.cameraViewMatrix, slice);
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

            HoShadowCastAtlasPacker packer = new HoShadowCastAtlasPacker(target.atlasSize);
            AddLightArray(controller.directionalLights, LightType.Directional, controller, ref cullResults, visibleLights, universalShadowData, ref compatibilityShadowData, useCompatibilityShadowData, mainLightIndex, packer, target);
            AddLightArray(controller.spotLights, LightType.Spot, controller, ref cullResults, visibleLights, universalShadowData, ref compatibilityShadowData, useCompatibilityShadowData, mainLightIndex, packer, target);
            AddLightArray(controller.pointLights, LightType.Point, controller, ref cullResults, visibleLights, universalShadowData, ref compatibilityShadowData, useCompatibilityShadowData, mainLightIndex, packer, target);

            target.FillUnused();
            return target.lightCount > 0 && target.sliceCount > 0;
        }

        private static void AddLightArray(
            Light[] lights,
            LightType requiredType,
            HoShadowCastController controller,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            UniversalShadowData universalShadowData,
            ref ShadowData compatibilityShadowData,
            bool useCompatibilityShadowData,
            int mainLightIndex,
            HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                AddLight(lights[i], requiredType, controller, ref cullResults, visibleLights, universalShadowData, ref compatibilityShadowData, useCompatibilityShadowData, mainLightIndex, packer, target);
            }
        }

        private static void AddLight(
            Light light,
            LightType requiredType,
            HoShadowCastController controller,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            UniversalShadowData universalShadowData,
            ref ShadowData compatibilityShadowData,
            bool useCompatibilityShadowData,
            int mainLightIndex,
            HoShadowCastAtlasPacker packer,
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
            if (visibleLightIndex < 0)
            {
                return;
            }

            if (visibleLightIndex == mainLightIndex)
            {
                return;
            }

            int firstSlice = target.sliceCount;
            int requestedSlices = requiredType == LightType.Point ? 6 : 1;
            if (firstSlice + requestedSlices > HoShadowCastShaderConstants.MaxShadowSlices)
            {
                return;
            }

            int resolution = GetResolution(controller, requiredType);
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
                        ref cullResults,
                        universalShadowData,
                        ref compatibilityShadowData,
                        useCompatibilityShadowData,
                        visibleLightIndex,
                        visibleLights[visibleLightIndex],
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
            target.lightData0[lightIndex] = new Vector4(GetLightTypeId(requiredType), firstSlice, writtenSlices, Mathf.Clamp01(controller.shadowStrength * light.shadowStrength));
            target.lightData1[lightIndex] = new Vector4(position.x, position.y, position.z, light.range);
            target.lightData2[lightIndex] = new Vector4(direction.x, direction.y, direction.z, Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad));
            target.lightColor[lightIndex] = new Vector4(finalColor.r, finalColor.g, finalColor.b, 1.0f);
        }

        private static bool TryBuildSlice(
            ref CullingResults cullResults,
            UniversalShadowData universalShadowData,
            ref ShadowData compatibilityShadowData,
            bool useCompatibilityShadowData,
            int visibleLightIndex,
            VisibleLight visibleLight,
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
            ShadowSplitData splitData;
            bool success;

            if (lightType == LightType.Directional)
            {
                success = ShadowUtils.ExtractDirectionalLightMatrix(
                    ref cullResults,
                    DirectionalShadowData,
                    visibleLightIndex,
                    0,
                    resolution,
                    resolution,
                    resolution,
                    controller.directionalNearPlane,
                    out _,
                    out ShadowSliceData directionalSlice);

                shadowMatrix = directionalSlice.shadowTransform;
                viewMatrix = directionalSlice.viewMatrix;
                projectionMatrix = directionalSlice.projectionMatrix;
                splitData = directionalSlice.splitData;
            }
            else if (lightType == LightType.Spot)
            {
                success = useCompatibilityShadowData
                    ? ShadowUtils.ExtractSpotLightMatrix(ref cullResults, ref compatibilityShadowData, visibleLightIndex, out shadowMatrix, out viewMatrix, out projectionMatrix, out splitData)
                    : ShadowUtils.ExtractSpotLightMatrix(ref cullResults, universalShadowData, visibleLightIndex, out shadowMatrix, out viewMatrix, out projectionMatrix, out splitData);
            }
            else if (lightType == LightType.Point)
            {
                success = useCompatibilityShadowData
                    ? ShadowUtils.ExtractPointLightMatrix(ref cullResults, ref compatibilityShadowData, visibleLightIndex, (CubemapFace)face, 4.0f, out shadowMatrix, out viewMatrix, out projectionMatrix, out splitData)
                    : ShadowUtils.ExtractPointLightMatrix(ref cullResults, universalShadowData, visibleLightIndex, (CubemapFace)face, 4.0f, out shadowMatrix, out viewMatrix, out projectionMatrix, out splitData);
            }
            else
            {
                return false;
            }

            if (!success)
            {
                return false;
            }

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
            slice.shadowBias = ComputeShadowBias(visibleLight, projectionMatrix, resolution);
            slice.lightDirection = -visibleLight.localToWorldMatrix.GetColumn(2);
            slice.lightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
            slice.worldToShadow = shadowSliceData.shadowTransform;
            slice.sliceData = new Vector4((float)offsetX / atlasSize, (float)offsetY / atlasSize, (float)resolution / atlasSize, face);
            return true;
        }

        private static Vector4 ComputeShadowBias(VisibleLight visibleLight, Matrix4x4 lightProjectionMatrix, int resolution)
        {
            Light light = visibleLight.light;
            if (light == null)
            {
                return Vector4.zero;
            }

            float frustumSize;
            if (visibleLight.lightType == LightType.Directional)
            {
                frustumSize = Mathf.Abs(2.0f / lightProjectionMatrix.m00);
            }
            else if (visibleLight.lightType == LightType.Spot)
            {
                frustumSize = Mathf.Tan(visibleLight.spotAngle * 0.5f * Mathf.Deg2Rad) * visibleLight.range;
            }
            else if (visibleLight.lightType == LightType.Point)
            {
                frustumSize = Mathf.Tan((90.0f + 4.0f) * 0.5f * Mathf.Deg2Rad) * visibleLight.range;
            }
            else
            {
                frustumSize = 0.0f;
            }

            float texelSize = resolution > 0 ? frustumSize / resolution : 0.0f;
            float depthBias = -light.shadowBias * texelSize;
            float normalBias = visibleLight.lightType == LightType.Point ? 0.0f : -light.shadowNormalBias * texelSize;
            return new Vector4(depthBias, normalBias, (float)visibleLight.lightType, 0.0f);
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

        private static int GetResolution(HoShadowCastController controller, LightType type)
        {
            int atlasSize = Mathf.Max(1, controller.atlasSize);
            int resolution = type switch
            {
                LightType.Directional => controller.directionalResolution,
                LightType.Spot => controller.spotResolution,
                LightType.Point => controller.pointFaceResolution,
                _ => 64
            };

            return Mathf.Clamp(resolution, 64, atlasSize);
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

        private static void SetShadowCasterGlobals(CommandBuffer cmd, Vector3 cameraPosition, Matrix4x4 cameraViewMatrix, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, cameraViewMatrix);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.ShadowBiasId, slice.shadowBias);
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightDirectionId, new Vector4(slice.lightDirection.x, slice.lightDirection.y, slice.lightDirection.z, 0.0f));
            cmd.SetGlobalVector(HoShadowCastShaderConstants.LightPositionId, new Vector4(slice.lightPosition.x, slice.lightPosition.y, slice.lightPosition.z, 1.0f));
            cmd.SetKeyword(HoShadowCastShaderConstants.CastingPunctualLightShadowKeyword, slice.lightType != LightType.Directional);
        }

        private static void SetShadowCasterGlobals(RasterCommandBuffer cmd, Vector3 cameraPosition, Matrix4x4 cameraViewMatrix, ShadowSliceInfo slice)
        {
            cmd.SetGlobalVector(HoShadowCastShaderConstants.WorldSpaceCameraPosId, cameraPosition);
            SetWorldToCameraMatrices(cmd, cameraViewMatrix);
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
            cmd.SetGlobalMatrixArray(HoShadowCastShaderConstants.WorldToShadowId, WorldToShadowMatrices);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
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
            cmd.SetGlobalMatrixArray(HoShadowCastShaderConstants.WorldToShadowId, WorldToShadowMatrices);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData0Id, LightData0);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData1Id, LightData1);
            cmd.SetGlobalVectorArray(HoShadowCastShaderConstants.LightData2Id, LightData2);
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
                WorldToShadowMatrices[i] = frame.worldToShadow[i];
                SliceData[i] = frame.sliceData[i];
            }

            for (int i = 0; i < HoShadowCastShaderConstants.MaxLights; i++)
            {
                LightData0[i] = frame.lightData0[i];
                LightData1[i] = frame.lightData1[i];
                LightData2[i] = frame.lightData2[i];
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
