using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.GTAO
{
    // A standalone camera has no matching screen-space AO unless it runs its own producer.
    // RenderSingleCamera skips begin/end camera callbacks, so bracket both CPU and queued GPU state.
    internal readonly struct HoAOStandaloneCameraScope : IDisposable
    {
        private readonly ScriptableRenderContext context;
        private readonly Texture texture;
        private readonly float available;
        private readonly Camera compatibilityCamera;
        private readonly RTHandle compatibilityTexture;

        internal HoAOStandaloneCameraScope(ScriptableRenderContext context)
        {
            this.context = context;
            texture = Shader.GetGlobalTexture(HoGTAOShaderConstants.AOTextureId);
            available = Shader.GetGlobalFloat(HoGTAOShaderConstants.AOAvailableId);
            compatibilityCamera = HoAOCompatibilityPublication.Camera;
            compatibilityTexture = HoAOCompatibilityPublication.Texture;
            HoAOCompatibilityPublication.Reset();
            HoAOGlobalState.Bind(context, Texture2D.whiteTexture, 0f);
        }

        public void Dispose()
        {
            HoAOCompatibilityPublication.Camera = compatibilityCamera;
            HoAOCompatibilityPublication.Texture = compatibilityTexture;
            HoAOGlobalState.Bind(context, texture != null ? texture : Texture2D.whiteTexture, available);
        }
    }
}
