#pragma warning disable CS0618

using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal static class HoShadowCastFrameCollector
    {
        private static int lastDebugLogFrame = -1000;

        public static bool BuildFrameData(
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            LightData lightData,
            ref ShadowData shadowData,
            Camera camera,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            return BuildFrameData(
                config,
                ref cullResults,
                lightData.visibleLights,
                null,
                ref shadowData,
                true,
                camera,
                mainLightIndex,
                cameraPosition,
                cameraViewMatrix,
                cameraProjectionMatrix,
                target,
                diagnostics);
        }

        public static bool BuildFrameData(
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            UniversalLightData lightData,
            UniversalShadowData shadowData,
            Camera camera,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            ShadowData unusedCompatibilityShadowData = default;
            return BuildFrameData(
                config,
                ref cullResults,
                lightData.visibleLights,
                shadowData,
                ref unusedCompatibilityShadowData,
                false,
                camera,
                mainLightIndex,
                cameraPosition,
                cameraViewMatrix,
                cameraProjectionMatrix,
                target,
                diagnostics);
        }

        public static bool BuildFrameData(
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            UniversalShadowData universalShadowData,
            ref ShadowData compatibilityShadowData,
            bool useCompatibilityShadowData,
            Camera camera,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            if (config == null)
            {
                return false;
            }

            target.Clear();
            target.atlasSize = Mathf.Max(1, config.atlasSize);
            target.cameraPosition = cameraPosition;
            target.cameraViewMatrix = cameraViewMatrix;
            target.cameraProjectionMatrix = cameraProjectionMatrix;
            target.pcssParams = CreatePcssParams(config, config.punctualPcssSoftness);
            target.pcssParams2 = CreatePcssParams2(config);

            int requestedSliceCount = CountRequestedSlices(config, visibleLights, mainLightIndex);
            int maxSliceResolution = GetMaxResolutionForSliceCount(target.atlasSize, requestedSliceCount);
            HoShadowCastAtlasPacker packer = new HoShadowCastAtlasPacker(target.atlasSize);
            if (config.collectVisibleLights)
            {
                AddVisibleLights(LightType.Spot, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
                AddVisibleLights(LightType.Point, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
            }
            else
            {
                AddLightArray(config.spotLights, LightType.Spot, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
                AddLightArray(config.pointLights, LightType.Point, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
            }

            target.FillUnused();
            return target.lightCount > 0 && target.sliceCount > 0;
        }

        public static bool BuildSecondDirectionalFrameData(
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            Camera camera,
            int mainLightIndex,
            Vector3 cameraPosition,
            Matrix4x4 cameraViewMatrix,
            Matrix4x4 cameraProjectionMatrix,
            HoShadowCastSecondDirectionalFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            target.Clear();
            target.cameraPosition = cameraPosition;
            target.cameraViewMatrix = cameraViewMatrix;
            target.cameraProjectionMatrix = cameraProjectionMatrix;
            if (config == null)
            {
                return false;
            }

            target.pcssParams = CreatePcssParams(config, config.secondDirectionalPcssSoftness);
            target.pcssParams2 = CreatePcssParams2(config);

            if (camera == null)
            {
                return false;
            }

            int cascadeCount = Mathf.Clamp(config.secondDirectionalCascadeCount, 1, HoShadowCastShaderConstants.MaxSecondDirectionalCascades);
            int atlasSize = Mathf.Max(1, config.secondDirectionalAtlasSize);
            int requestedSliceCount = CountRequestedSecondDirectionalSlices(config, visibleLights, mainLightIndex, cascadeCount);
            if (requestedSliceCount <= 0)
            {
                return false;
            }

            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(requestedSliceCount));
            int resolution = Mathf.Max(64, atlasSize / Mathf.Max(1, gridSize));
            float nearDistance = Mathf.Max(0.001f, camera.nearClipPlane);
            float farDistance = Mathf.Min(Mathf.Max(nearDistance + 0.01f, config.secondDirectionalMaxDistance), Mathf.Max(nearDistance + 0.01f, camera.farClipPlane));

            target.atlasSize = atlasSize;
            target.cascadeCountPerLight = cascadeCount;
            target.lightCount = 0;
            target.sliceCount = 0;

            int directionalCandidateCount = config.collectVisibleLights && visibleLights.IsCreated
                ? visibleLights.Length
                : (config.directionalLights != null ? config.directionalLights.Length : 0);
            for (int lightSlot = 0; lightSlot < directionalCandidateCount; lightSlot++)
            {
                Light light;
                if (config.collectVisibleLights)
                {
                    if (!visibleLights.IsCreated || lightSlot == mainLightIndex)
                    {
                        continue;
                    }

                    VisibleLight visibleLight = visibleLights[lightSlot];
                    if (visibleLight.lightType != LightType.Directional)
                    {
                        continue;
                    }

                    light = visibleLight.light;
                }
                else
                {
                    light = config.directionalLights[lightSlot];
                }

                if (light == null && !config.collectVisibleLights)
                {
                    continue;
                }

                diagnostics?.AddCandidate();
                if (!IsLightCollectable(light, config, LightType.Directional, config.collectVisibleLights))
                {
                    diagnostics?.AddSkipped(light, "SecondDirectional", LightType.Directional, GetCandidateSkipReason(light, config, LightType.Directional, config.collectVisibleLights));
                    continue;
                }

                int visibleLightIndex = FindVisibleLightIndex(visibleLights, light, LightType.Directional);
                if (visibleLightIndex >= 0 && visibleLightIndex == mainLightIndex)
                {
                    diagnostics?.AddSkipped(light, "SecondDirectional", LightType.Directional, "URP main light is skipped");
                    continue;
                }

                if (target.lightCount >= HoShadowCastShaderConstants.MaxDirectionalLights || target.sliceCount + cascadeCount > HoShadowCastShaderConstants.MaxSecondDirectionalSlices)
                {
                    diagnostics?.AddSkipped(light, "SecondDirectional", LightType.Directional, "capacity limit reached");
                    break;
                }

                int firstSlice = target.sliceCount;
                float lightShadowStrength = light.shadows == LightShadows.None ? 1.0f : light.shadowStrength;
                float shadowStrength = Mathf.Clamp01(config.secondDirectionalShadowStrength * lightShadowStrength);

                float previousDistance = nearDistance;
                for (int cascadeIndex = 0; cascadeIndex < cascadeCount; cascadeIndex++)
                {
                    float splitRatio = GetSecondDirectionalCascadeSplit(config.secondDirectionalCascadeSplits, cascadeCount, cascadeIndex);
                    float cascadeFarDistance = cascadeIndex == cascadeCount - 1
                        ? farDistance
                        : Mathf.Lerp(nearDistance, farDistance, splitRatio);
                    cascadeFarDistance = Mathf.Max(previousDistance + 0.01f, cascadeFarDistance);

                    int tileIndex = target.sliceCount;
                    int tileX = tileIndex % gridSize;
                    int tileY = tileIndex / gridSize;
                    int offsetX = tileX * resolution;
                    int offsetY = tileY * resolution;
                    if (!TryBuildSecondDirectionalCascadeSlice(
                            light,
                            camera,
                            previousDistance,
                            cascadeFarDistance,
                            config,
                            atlasSize,
                            resolution,
                            offsetX,
                            offsetY,
                            out ShadowSliceInfo slice))
                    {
                        diagnostics?.AddSkipped(light, "SecondDirectional", LightType.Directional, "failed to build cascade slice");
                        target.Clear();
                        return false;
                    }

                    target.slices[target.sliceCount] = slice;
                    target.worldToShadow[target.sliceCount] = slice.worldToShadow;
                    target.sliceData[target.sliceCount] = slice.sliceData;
                    target.sliceCount++;
                    previousDistance = cascadeFarDistance;
                }

                int lightIndex = target.lightCount++;
                target.sourceLights[lightIndex] = light;
                target.lightData[lightIndex] = new Vector4(firstSlice, cascadeCount, shadowStrength, 0.0f);
                diagnostics?.AddAccepted(light, "SecondDirectional", LightType.Directional, firstSlice, cascadeCount, resolution);
            }

            target.FillUnused();
            return target.lightCount > 0 && target.sliceCount > 0;
        }

        private static int CountRequestedSecondDirectionalSlices(HoShadowCastFrameConfig config, NativeArray<VisibleLight> visibleLights, int mainLightIndex, int cascadeCount)
        {
            if (config == null)
            {
                return 0;
            }

            int count = 0;
            if (config.collectVisibleLights)
            {
                if (!visibleLights.IsCreated)
                {
                    return 0;
                }

                for (int i = 0; i < visibleLights.Length; i++)
                {
                    if (GetVisibleLight(visibleLights, i, config, LightType.Directional, mainLightIndex) != null)
                    {
                        count += cascadeCount;
                    }
                }
            }
            else if (config.directionalLights != null)
            {
                for (int i = 0; i < config.directionalLights.Length; i++)
                {
                    Light light = config.directionalLights[i];
                    if (!IsLightCollectable(light, config, LightType.Directional, false))
                    {
                        continue;
                    }

                    int visibleLightIndex = FindVisibleLightIndex(visibleLights, light, LightType.Directional);
                    if (visibleLightIndex >= 0 && visibleLightIndex == mainLightIndex)
                    {
                        continue;
                    }

                    count += cascadeCount;
                }
            }

            return Mathf.Min(count, HoShadowCastShaderConstants.MaxSecondDirectionalSlices);
        }

        private static float GetSecondDirectionalCascadeSplit(Vector3 splits, int cascadeCount, int cascadeIndex)
        {
            float splitX = Mathf.Clamp(splits.x, 0.001f, 0.997f);
            float splitY = Mathf.Clamp(splits.y, splitX + 0.001f, 0.998f);
            float splitZ = Mathf.Clamp(splits.z, splitY + 0.001f, 0.999f);
            if (cascadeCount <= 1)
            {
                return 1.0f;
            }

            if (cascadeCount == 2)
            {
                return cascadeIndex == 0 ? splitX : 1.0f;
            }

            if (cascadeCount == 3)
            {
                if (cascadeIndex == 0)
                {
                    return splitX;
                }

                return cascadeIndex == 1 ? splitY : 1.0f;
            }

            return cascadeIndex switch
            {
                0 => splitX,
                1 => splitY,
                2 => splitZ,
                _ => 1.0f
            };
        }

        private static bool TryBuildSecondDirectionalCascadeSlice(
            Light light,
            Camera camera,
            float cascadeNearDistance,
            float cascadeFarDistance,
            HoShadowCastFrameConfig config,
            int atlasSize,
            int resolution,
            int offsetX,
            int offsetY,
            out ShadowSliceInfo slice)
        {
            slice = new ShadowSliceInfo
            {
                visibleLightIndex = -1,
                lightType = LightType.Directional,
                faceIndex = 0
            };

            if (light == null || camera == null)
            {
                return false;
            }

            Vector3[] corners = new Vector3[8];
            FillCameraFrustumCorners(camera, cascadeNearDistance, cascadeFarDistance, corners);

            Vector3 center = Vector3.zero;
            for (int i = 0; i < corners.Length; i++)
            {
                center += corners[i];
            }

            center /= corners.Length;

            Vector3 lightForward = light.transform.forward;
            float minLightDistance = float.PositiveInfinity;
            float maxLightDistance = float.NegativeInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                float lightDistance = Vector3.Dot(corners[i] - center, lightForward);
                minLightDistance = Mathf.Min(minLightDistance, lightDistance);
                maxLightDistance = Mathf.Max(maxLightDistance, lightDistance);
            }

            float cascadeDepth = maxLightDistance - minLightDistance;
            float depth = Mathf.Max(Mathf.Max(0.01f, config.secondDirectionalShadowDepth), cascadeDepth + 1.0f);
            Matrix4x4 fitViewMatrix = CreateViewMatrix(center - lightForward * (depth * 0.5f), lightForward, light.transform.up);
            float size = 0.01f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 cornerView = fitViewMatrix.MultiplyPoint(corners[i]);
                size = Mathf.Max(size, Mathf.Max(Mathf.Abs(cornerView.x), Mathf.Abs(cornerView.y)));
            }

            size *= 1.05f;
            center = SnapDirectionalCascadeCenter(center, lightForward, light.transform.up, size, resolution);

            float nearPlane = Mathf.Max(0.001f, light.shadowNearPlane);
            Matrix4x4 viewMatrix = CreateViewMatrix(center - lightForward * (depth * 0.5f), lightForward, light.transform.up);
            Matrix4x4 projectionMatrix = Matrix4x4.Ortho(-size, size, -size, size, nearPlane, depth);
            Matrix4x4 shadowMatrix = GetShadowTransform(projectionMatrix, viewMatrix);

            ShadowSliceData shadowSliceData = new ShadowSliceData
            {
                viewMatrix = viewMatrix,
                projectionMatrix = projectionMatrix,
                shadowTransform = shadowMatrix,
                splitData = default,
                offsetX = offsetX,
                offsetY = offsetY,
                resolution = resolution
            };
            ShadowUtils.ApplySliceTransform(ref shadowSliceData, atlasSize, atlasSize);

            slice.shadowSliceData = shadowSliceData;
            slice.viewMatrix = viewMatrix;
            slice.projectionMatrix = projectionMatrix;
            slice.splitData = default;
            slice.shadowBias = ComputeShadowBias(light, LightType.Directional, projectionMatrix, resolution);
            slice.lightDirection = -light.transform.forward;
            slice.lightPosition = light.transform.position;
            slice.worldToShadow = shadowMatrix;
            slice.sliceData = new Vector4((float)offsetX / atlasSize, (float)offsetY / atlasSize, (float)resolution / atlasSize, cascadeFarDistance * cascadeFarDistance);
            return true;
        }

        private static void FillCameraFrustumCorners(Camera camera, float nearDistance, float farDistance, Vector3[] corners)
        {
            Vector3[] tempCorners = new Vector3[4];
            camera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), nearDistance, Camera.MonoOrStereoscopicEye.Mono, tempCorners);
            for (int i = 0; i < 4; i++)
            {
                corners[i] = camera.transform.position + camera.transform.TransformVector(tempCorners[i]);
            }

            camera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), farDistance, Camera.MonoOrStereoscopicEye.Mono, tempCorners);
            for (int i = 0; i < 4; i++)
            {
                corners[i + 4] = camera.transform.position + camera.transform.TransformVector(tempCorners[i]);
            }
        }

        private static Vector3 SnapDirectionalCascadeCenter(Vector3 center, Vector3 lightForward, Vector3 lightUp, float size, int resolution)
        {
            if (resolution <= 0 || size <= 0.0f)
            {
                return center;
            }

            Matrix4x4 lightViewAtOrigin = CreateViewMatrix(Vector3.zero, lightForward, lightUp);
            Vector3 centerLightSpace = lightViewAtOrigin.MultiplyPoint(center);
            float texelSize = (size * 2.0f) / resolution;
            centerLightSpace.x = Mathf.Round(centerLightSpace.x / texelSize) * texelSize;
            centerLightSpace.y = Mathf.Round(centerLightSpace.y / texelSize) * texelSize;
            return lightViewAtOrigin.inverse.MultiplyPoint(centerLightSpace);
        }

        private static void AddLightArray(
            Light[] lights,
            LightType requiredType,
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            int mainLightIndex,
            int maxSliceResolution,
            ref HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            if (lights == null)
            {
                return;
            }

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null)
                {
                    continue;
                }

                AddLight(lights[i], requiredType, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
            }
        }

        private static void AddVisibleLights(
            LightType requiredType,
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            int mainLightIndex,
            int maxSliceResolution,
            ref HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            if (!visibleLights.IsCreated)
            {
                return;
            }

            for (int i = 0; i < visibleLights.Length; i++)
            {
                if (i == mainLightIndex)
                {
                    continue;
                }

                VisibleLight visibleLight = visibleLights[i];
                if (visibleLight.lightType != requiredType)
                {
                    continue;
                }

                Light light = visibleLight.light;
                if (!IsLightCollectable(light, config, requiredType, true))
                {
                    diagnostics?.AddCandidate();
                        diagnostics?.AddSkipped(light, "Punctual", requiredType, GetCandidateSkipReason(light, config, requiredType, true));
                    continue;
                }

                AddLight(light, requiredType, config, ref cullResults, visibleLights, mainLightIndex, maxSliceResolution, ref packer, target, diagnostics);
            }
        }

        private static void AddLight(
            Light light,
            LightType requiredType,
            HoShadowCastFrameConfig config,
            ref CullingResults cullResults,
            NativeArray<VisibleLight> visibleLights,
            int mainLightIndex,
            int maxSliceResolution,
            ref HoShadowCastAtlasPacker packer,
            HoShadowCastFrame target,
            HoShadowCastFrameDiagnostics diagnostics)
        {
            diagnostics?.AddCandidate();
            if (!IsLightCollectable(light, config, requiredType, false) || target.Contains(light))
            {
                diagnostics?.AddSkipped(light, "Punctual", requiredType, GetCandidateSkipReason(light, config, requiredType, false, target));
                return;
            }

            if (target.lightCount >= HoShadowCastShaderConstants.MaxLights)
            {
                diagnostics?.AddSkipped(light, "Punctual", requiredType, "light capacity limit reached");
                return;
            }

            int visibleLightIndex = FindVisibleLightIndex(visibleLights, light, requiredType);
            if (visibleLightIndex >= 0 && visibleLightIndex == mainLightIndex)
            {
                diagnostics?.AddSkipped(light, "Punctual", requiredType, "URP main light is skipped");
                return;
            }

            int firstSlice = target.sliceCount;
            int requestedSlices = requiredType == LightType.Point ? 6 : 1;
            if (firstSlice + requestedSlices > HoShadowCastShaderConstants.MaxShadowSlices)
            {
                diagnostics?.AddSkipped(light, "Punctual", requiredType, "slice capacity limit reached");
                return;
            }

            int resolution = GetResolution(config, requiredType, maxSliceResolution);
            int writtenSlices = 0;
            bool completed = true;
            for (int face = 0; face < requestedSlices; face++)
            {
                if (!packer.TryAllocate(resolution, out int offsetX, out int offsetY))
                {
                    diagnostics?.AddSkipped(light, "Punctual", requiredType, "atlas is full");
                    completed = false;
                    break;
                }

                if (!TryBuildSlice(
                        light,
                        ref cullResults,
                        visibleLightIndex,
                        requiredType,
                        face,
                        config,
                        target.atlasSize,
                        resolution,
                        offsetX,
                        offsetY,
                        out ShadowSliceInfo slice))
                {
                    diagnostics?.AddSkipped(light, "Punctual", requiredType, "failed to build shadow slice");
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
            Vector3 direction = light.transform.forward;
            Color finalColor = light.color * light.intensity;
            float lightShadowStrength = light.shadows == LightShadows.None ? 1.0f : light.shadowStrength;
            float controllerStrength = requiredType == LightType.Directional ? config.shadowStrength : config.punctualShadowStrength;
            target.lightData0[lightIndex] = new Vector4(GetLightTypeId(requiredType), firstSlice, writtenSlices, Mathf.Clamp01(controllerStrength * lightShadowStrength));
            target.lightData1[lightIndex] = new Vector4(position.x, position.y, position.z, light.range);
            target.lightData2[lightIndex] = new Vector4(direction.x, direction.y, direction.z, Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad));
            target.lightAttenuation[lightIndex] = ComputeLightAttenuation(light, requiredType, config.punctualShadowFadeSpeed);
            target.lightColor[lightIndex] = new Vector4(finalColor.r, finalColor.g, finalColor.b, 1.0f);
            diagnostics?.AddAccepted(light, "Punctual", requiredType, firstSlice, writtenSlices, resolution);
        }

        private static bool TryBuildSlice(
            Light light,
            ref CullingResults cullResults,
            int visibleLightIndex,
            LightType lightType,
            int face,
            HoShadowCastFrameConfig config,
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

            if (!TryBuildLightMatrices(light, ref cullResults, visibleLightIndex, lightType, face, config, out viewMatrix, out projectionMatrix, out splitData))
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
            HoShadowCastFrameConfig config,
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
                float size = Mathf.Max(0.01f, config.directionalShadowSize);
                float depth = Mathf.Max(nearPlane + 0.01f, config.directionalShadowDepth);
                Vector3 lightForward = lightTransform.forward;
                Vector3 lightPosition = config.directionalAnchorPosition - lightForward * (depth * 0.5f);
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

        public static void MaybeLogDebugFrame(HoShadowCastFrameConfig config, HoShadowCastFrame frame, HoShadowCastSecondDirectionalFrame secondDirectionalFrame, string path, bool hasFrame, bool hasSecondDirectionalFrame)
        {
            if (config == null || config.debugMode == HoShadowCastDebugMode.Off)
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
            builder.Append(", secondDirectional=");
            builder.Append(hasSecondDirectionalFrame);
            builder.Append("@");
            builder.Append(secondDirectionalFrame.lightCount);
            builder.Append("x");
            builder.Append(secondDirectionalFrame.cascadeCountPerLight);
            builder.Append("/");
            builder.Append(secondDirectionalFrame.sliceCount);
            builder.Append("x");
            builder.Append(secondDirectionalFrame.atlasSize);
            builder.Append(", casterMask=0x");
            builder.Append(config.casterLayerMask.value.ToString("X8"));
            builder.Append(", lightMask=0x");
            builder.Append(config.lightLayerMask.value.ToString("X8"));
            builder.Append(", strength second/punctual=");
            builder.Append(config.secondDirectionalShadowStrength.ToString("0.##"));
            builder.Append("/");
            builder.Append(config.punctualShadowStrength.ToString("0.##"));
            builder.Append(", source=");
            builder.Append(config.collectVisibleLights ? "visibleLights" : "controller");
            if (!config.collectVisibleLights)
            {
                builder.Append(", assigned D/S/P=");
                builder.Append(CountAssigned(config.directionalLights));
                builder.Append('/');
                builder.Append(CountAssigned(config.spotLights));
                builder.Append('/');
                builder.Append(CountAssigned(config.pointLights));
            }

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

            Debug.Log(builder.ToString(), config.controller);
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

        private static Light GetVisibleLight(NativeArray<VisibleLight> visibleLights, int index, HoShadowCastFrameConfig config, LightType requiredType, int mainLightIndex)
        {
            if (!visibleLights.IsCreated || index < 0 || index >= visibleLights.Length || index == mainLightIndex)
            {
                return null;
            }

            VisibleLight visibleLight = visibleLights[index];
            Light light = visibleLight.light;
            if (light == null || visibleLight.lightType != requiredType || !IsLightCollectable(light, config, requiredType, true))
            {
                return null;
            }

            return light;
        }

        private static bool IsLightCollectable(Light light, HoShadowCastFrameConfig config, LightType requiredType, bool requireShadows)
        {
            if (light == null || light.type != requiredType || !light.isActiveAndEnabled)
            {
                return false;
            }

            if (!IsLightLayerAllowed(light, config))
            {
                return false;
            }

            return !requireShadows || light.shadows != LightShadows.None;
        }

        private static bool IsLightLayerAllowed(Light light, HoShadowCastFrameConfig config)
        {
            if (light == null || config == null)
            {
                return true;
            }

            GameObject lightObject = light.gameObject;
            return lightObject == null || IsLayerInMask(lightObject.layer, config.lightLayerMask);
        }

        private static bool IsLayerInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private static string GetCandidateSkipReason(Light light, HoShadowCastFrameConfig config, LightType requiredType, bool requireShadows, HoShadowCastFrame target = null)
        {
            if (light == null)
            {
                return "no eligible visible light";
            }

            if (light.type != requiredType)
            {
                return "light type mismatch";
            }

            if (!light.isActiveAndEnabled)
            {
                return "light disabled";
            }

            if (!IsLightLayerAllowed(light, config))
            {
                return "light layer excluded";
            }

            if (requireShadows && light.shadows == LightShadows.None)
            {
                return "shadows disabled";
            }

            if (target != null && target.Contains(light))
            {
                return "duplicate light";
            }

            return "not eligible";
        }

        private static int CountRequestedSlices(HoShadowCastFrameConfig config, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            if (config == null)
            {
                return 0;
            }

            int count = 0;
            if (config.collectVisibleLights)
            {
                count += CountRequestedVisibleSlices(config, LightType.Spot, visibleLights, mainLightIndex);
                count += CountRequestedVisibleSlices(config, LightType.Point, visibleLights, mainLightIndex);
            }
            else
            {
                count += CountRequestedSlices(config.spotLights, config, LightType.Spot, visibleLights, mainLightIndex);
                count += CountRequestedSlices(config.pointLights, config, LightType.Point, visibleLights, mainLightIndex);
            }

            return count;
        }

        private static int CountRequestedVisibleSlices(HoShadowCastFrameConfig config, LightType requiredType, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            if (!visibleLights.IsCreated)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                if (GetVisibleLight(visibleLights, i, config, requiredType, mainLightIndex) != null)
                {
                    count += requiredType == LightType.Point ? 6 : 1;
                }
            }

            return count;
        }

        private static int CountRequestedSlices(Light[] lights, HoShadowCastFrameConfig config, LightType requiredType, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            if (lights == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (!IsLightCollectable(light, config, requiredType, false))
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

        private static int GetResolution(HoShadowCastFrameConfig config, LightType type, int maxSliceResolution)
        {
            int atlasSize = Mathf.Max(1, config.atlasSize);
            int resolution = type switch
            {
                LightType.Directional => config.directionalResolution,
                LightType.Spot => config.spotResolution,
                LightType.Point => config.pointFaceResolution,
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

        private static Vector4 ComputeLightAttenuation(Light light, LightType lightType, float fadeSpeed)
        {
            if (light == null || lightType == LightType.Directional)
            {
                return Vector4.zero;
            }

            float range = Mathf.Max(0.0001f, light.range);
            float oneOverRangeSqr = 1.0f / (range * range);
            fadeSpeed = fadeSpeed <= 0.0f ? 1.0f : Mathf.Clamp(fadeSpeed, 0.1f, 4.0f);
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

            return new Vector4(oneOverRangeSqr, fadeSpeed, spotScale, spotOffset);
        }

        private static Vector4 CreatePcssParams(HoShadowCastFrameConfig config, float softness)
        {
            if (config == null || !config.pcssEnabled || softness <= 0.0f)
            {
                return Vector4.zero;
            }

            return new Vector4(
                1.0f,
                Mathf.Clamp(softness, 0.0f, 4.0f),
                Mathf.Clamp(config.pcssBlockerSearchRadius, 0.25f, 8.0f),
                Mathf.Clamp(config.pcssMaxPenumbraRadius, 1.0f, 32.0f));
        }

        private static Vector4 CreatePcssParams2(HoShadowCastFrameConfig config)
        {
            if (config == null || !config.pcssEnabled)
            {
                return Vector4.zero;
            }

            GetPcssSampleCounts(config.pcssQuality, out int blockerSamples, out int filterSamples);
            return new Vector4(
                Mathf.Clamp(config.pcssDepthBias, 0.0f, 0.01f),
                blockerSamples,
                filterSamples,
                0.0f);
        }

        private static void GetPcssSampleCounts(HoShadowCastPcssQuality quality, out int blockerSamples, out int filterSamples)
        {
            switch (quality)
            {
                case HoShadowCastPcssQuality.Low:
                    blockerSamples = 8;
                    filterSamples = 16;
                    break;
                case HoShadowCastPcssQuality.High:
                    blockerSamples = 24;
                    filterSamples = 48;
                    break;
                case HoShadowCastPcssQuality.Ultra:
                    blockerSamples = 32;
                    filterSamples = 64;
                    break;
                default:
                    blockerSamples = 16;
                    filterSamples = 32;
                    break;
            }
        }

    }
}
