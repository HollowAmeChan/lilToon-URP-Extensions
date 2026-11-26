using lilToon.URP.Extensions.RT;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.RTGI
{
    // GI-only closest-hit materials. Source materials and their raster passes
    // are never modified. Unsupported opaque surfaces remain occluders.
    internal sealed class HoRTGISecondaryMaterials : IHoRTMaterialProfile
    {
        private readonly Shader shader;
        private readonly Dictionary<Material, Material> adapters = new(MaterialReferences.Instance);
        private readonly HashSet<Material> seen = new(MaterialReferences.Instance);
        private readonly List<Material> removed = new();
        internal readonly List<string> Rejections = new();
        internal int Signature { get; private set; }
        internal bool TimeVarying { get; private set; }
        int IHoRTMaterialProfile.Signature => Signature;
        void IHoRTMaterialProfile.BeginPrepare() => BeginFrame();
        Material IHoRTMaterialProfile.Resolve(Material source) => Resolve(source);
        bool IHoRTMaterialProfile.SupportsCutout(Material source) => HoRTCutoutSupport.Supports(source);
        void IHoRTMaterialProfile.EndPrepare() => EndFrame();
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
            adapter.CopyPropertiesFromMaterial(source);
            adapter.shaderKeywords = Array.Empty<string>();
            var family = HoRTMaterialFamilies.Get(source);
            bool cutout = HoRTCutoutSupport.Supports(source);
            bool supported = IsSupported(source, family, cutout);
            if (supported || cutout) ConfigureSurface(source, adapter, family);
            adapter.SetKeyword(new LocalKeyword(shader, "_HO_RT_TENSION"), supported && Float(adapter, "_HoRTTensionEnabled") > .5f);
            unchecked
            {
                int derived = Float(adapter, "_HoRTNormalEnabled").GetHashCode();
                derived = derived * 397 ^ Float(adapter, "_HoRTEmissionMode").GetHashCode();
                derived = derived * 397 ^ Float(adapter, "_HoRTTensionEnabled").GetHashCode();
                Signature ^= MaterialSignature(source) * 397 ^ derived ^ (int)family ^ supported.GetHashCode() ^ cutout.GetHashCode();
            }
            if (supported && family == HoRTMaterialFamily.LegacyLilToon) TimeVarying |= Animated(source);
            adapter.SetFloat("_HoRTGISecondarySupported", supported ? 1 : 0);
            adapter.SetFloat("_HoRTGICutout", cutout ? 1 : 0);
            if (!supported) Rejections.Add(source.name + ": no matching secondary surface contract; remains an opaque occluder.");
            return adapter;
        }

        internal static bool IsSupported(Material source) => IsSupported(source, HoRTMaterialFamilies.Get(source), HoRTCutoutSupport.Supports(source));

        private static bool IsSupported(Material source, HoRTMaterialFamily family, bool cutout)
        {
            if (source == null || source.shader == null || family == HoRTMaterialFamily.Unsupported) return false;
            if (family == HoRTMaterialFamily.Environment && Enabled(source, "_Surface")) return false;
            if ((family == HoRTMaterialFamily.Cloth || family == HoRTMaterialFamily.Hair || family == HoRTMaterialFamily.Eye)
                && Float(source, "_SurfaceMode") > 1.5f) return false;
            bool opaque = source.renderQueue < (int)RenderQueue.AlphaTest && source.GetTag("RenderType", false) != "Transparent"
                && source.GetTag("RenderType", false) != "TransparentCutout" && !source.IsKeywordEnabled("_ALPHATEST_ON");
            return (opaque || cutout) && (family != HoRTMaterialFamily.LegacyLilToon || !Enabled(source, "_UseParallax"));
        }

        private static void ConfigureSurface(Material source, Material target, HoRTMaterialFamily family)
        {
            HoRTMaterialFamilies.CopyBaseAndAlpha(source, target);
            bool legacy = family == HoRTMaterialFamily.LegacyLilToon;
            bool cloth = family == HoRTMaterialFamily.Cloth;
            bool environment = family == HoRTMaterialFamily.Environment;
            bool skin = family == HoRTMaterialFamily.Skin;
            bool hair = family == HoRTMaterialFamily.Hair;
            bool smoothnessMapFamily = skin || family == HoRTMaterialFamily.Eye;
            target.SetFloat("_HoRTMaterialFamily", (float)family);
            target.SetVector("_HoRTColorAdjustment", cloth ? source.GetVector("_ColorAdjustment") : new Vector4(0, 1, 1, 1));
            target.SetFloat("_Metallic", legacy || cloth || environment ? Float(source, "_Metallic") : 0);
            target.SetFloat("_Reflectance", hair ? 0 : skin ? .04f * Mathf.Clamp(Float(source, "_SpecularStrength", .8f), 0, 2)
                : family == HoRTMaterialFamily.Eye ? .04f : Float(source, "_Reflectance", .04f));
            target.SetFloat("_Smoothness", hair ? 0 : Float(source, "_Smoothness", .5f));
            int input = legacy || environment || cloth ? (Float(source, "_PBRInputMode") > .5f ? 2 : 1) : 0;
            Vector4 flags = cloth ? source.GetVector("_PBRMapFlags") : new Vector4(1, 1, 1, 1);
            if (cloth && input == 2 && flags.x < .5f) input = 0;
            if (smoothnessMapFamily && Float(source, "_UseSmoothnessMap") > .5f) { input = 1; flags = new Vector4(0, 0, 0, 1); }
            target.SetFloat("_HoRTPBRMode", input);
            target.SetVector("_HoRTPBRFlags", flags);
            target.SetFloat("_HoRTMetallicMapScale", cloth ? Float(source, "_MetallicMapScale", 1) : Float(source, "_Metallic"));
            target.SetFloat("_HoRTSmoothnessMapScale", cloth || smoothnessMapFamily ? Float(source, "_SmoothnessMapScale", 1) : Float(source, "_Smoothness", .5f));
            foreach (string property in new[] { "_PBRMap", "_MetallicGlossMap", "_ReflectanceTex", "_SmoothnessTex" })
            {
                string from = smoothnessMapFamily && property == "_SmoothnessTex" ? "_SmoothnessMap" : property;
                HoRTMaterialFamilies.CopyTexture(source, from, target, property);
                if (legacy) ComposeMainUV(source, target, property);
            }
            HoRTMaterialFamilies.CopyTexture(source, "_BumpMap", target, "_BumpMap");
            if (legacy) ComposeMainUV(source, target, "_BumpMap");
            if (skin || family == HoRTMaterialFamily.Eye)
            {
                target.SetTextureScale("_BumpMap", source.GetTextureScale("_BaseMap"));
                target.SetTextureOffset("_BumpMap", source.GetTextureOffset("_BaseMap"));
            }
            target.SetFloat("_BumpScale", Float(source, "_BumpScale", 1));
            target.SetFloat("_HoRTNormalEnabled", !environment || source.IsKeywordEnabled("_NORMALMAP") ? 1 : 0);
            target.SetFloat("_HoRTNormalPolicy", legacy ? 0 : hair ? 3 : 1);
            target.SetFloat("_FlipNormal", skin || hair || family == HoRTMaterialFamily.Eye ? 1 : Float(source, "_FlipNormal"));
            bool emission = environment
                ? source.IsKeywordEnabled("_EMISSION") && (!source.HasProperty("_UseEmission") || Enabled(source, "_UseEmission"))
                : Enabled(source, "_UseEmission");
            foreach (string property in new[] { "_EmissionMap", "_EmissionBlendMask", "_EmissionGradTex",
                "_HoTesionStretchBaseColor", "_HoTesionCompressionBaseColor", "_HoTesionStretchNormal", "_HoTesionCompressionNormal" })
                HoRTMaterialFamilies.CopyTexture(source, property, target, property);
            target.SetFloat("_HoRTEmissionMode", !emission ? 0 : legacy ? 2 : skin || family == HoRTMaterialFamily.Eye ? 3 : 1);
            target.SetFloat("_HoRTTensionEnabled", (cloth || skin) && Enabled(source, "_HoTesionEnabled")
                && source.IsKeywordEnabled("_HO_GD_TENSION") ? 1 : 0);
            // Reset fields absent from the new source shader when an existing material changes family.
            target.SetFloat("_EmissionMainStrength", Float(source, "_EmissionMainStrength"));
        }

        private static void ComposeMainUV(Material source, Material target, string map)
        {
            Vector2 scale = source.GetTextureScale("_MainTex"), offset = source.GetTextureOffset("_MainTex");
            Vector2 mapScale = target.GetTextureScale(map), mapOffset = target.GetTextureOffset(map);
            target.SetTextureScale(map, Vector2.Scale(scale, mapScale));
            target.SetTextureOffset(map, Vector2.Scale(offset, mapScale) + mapOffset);
        }

        private static float Float(Material material, string property, float fallback = 0) => material.HasProperty(property) ? material.GetFloat(property) : fallback;
        private static bool Enabled(Material material, string property) => Float(material, property) != 0;

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
            // Disabled emission groups do not execute in the hit shader.
            // Dormant animation settings must not reset every camera's GI/NRD
            // history on an otherwise static material.
            return Enabled(material, "_UseEmission") &&
                    (Scrolls(material, "_EmissionMap_ScrollRotate") || Scrolls(material, "_EmissionBlendMask_ScrollRotate")
                     || Blinks(material, "_EmissionBlink") || Enabled(material, "_EmissionUseGrad"));
        }
        private static bool Scrolls(Material material, string name)
        {
            if (!material.HasProperty(name)) return false;
            var value = material.GetVector(name);
            return value.x != 0 || value.y != 0 || value.w != 0;
        }
        private static bool Blinks(Material material, string name) =>
            material.HasProperty(name) && material.GetVector(name).x != 0;

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
