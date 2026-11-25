#pragma warning disable CS0618, CS0672

using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.GTAO
{
    // Frame-local publication of the public AO semantic. Consumers such as
    // DebugTile must depend on this handle, rather than guessing a global
    // texture binding after RenderGraph compilation.
    internal sealed class HoGTAORenderGraphResources : ContextItem
    {
        public TextureHandle aoTexture = TextureHandle.nullHandle;

        // Only the final public visibility may be borrowed as an attachment.
        // Geometry/temporal debug products can also occupy aoTexture historically.
        public bool canAppend;
        public int cameraId;
        public Vector2Int size;

        public bool CanAppend(Camera camera, int width, int height) => canAppend
            && aoTexture.IsValid() && camera != null && cameraId == camera.GetInstanceID()
            && size == new Vector2Int(width, height);

        public TextureHandle[] debugViews = new TextureHandle[5];

        public bool HasAO => aoTexture.IsValid();

        public override void Reset()
        {
            aoTexture = TextureHandle.nullHandle;
            canAppend = false;
            cameraId = 0;
            size = Vector2Int.zero;
            System.Array.Clear(debugViews, 0, debugViews.Length);
        }
    }
}
