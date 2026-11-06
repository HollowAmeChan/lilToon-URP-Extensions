using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ScreenProcessRTLightingConsumerSettings
    {
        internal readonly bool directEnabled;
        internal readonly bool directAdditive;
        internal readonly float directWeight;
        internal readonly bool indirectEnabled;
        internal readonly float indirectIntensity;

        internal bool HasAny => directEnabled || indirectEnabled;

        internal ScreenProcessRTLightingConsumerSettings(
            bool directEnabled,
            bool directAdditive,
            float directWeight,
            bool indirectEnabled,
            float indirectIntensity)
        {
            this.directEnabled = directEnabled;
            this.directAdditive = directAdditive;
            this.directWeight = float.IsFinite(directWeight) ? Mathf.Max(0.0f, directWeight) : 0.0f;
            this.indirectEnabled = indirectEnabled;
            this.indirectIntensity = float.IsFinite(indirectIntensity) ? Mathf.Max(0.0f, indirectIntensity) : 0.0f;
        }

        internal static ScreenProcessRTLightingConsumerSettings FromLayers(
            IList<ScreenProcessLayer> layers)
        {
            bool directEnabled = false;
            bool directAdditive = false;
            float directWeight = 0.0f;
            bool indirectEnabled = false;
            float indirectIntensity = 0.0f;

            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ScreenProcessLayer layer = layers[i];
                    if (layer == null || !layer.IsActive)
                    {
                        continue;
                    }

                    if (layer.effect == ScreenProcessEffect.RTDI)
                    {
                        directEnabled = true;
                        directAdditive = layer.rtLightingMode == ScreenProcessRTLightingMode.Additive;
                        directWeight = layer.intensity;
                    }
                    else if (layer.effect == ScreenProcessEffect.RTGI)
                    {
                        indirectEnabled = true;
                        indirectIntensity = layer.intensity;
                    }
                }
            }

            return new ScreenProcessRTLightingConsumerSettings(
                directEnabled,
                directAdditive,
                directWeight,
                indirectEnabled,
                indirectIntensity);
        }
    }

    /// <summary>
    /// Frame-local publication contract between RT lighting producers and the
    /// ScreenProcess RT consumer. Products remain owned by their producer;
    /// this item only borrows their RenderGraph handles.
    /// </summary>
    internal sealed class ScreenProcessRTLightingResources : ContextItem
    {
        internal TextureHandle directDiffuse = TextureHandle.nullHandle;
        internal TextureHandle directSpecular = TextureHandle.nullHandle;
        internal TextureHandle indirectDiffuse = TextureHandle.nullHandle;
        internal bool directEnabled;
        internal bool indirectEnabled;

        internal void PublishDirectProduct(TextureHandle diffuse, TextureHandle specular)
        {
            directDiffuse = diffuse;
            directSpecular = specular;
            directEnabled = directDiffuse.IsValid() && directSpecular.IsValid();
        }

        internal void PublishIndirectProduct(TextureHandle diffuse)
        {
            indirectDiffuse = diffuse;
            indirectEnabled = indirectDiffuse.IsValid();
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
            indirectEnabled = false;
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
        internal bool indirectEnabled;

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

        internal static void PublishDirectProduct(Camera camera, RTHandle diffuse, RTHandle specular)
        {
            if (camera == null)
            {
                return;
            }

            ScreenProcessRTLightingCompatibilityFrame frame = GetOrCreate(camera);
            frame.directDiffuse = diffuse;
            frame.directSpecular = specular;
            frame.directEnabled = diffuse != null && specular != null;
        }

        internal static void PublishIndirectProduct(Camera camera, RTHandle diffuse)
        {
            if (camera == null)
            {
                return;
            }

            ScreenProcessRTLightingCompatibilityFrame frame = GetOrCreate(camera);
            frame.indirectDiffuse = diffuse;
            frame.indirectEnabled = diffuse != null;
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
