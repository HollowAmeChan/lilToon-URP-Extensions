using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.VolumeLight
{
    // Persistent resources only. No RenderGraph handles survive a render invocation.
    internal sealed class HoVolumeLightHistory
    {
        internal readonly Camera camera;
        internal readonly RTHandle[] color = new RTHandle[2], depth = new RTHandle[2];
        internal Matrix4x4 viewProjection, inverseViewProjection, projection;
        internal Vector3 position, forward;
        internal bool valid, orthographic;
        internal int index, signature, sequence;
        internal float sampleTime;
        internal double lastUse;
        private int width, height;

        internal HoVolumeLightHistory(Camera camera) { this.camera = camera; }
        internal void Ensure(int w, int h)
        {
            if (width == w && height == h && Alive(color[0]) && Alive(color[1]) && Alive(depth[0]) && Alive(depth[1])) return;
            Dispose(); width = w; height = h;
            for (int i = 0; i < 2; ++i)
            {
                color[i] = Allocate(w, h, GraphicsFormat.R16G16B16A16_SFloat, "Ho-VolumeLight History " + i);
                depth[i] = Allocate(w, h, GraphicsFormat.R32_SFloat, "Ho-VolumeLight History Depth " + i);
            }
        }
        private static bool Alive(RTHandle handle) => handle != null && handle.rt != null && handle.rt.IsCreated();
        private static RTHandle Allocate(int w, int h, GraphicsFormat format, string name)
        {
            var texture = new RenderTexture(new RenderTextureDescriptor(w, h)
            {
                graphicsFormat = format, depthStencilFormat = GraphicsFormat.None, msaaSamples = 1,
                useMipMap = false, autoGenerateMips = false
            }) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.Create();
            return RTHandles.Alloc(texture, transferOwnership: true);
        }
        internal void Invalidate() { valid = false; sequence = 0; }
        internal void Dispose()
        {
            for (int i = 0; i < 2; ++i) { color[i]?.Release(); color[i] = null; depth[i]?.Release(); depth[i] = null; }
            width = height = 0; Invalidate();
        }
    }
}
