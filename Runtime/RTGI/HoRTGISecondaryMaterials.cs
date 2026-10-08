using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RTGI
{
    // GI-only closest-hit materials. Source materials and their raster passes
    // are never modified. Unsupported opaque surfaces remain occluders.
    internal sealed class HoRTGISecondaryMaterials : IDisposable
    {
        private readonly Shader shader;
        private readonly Dictionary<Material, Material> adapters = new(MaterialReferences.Instance);
        private readonly HashSet<Material> seen = new(MaterialReferences.Instance);
        private readonly List<Material> removed = new();
        internal readonly List<string> Rejections = new();
        internal int Signature { get; private set; }
        internal bool TimeVarying { get; private set; }
        private sealed class MaterialReferences : IEqualityComparer<Material>
        {
            internal static readonly MaterialReferences Instance = new();
            public bool Equals(Material a, Material b) => ReferenceEquals(a, b);
            public int GetHashCode(Material material) => RuntimeHelpers.GetHashCode(material);
        }

        internal HoRTGISecondaryMaterials(Shader shader)
        {
            this.shader = shader != null ? shader : throw new ArgumentNullException(nameof(shader));
        }

        internal void BeginFrame() { seen.Clear(); Rejections.Clear(); Signature = 0; TimeVarying = false; }

        internal Material Resolve(Material source)
        {
            if (!seen.Add(source)) return adapters[source];
            if (!adapters.TryGetValue(source, out var adapter))
            {
                adapter = new Material(shader) { name = "HoRTGI hit: " + source.name, hideFlags = HideFlags.HideAndDontSave };
                adapters.Add(source, adapter);
            }
            // Copy the source values, including texture ST and the dedicated
            // emission groups. The private hit shader keeps its own keywords.
            adapter.CopyPropertiesFromMaterial(source);
            adapter.shaderKeywords = Array.Empty<string>();
            bool supported = IsSupported(source);
            unchecked { Signature ^= MaterialSignature(source); }
            if (supported) TimeVarying |= Animated(source);
            adapter.SetFloat("_HoRTGISecondarySupported", supported ? 1 : 0);
            if (!supported) Rejections.Add(source.name + ": secondary material not supported; remains opaque to RT.");
            return adapter;
        }

        internal static bool IsSupported(Material source) => source != null && source.shader != null
            && source.shader.name == "lilToon" && source.renderQueue < (int)RenderQueue.AlphaTest
            && !Enabled(source, "_UseMain2ndTex") && !Enabled(source, "_UseMain3rdTex")
            && !Enabled(source, "_UseParallax");

        private static bool Enabled(Material material, string property) => material.HasProperty(property) && material.GetFloat(property) != 0;

        // A conservative content signature for GI radiance history, not a second
        // material representation. Texture contents and ST participate as well.
        private static int MaterialSignature(Material source)
        {
            unchecked
            {
                int hash = source.GetInstanceID() * 397 ^ source.shader.GetInstanceID();
                for (int i = 0; i < source.shader.GetPropertyCount(); i++)
                {
                    int id = source.shader.GetPropertyNameId(i);
                    switch (source.shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: hash = hash * 397 ^ source.GetColor(id).GetHashCode(); break;
                        case ShaderPropertyType.Vector: hash = hash * 397 ^ source.GetVector(id).GetHashCode(); break;
                        case ShaderPropertyType.Texture:
                            var texture = source.GetTexture(id);
                            hash = hash * 397 ^ (texture != null ? texture.GetInstanceID() ^ (int)texture.updateCount : 0);
                            if (texture != null) { hash = hash * 397 ^ (int)texture.filterMode; hash = hash * 397 ^ (int)texture.wrapModeU; hash = hash * 397 ^ (int)texture.wrapModeV; }
                            hash = hash * 397 ^ source.GetTextureScale(id).GetHashCode();
                            hash = hash * 397 ^ source.GetTextureOffset(id).GetHashCode(); break;
                        case ShaderPropertyType.Int: hash = hash * 397 ^ source.GetInteger(id); break;
                        default: hash = hash * 397 ^ source.GetFloat(id).GetHashCode(); break;
                    }
                }
                return hash;
            }
        }
        private static bool Animated(Material material)
        {
            foreach (string name in new[] { "_MainTex_ScrollRotate", "_EmissionMap_ScrollRotate", "_EmissionBlendMask_ScrollRotate", "_Emission2ndMap_ScrollRotate", "_Emission2ndBlendMask_ScrollRotate" })
                if (material.HasProperty(name)) { var v = material.GetVector(name); if (v.x != 0 || v.y != 0 || v.w != 0) return true; }
            foreach (string name in new[] { "_EmissionBlink", "_Emission2ndBlink" })
                if (material.HasProperty(name) && material.GetVector(name).x != 0) return true;
            return Enabled(material, "_EmissionUseGrad") || Enabled(material, "_Emission2ndUseGrad");
        }

        // Call after the scene registry has removed its obsolete instances.
        internal void EndFrame()
        {
            removed.Clear();
            foreach (var pair in adapters) if (pair.Key == null || !seen.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var key in removed) { CoreUtils.Destroy(adapters[key]); adapters.Remove(key); }
        }

        public void Dispose()
        {
            foreach (var material in adapters.Values) CoreUtils.Destroy(material);
            adapters.Clear(); seen.Clear(); removed.Clear(); Rejections.Clear();
        }
    }
}
