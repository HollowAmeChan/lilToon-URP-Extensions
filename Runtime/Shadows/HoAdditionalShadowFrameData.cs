using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace lilToon.URP.Extensions.Shadows
{
    public sealed class HoAdditionalShadowFrameData : ContextItem
    {
        private readonly List<AdditionalLightShadowAtlasData.Entry> lights = new();

        public TextureHandle Visibility { get; internal set; }
        public TextureHandle SurfaceDepth { get; internal set; }
        public int CameraInstanceId { get; internal set; }
        public int Count => lights.Count;
        public int GetShaderLightIndex(int slot) => lights[slot].ShaderLightIndex;

        public bool TryGetSlice(int shaderLightIndex, out int resultSlice)
        {
            for (int slot = 0; slot < lights.Count; slot++)
            {
                if (lights[slot].ShaderLightIndex != shaderLightIndex)
                    continue;
                resultSlice = slot;
                return true;
            }
            resultSlice = -1;
            return false;
        }

        internal void Add(AdditionalLightShadowAtlasData.Entry entry) => lights.Add(entry);
        internal int GetVisibleLightIndex(int slot) => lights[slot].VisibleLightIndex;

        public override void Reset()
        {
            Visibility = TextureHandle.nullHandle;
            SurfaceDepth = TextureHandle.nullHandle;
            CameraInstanceId = 0;
            lights.Clear();
        }
    }
}
