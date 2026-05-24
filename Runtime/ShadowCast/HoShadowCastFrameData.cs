#pragma warning disable CS0618

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal static class HoShadowCastAtlasDescriptors
    {
        public static RenderTextureDescriptor CreateAtlasDescriptor(HoShadowCastFrameConfig config)
        {
            int size = Mathf.Max(1, config != null ? config.atlasSize : 1);
            return CreateDepthAtlasDescriptor(size);
        }

        public static RenderTextureDescriptor CreateSecondDirectionalAtlasDescriptor(HoShadowCastFrameConfig config)
        {
            int size = Mathf.Max(1, config != null ? config.secondDirectionalAtlasSize : 1);
            return CreateDepthAtlasDescriptor(size);
        }

        private static RenderTextureDescriptor CreateDepthAtlasDescriptor(int size)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(size, size, GraphicsFormat.None, GraphicsFormatUtility.GetDepthStencilFormat(32, 0));
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 32;
            descriptor.shadowSamplingMode = RenderingUtils.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap)
                ? ShadowSamplingMode.CompareDepths
                : ShadowSamplingMode.None;
            return descriptor;
        }
    }

    internal sealed class HoShadowCastRenderTargets
    {
        private RTHandle atlasTexture;
        private RTHandle secondDirectionalAtlasTexture;

        public RTHandle AtlasTexture => atlasTexture;
        public RTHandle SecondDirectionalAtlasTexture => secondDirectionalAtlasTexture;

        public void ReAllocateIfNeeded(RenderTextureDescriptor descriptor)
        {
            RenderingUtils.ReAllocateIfNeeded(ref atlasTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoShadowCastShaderConstants.AtlasTextureName);
        }

        public void ReAllocateSecondDirectionalIfNeeded(RenderTextureDescriptor descriptor)
        {
            RenderingUtils.ReAllocateIfNeeded(ref secondDirectionalAtlasTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: HoShadowCastShaderConstants.SecondDirectionalAtlasTextureName);
        }

        public void Release()
        {
            atlasTexture?.Release();
            atlasTexture = null;
            secondDirectionalAtlasTexture?.Release();
            secondDirectionalAtlasTexture = null;
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
        public Vector4 pcssParams;
        public Vector4 pcssParams2;
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
            pcssParams = Vector4.zero;
            pcssParams2 = Vector4.zero;

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

    internal sealed class HoShadowCastSecondDirectionalFrame
    {
        public int atlasSize;
        public int lightCount;
        public int cascadeCountPerLight;
        public int sliceCount;
        public Vector3 cameraPosition;
        public Matrix4x4 cameraViewMatrix;
        public Matrix4x4 cameraProjectionMatrix;
        public Vector4 pcssParams;
        public Vector4 pcssParams2;
        public readonly Light[] sourceLights = new Light[HoShadowCastShaderConstants.MaxDirectionalLights];
        public readonly Vector4[] lightData = new Vector4[HoShadowCastShaderConstants.MaxDirectionalLights];
        public readonly ShadowSliceInfo[] slices = new ShadowSliceInfo[HoShadowCastShaderConstants.MaxSecondDirectionalSlices];
        public readonly Matrix4x4[] worldToShadow = new Matrix4x4[HoShadowCastShaderConstants.MaxSecondDirectionalSlices];
        public readonly Vector4[] sliceData = new Vector4[HoShadowCastShaderConstants.MaxSecondDirectionalSlices];

        public void Clear()
        {
            atlasSize = 1;
            lightCount = 0;
            cascadeCountPerLight = 0;
            sliceCount = 0;
            cameraPosition = Vector3.zero;
            cameraViewMatrix = Matrix4x4.identity;
            cameraProjectionMatrix = Matrix4x4.identity;
            pcssParams = Vector4.zero;
            pcssParams2 = Vector4.zero;
            for (int i = 0; i < sourceLights.Length; i++)
            {
                sourceLights[i] = null;
                lightData[i] = Vector4.zero;
            }

            for (int i = 0; i < slices.Length; i++)
            {
                slices[i] = default;
                worldToShadow[i] = Matrix4x4.identity;
                sliceData[i] = Vector4.zero;
            }
        }

        public void FillUnused()
        {
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
