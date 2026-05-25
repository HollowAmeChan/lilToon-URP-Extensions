#pragma warning disable CS0618

using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.ShadowCast
{
    internal sealed class HoShadowCastDebugMaterial
    {
        private Material material;
        private Shader shader;
        private bool warnedMissingShader;

        public Material Material => material;

        public bool Ensure()
        {
            if (material != null)
            {
                return true;
            }

            if (shader == null)
            {
                shader = Shader.Find(HoShadowCastShaderConstants.DebugShaderName);
            }

            if (shader == null)
            {
                if (!warnedMissingShader)
                {
                    Debug.LogWarning("[lilToon] HoShadowCast debug shader not found: " + HoShadowCastShaderConstants.DebugShaderName);
                    warnedMissingShader = true;
                }

                return false;
            }

            material = CoreUtils.CreateEngineMaterial(shader);
            return material != null;
        }

        public void Release()
        {
            CoreUtils.Destroy(material);
            material = null;
            shader = null;
        }
    }
}
