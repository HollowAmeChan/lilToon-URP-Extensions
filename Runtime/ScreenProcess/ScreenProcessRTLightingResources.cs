using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>
    /// Frame-local publication contract between RT lighting producers and the
    /// ScreenProcess RT consumer. Products remain owned by their producer;
    /// this item only borrows their RenderGraph handles and composition intent.
    /// </summary>
    internal sealed class ScreenProcessRTLightingResources : ContextItem
    {
        internal TextureHandle directDiffuse = TextureHandle.nullHandle;
        internal TextureHandle directSpecular = TextureHandle.nullHandle;
        internal TextureHandle indirectDiffuse = TextureHandle.nullHandle;
        internal bool directEnabled;
        internal bool directAdditive;
        internal float directWeight;
        internal bool indirectEnabled;
        internal float indirectIntensity;

        internal void PublishDirect(
            TextureHandle diffuse,
            TextureHandle specular,
            float weight,
            bool additive)
        {
            directDiffuse = diffuse;
            directSpecular = specular;
            directWeight = float.IsFinite(weight) ? Mathf.Max(0.0f, weight) : 0.0f;
            directAdditive = additive;
            directEnabled = directDiffuse.IsValid() && directSpecular.IsValid() && directWeight > 0.0f;
        }

        internal void PublishIndirect(TextureHandle diffuse, float intensity)
        {
            indirectDiffuse = diffuse;
            indirectIntensity = float.IsFinite(intensity) ? Mathf.Max(0.0f, intensity) : 0.0f;
            indirectEnabled = indirectDiffuse.IsValid() && indirectIntensity > 0.0f;
        }

        internal bool HasAnyProduct =>
            (directEnabled && directDiffuse.IsValid() && directSpecular.IsValid()) ||
            (indirectEnabled && indirectDiffuse.IsValid());

        public override void Reset()
        {
            directDiffuse = TextureHandle.nullHandle;
            directSpecular = TextureHandle.nullHandle;
            indirectDiffuse = TextureHandle.nullHandle;
            directEnabled = false;
            directAdditive = false;
            directWeight = 0.0f;
            indirectEnabled = false;
            indirectIntensity = 0.0f;
        }
    }

    /// <summary>
    /// Compatibility-path equivalent of the frame-local RenderGraph contract.
    /// Producers publish borrowed RTHandles during AddRenderPasses; the
    /// consumer reads them later in its scheduled Execute call.
    /// </summary>
    internal sealed class ScreenProcessRTLightingCompatibilityFrame
    {
        internal RTHandle directDiffuse;
        internal RTHandle directSpecular;
        internal RTHandle indirectDiffuse;
        internal bool directEnabled;
        internal bool directAdditive;
        internal float directWeight;
        internal bool indirectEnabled;
        internal float indirectIntensity;

        internal bool HasAnyProduct =>
            (directEnabled && directDiffuse != null && directSpecular != null) ||
            (indirectEnabled && indirectDiffuse != null);
    }

    internal static class ScreenProcessRTLightingCompatibility
    {
        private static readonly Dictionary<Camera, ScreenProcessRTLightingCompatibilityFrame> Frames = new();

        internal static void Reset(Camera camera)
        {
            if (camera != null)
            {
                Frames.Remove(camera);
            }
        }

        internal static void Clear()
        {
            Frames.Clear();
        }

        internal static void PublishDirect(
            Camera camera,
            RTHandle diffuse,
            RTHandle specular,
            float weight,
            bool additive)
        {
            if (camera == null)
            {
                return;
            }

            ScreenProcessRTLightingCompatibilityFrame frame = GetOrCreate(camera);
            frame.directDiffuse = diffuse;
            frame.directSpecular = specular;
            frame.directWeight = float.IsFinite(weight) ? Mathf.Max(0.0f, weight) : 0.0f;
            frame.directAdditive = additive;
            frame.directEnabled = diffuse != null && specular != null && frame.directWeight > 0.0f;
        }

        internal static void PublishIndirect(Camera camera, RTHandle diffuse, float intensity)
        {
            if (camera == null)
            {
                return;
            }

            ScreenProcessRTLightingCompatibilityFrame frame = GetOrCreate(camera);
            frame.indirectDiffuse = diffuse;
            frame.indirectIntensity = float.IsFinite(intensity) ? Mathf.Max(0.0f, intensity) : 0.0f;
            frame.indirectEnabled = diffuse != null && frame.indirectIntensity > 0.0f;
        }

        internal static bool TryGet(Camera camera, out ScreenProcessRTLightingCompatibilityFrame frame)
        {
            if (camera != null && Frames.TryGetValue(camera, out frame) && frame.HasAnyProduct)
            {
                return true;
            }

            frame = null;
            return false;
        }

        private static ScreenProcessRTLightingCompatibilityFrame GetOrCreate(Camera camera)
        {
            if (!Frames.TryGetValue(camera, out ScreenProcessRTLightingCompatibilityFrame frame))
            {
                frame = new ScreenProcessRTLightingCompatibilityFrame();
                Frames.Add(camera, frame);
            }

            return frame;
        }
    }
}
