using System.Collections.Generic;
using UnityEngine;

namespace lilToon.URP.Extensions.PostProcessing
{
    /// <summary>
    /// Bakes each layer's <see cref="ImageProcessLayer.ramp"/> into a small 1D ramp texture and
    /// keeps it alive for as long as the layer is in use.
    /// </summary>
    /// <remarks>
    /// The effect reads a texture instead of a handful of packed stops, which is what lets the
    /// inspector use Unity's own gradient editor (8 colour keys + 8 alpha keys, Blend/Fixed) and
    /// costs the shader a single texture fetch. The interpolation space is applied here, at bake
    /// time, so the ramp can also be evaluated with more freedom than a per-pixel two-stop lerp.
    ///
    /// The volume profile stays self-contained: the texture is generated at runtime, is not an
    /// asset, and is only rebuilt when the gradient or the interpolation space changes.
    ///
    /// Main thread only (texture creation).
    /// </remarks>
    internal static class ImageProcessGradientRampCache
    {
        /// <summary>Ramp texture width. 256 is the usual size for ramp LUTs and is plenty for a display-referred ramp.</summary>
        internal const int Resolution = ImageProcessLayer.RampResolution;

        /// <summary>Frames an unused entry survives before its texture is destroyed.</summary>
        private const int UnusedFrameLifetime = 600;

        private sealed class Entry
        {
            public int Hash;
            public Texture2D Texture;
            public int LastUsedFrame;
        }

        private static readonly Dictionary<ImageProcessLayer, Entry> Entries = new Dictionary<ImageProcessLayer, Entry>();
        private static readonly float[] BakeBuffer = new float[Resolution * 4];
        private static readonly Color[] PixelBuffer = new Color[Resolution];

        /// <summary>Black-to-white ramp: a neutral default that can never produce a broken frame.</summary>
        internal static Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.mode = GradientMode.Blend;
            gradient.colorKeys = new[]
            {
                new GradientColorKey(Color.black, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            };
            gradient.alphaKeys = new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 1.0f)
            };
            return gradient;
        }

        /// <summary>
        /// Returns the ramp texture for a layer, baking or re-baking it when the gradient or the
        /// interpolation space changed. Returns null when the layer has no usable gradient.
        /// </summary>
        internal static Texture2D GetRamp(ImageProcessLayer layer)
        {
            if (layer == null || layer.ramp == null)
            {
                return null;
            }

            int space = Mathf.Clamp(Mathf.RoundToInt(layer.parameters6.x), 0, ImageProcessGradientRampBaker.SpaceCount - 1);
            bool fixedMode = layer.ramp.mode == GradientMode.Fixed;
            int hash = ComputeHash(layer.ramp, space, fixedMode);

            if (Entries.TryGetValue(layer, out Entry entry))
            {
                entry.LastUsedFrame = Time.frameCount;
                if (entry.Hash == hash && entry.Texture != null)
                {
                    return entry.Texture;
                }

                DestroyTexture(entry.Texture);
                entry.Texture = Bake(layer.ramp, space, fixedMode);
                entry.Hash = hash;
                return entry.Texture;
            }

            var created = new Entry
            {
                Hash = hash,
                Texture = Bake(layer.ramp, space, fixedMode),
                LastUsedFrame = Time.frameCount
            };
            Entries.Add(layer, created);
            return created.Texture;
        }

        /// <summary>Destroys ramp textures whose layer has not been rendered for a while.</summary>
        internal static void PruneUnusedLayers(int frameCount)
        {
            if (Entries.Count == 0)
            {
                return;
            }

            List<ImageProcessLayer> stale = null;
            foreach (KeyValuePair<ImageProcessLayer, Entry> pair in Entries)
            {
                if (frameCount - pair.Value.LastUsedFrame <= UnusedFrameLifetime)
                {
                    continue;
                }

                stale ??= new List<ImageProcessLayer>();
                stale.Add(pair.Key);
            }

            if (stale == null)
            {
                return;
            }

            foreach (ImageProcessLayer layer in stale)
            {
                DestroyTexture(Entries[layer].Texture);
                Entries.Remove(layer);
            }
        }

        /// <summary>Drops the cached ramp of one layer (used when a layer changes effect).</summary>
        internal static void Release(ImageProcessLayer layer)
        {
            if (layer == null || !Entries.TryGetValue(layer, out Entry entry))
            {
                return;
            }

            DestroyTexture(entry.Texture);
            Entries.Remove(layer);
        }

        private static Texture2D Bake(Gradient gradient, int space, bool fixedMode)
        {
            if (!ImageProcessLayer.BakeRamp(gradient, space, BakeBuffer, out bool bakedFixedMode))
            {
                return null;
            }

            // The bake owns the stepping; the sampler has to match it (point sampling for Fixed).
            fixedMode |= bakedFixedMode;

            for (int x = 0; x < Resolution; x++)
            {
                int index = x * 4;
                PixelBuffer[x] = new Color(BakeBuffer[index], BakeBuffer[index + 1], BakeBuffer[index + 2], BakeBuffer[index + 3]);
            }

            TextureFormat format = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf)
                ? TextureFormat.RGBAHalf
                : TextureFormat.RGBA32;

            var texture = new Texture2D(Resolution, 1, format, false)
            {
                name = "Ho-ImageProcess GradientMap Ramp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = fixedMode ? FilterMode.Point : FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                anisoLevel = 0
            };
            texture.SetPixels(PixelBuffer);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static int ComputeHash(Gradient gradient, int space, bool fixedMode)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (fixedMode ? 1 : 0);
                hash = hash * 31 + space;

                GradientColorKey[] colors = gradient.colorKeys ?? System.Array.Empty<GradientColorKey>();
                hash = hash * 31 + colors.Length;
                for (int i = 0; i < colors.Length; i++)
                {
                    hash = hash * 31 + colors[i].time.GetHashCode();
                    hash = hash * 31 + colors[i].color.GetHashCode();
                }

                GradientAlphaKey[] alphas = gradient.alphaKeys ?? System.Array.Empty<GradientAlphaKey>();
                hash = hash * 31 + alphas.Length;
                for (int i = 0; i < alphas.Length; i++)
                {
                    hash = hash * 31 + alphas[i].time.GetHashCode();
                    hash = hash * 31 + alphas[i].alpha.GetHashCode();
                }

                return hash;
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
