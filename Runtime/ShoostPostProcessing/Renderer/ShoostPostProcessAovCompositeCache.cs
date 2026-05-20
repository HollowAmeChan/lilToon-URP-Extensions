using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.PostProcessing
{
    internal sealed class ShoostPostProcessAovCompositeCache : IDisposable
    {
        private Material aovCompositeMaterial;
        private Shader aovCompositeShader;
        private bool warnedMissingAovCompositeShader;

        public Material Ensure(List<ShoostPostProcessRuntimeLayer> layers)
        {
            if (!ShoostPostProcessAovSupport.ContainsAovMaskedLayer(layers))
            {
                return null;
            }

            Shader shader = Shader.Find(ShoostPostProcessShaderConstants.AovCompositeShaderName);
            if (aovCompositeMaterial != null && aovCompositeShader == shader)
            {
                return aovCompositeMaterial;
            }

            if (shader == null)
            {
                if (!warnedMissingAovCompositeShader)
                {
                    warnedMissingAovCompositeShader = true;
                    Debug.LogWarning($"Shoost AOV 遮罩已跳过：找不到 Shader '{ShoostPostProcessShaderConstants.AovCompositeShaderName}'。");
                }

                return null;
            }

            CoreUtils.Destroy(aovCompositeMaterial);
            aovCompositeShader = shader;
            aovCompositeMaterial = CoreUtils.CreateEngineMaterial(shader);
            return aovCompositeMaterial;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(aovCompositeMaterial);
            aovCompositeMaterial = null;
            aovCompositeShader = null;
            warnedMissingAovCompositeShader = false;
        }
    }
}