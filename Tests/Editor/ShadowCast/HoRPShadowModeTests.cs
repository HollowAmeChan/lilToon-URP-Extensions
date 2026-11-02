using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using lilToon.URP.Extensions.ShadowCast;
using Object = UnityEngine.Object;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoRPShadowModeTests
    {
        [Test]
        public void GeneratedLilToonVariantsExposePersistentMainDefault()
        {
            int checkedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.lilxyzw.liltoon/Shader" }))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader == null || shader.FindPropertyIndex("_MultiLightIntensity") < 0) continue;
                var material = new Material(shader);
                try
                {
                    Assert.IsTrue(material.HasProperty("_HoRPShadowMode"), shader.name);
                    Assert.AreEqual(1f, material.GetFloat("_HoRPShadowMode"), shader.name);
                    checkedCount++;
                }
                finally { Object.DestroyImmediate(material); }
            }
            Assert.Greater(checkedCount, 0, "No lilToon shaders were tested.");
        }

        [TestCase(0, 0, 1f)]
        [TestCase(0, 2, .35f)]
        [TestCase(1, 0, 1f)]
        [TestCase(1, 1, 1f)]
        [TestCase(2, 0, .35f)]
        [TestCase(2, 1, .35f)]
        [TestCase(1, 2, .35f)]
        [TestCase(1, 3, .35f)]
        [TestCase(2, 2, .35f)]
        [TestCase(2, 3, .35f)]
        public void GPUReceiverModeFiltersOnlyTheRequestedPool(int mode, int lightFlags, float expected)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            var shader = Shader.Find("Hidden/HoRP/Tests/ShadowModeProbe");
            Assert.IsNotNull(shader);
            var material = new Material(shader);
            var depth = new Texture2D(2, 2, TextureFormat.RFloat, false, true);
            var target = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var result = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            var restore = new List<Action>();
            var previousTarget = RenderTexture.active;
            void Float(string name, float value)
            {
                float old = Shader.GetGlobalFloat(name);
                restore.Add(() => Shader.SetGlobalFloat(name, old));
                Shader.SetGlobalFloat(name, value);
            }
            void Vector(string name, Vector4 value)
            {
                Vector4 old = Shader.GetGlobalVector(name);
                restore.Add(() => Shader.SetGlobalVector(name, old));
                Shader.SetGlobalVector(name, value);
            }
            void Int(string name, int value)
            {
                int old = Shader.GetGlobalInt(name);
                restore.Add(() => Shader.SetGlobalInt(name, old));
                Shader.SetGlobalInt(name, value);
            }
            void Array(string name, Vector4 first, int size)
            {
                var old = Shader.GetGlobalVectorArray(name) ?? new Vector4[0];
                var values = new Vector4[Math.Max(size, old.Length)];
                values[0] = first;
                restore.Add(() => Shader.SetGlobalVectorArray(name, old.Length > 0 ? old : new Vector4[values.Length]));
                Shader.SetGlobalVectorArray(name, values);
            }
            Texture previousDepth = Shader.GetGlobalTexture("_HoShadowCastAtlas");
            try
            {
                // Depth=1 is this atlas contract's empty sentinel, even under reversed Z.
                float occluder = SystemInfo.usesReversedZBuffer ? .75f : .25f;
                depth.SetPixels(Enumerable.Repeat(new Color(occluder, 0, 0, 1), 4).ToArray());
                depth.Apply();
                Shader.SetGlobalTexture("_HoShadowCastAtlas", depth);
                Float("_HoShadowCastActive", 1);
                Int("_HoShadowCastLightCount", 1);
                Int("_HoShadowCastSliceCount", 1);
                Vector("_HoShadowCastSecondDirectionalParams", Vector4.zero);
                Vector("_HoShadowCastAtlasSize", new Vector4(2, 2, .5f, .5f));
                Vector("_HoShadowCastPcssParams", Vector4.zero);
                Vector("_HoShadowCastPcssParams2", Vector4.zero);
                Array("_HoShadowCastLightData0", new Vector4(0, 0, 1, 1), HoShadowCastShaderContract.ArrayLights);
                Array("_HoShadowCastLightColor", new Vector4(1, 1, 1, lightFlags), HoShadowCastShaderContract.ArrayLights);
                Array("_HoShadowCastSliceData", new Vector4(0, 0, 1, 0), HoShadowCastShaderContract.ArraySlices);
                Array("_HoShadowCastWorldToShadowRow0", new Vector4(0, 0, 0, .5f), HoShadowCastShaderContract.ArraySlices);
                Array("_HoShadowCastWorldToShadowRow1", new Vector4(0, 0, 0, .5f), HoShadowCastShaderContract.ArraySlices);
                Array("_HoShadowCastWorldToShadowRow2", new Vector4(0, 0, 0, .5f), HoShadowCastShaderContract.ArraySlices);
                Array("_HoShadowCastWorldToShadowRow3", new Vector4(0, 0, 0, 1), HoShadowCastShaderContract.ArraySlices);
                material.SetFloat("_HoRPShadowMode", mode);
                Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target;
                result.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
                result.Apply();
                Assert.That(result.GetPixel(0, 0).r, Is.EqualTo(expected).Within(.005f));
                var diagnostics = ShaderUtil.GetShaderMessages(shader)
                    .Where(message => message.severity.ToString() == "Warning" ||
                                      message.severity.ToString() == "Error")
                    .Select(message => message.message);
                Assert.IsEmpty(diagnostics, "Receiver shader must compile without warnings or errors.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                foreach (var action in restore) action();
                Shader.SetGlobalTexture("_HoShadowCastAtlas", previousDepth);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(depth);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(result);
            }
        }
    }
}
