using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ShoostPostProcessMaterialCache : IDisposable
    {
        private readonly Dictionary<Shader, Material> materialCache = new Dictionary<Shader, Material>();
        private readonly HashSet<string> warnedMissingShaders = new HashSet<string>();

        public Material ResolveMaterial(ShoostPostProcessLayer layer, ShoostPostProcessStackSettings settings)
        {
            if (layer.materialOverride != null)
            {
                return layer.materialOverride;
            }

            Shader shader = layer.shaderOverride;
            if (shader == null && layer.effect == ShoostPostProcessEffect.CustomMaterial && settings != null)
            {
                shader = settings.defaultLayerShader;
            }

            string shaderName = ShoostPostProcessEffectRegistry.GetDefaultShaderName(layer.effect);
            if (shader == null)
            {
                shader = Shader.Find(shaderName);
            }

            if (shader == null)
            {
                WarnMissingShader(layer, shaderName);
                return null;
            }

            if (materialCache.TryGetValue(shader, out Material material) && material != null)
            {
                return material;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            materialCache[shader] = material;
            return material;
        }

        public void Dispose()
        {
            foreach (Material material in materialCache.Values)
            {
                CoreUtils.Destroy(material);
            }

            materialCache.Clear();
            warnedMissingShaders.Clear();
        }

        private void WarnMissingShader(ShoostPostProcessLayer layer, string shaderName)
        {
            string key = $"{layer.effect}:{shaderName}";
            if (!warnedMissingShaders.Add(key))
            {
                return;
            }

            Debug.LogWarning($"lilToon-Shoost 后处理图层 '{layer.name}' 已跳过：找不到 Shader '{shaderName}'。");
        }
    }
}