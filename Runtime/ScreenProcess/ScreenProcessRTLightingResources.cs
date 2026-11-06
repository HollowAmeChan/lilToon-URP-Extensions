using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal readonly struct ScreenProcessRTLightingConsumerSettings
    {
        // Value snapshots: Volume edits and subsequent cameras cannot mutate recorded passes.
        internal readonly struct Layer
        {
            internal readonly bool indirect;
            internal readonly Vector4 blend, components, tint, depthRange;
            internal Layer(ScreenProcessLayer layer)
            {
                indirect = layer.effect == ScreenProcessEffect.RTGI;
                var mode = indirect ? layer.rtIndirectMode : layer.rtLightingMode;
                if (!System.Enum.IsDefined(typeof(ScreenProcessRTLightingMode), mode))
                    mode = indirect ? ScreenProcessRTLightingMode.Additive : ScreenProcessRTLightingMode.Replace;
                float gain = Mathf.Min(Safe(layer.rtGain), 65536) * Mathf.Pow(2, Mathf.Clamp(Finite(layer.rtExposure), -16, 16));
                blend = new Vector4((int)mode, Mathf.Clamp01(Safe(layer.intensity)), gain, indirect ? 1 : 0);
                components = new Vector4(Safe(layer.rtDiffuseWeight), Safe(layer.rtSpecularWeight),
                    Safe(layer.rtEmissionWeight), System.Enum.IsDefined(typeof(ScreenProcessRTReceiver), layer.rtReceiver) ? (int)layer.rtReceiver : 0);
                // Color fields use Unity's usual authored sRGB convention.
                Color color = layer.rtTint.linear;
                tint = new Vector4(Safe(color.r), Safe(color.g), Safe(color.b), 1);
                depthRange = new Vector4(Safe(layer.rtDepthRange.x), Safe(layer.rtDepthRange.y), 0, 0);
            }
            private static float Finite(float value) => float.IsFinite(value) ? value : 0;
            private static float Safe(float value) => Mathf.Max(0, Finite(value));
        }
        internal readonly Layer[] layers;
        internal bool HasAny => layers != null && layers.Length > 0;
        private ScreenProcessRTLightingConsumerSettings(Layer[] layers) { this.layers = layers; }

        internal static ScreenProcessRTLightingConsumerSettings FromLayers(
            IList<ScreenProcessLayer> layers)
        {
            var snapshots = new List<Layer>();
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    ScreenProcessLayer layer = layers[i];
                    if (layer == null || !layer.IsActive)
                    {
                        continue;
                    }

                    if (ScreenProcessEffectRegistry.IsRTLightingEffect(layer.effect))
                        snapshots.Add(new Layer(layer));
                }
            }

            return new ScreenProcessRTLightingConsumerSettings(snapshots.ToArray());
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
