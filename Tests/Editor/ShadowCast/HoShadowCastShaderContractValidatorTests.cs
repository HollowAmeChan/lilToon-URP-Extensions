using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using lilToon.URP.Extensions.ShadowCast;

namespace lilToon.URP.Extensions.Tests
{
    public sealed class HoShadowCastShaderContractValidatorTests
    {
        static readonly Type Validator = Type.GetType(
            "lilToon.URP.Extensions.Editor.ShadowCast.HoShadowCastShaderContractValidator, jp.lilxyzw.liltoon.urp.extensions.Editor", true);

        static Dictionary<string, float> Parse(string text)
        {
            var method = Validator.GetMethod("ParseDefines", BindingFlags.NonPublic | BindingFlags.Static);
            return (Dictionary<string, float>)method.Invoke(null, new object[] { text });
        }

        [TestCase("\n"), TestCase("\r\n")]
        public void NumericDefinesAcceptLineEndingsWhitespaceAndComments(string newline)
        {
            string text = string.Join(newline, new[] {
                "#define GUARD",
                "#define HO_INTEGER 48",
                " \t#define HO_SPACED\t128 \t",
                "#define HO_FLOAT 0.25 // comment",
                "#define HO_ALIAS HO_INTEGER",
                ""
            });
            var values = Parse(text);
            Assert.That(values.Count, Is.EqualTo(3));
            Assert.That(values["HO_INTEGER"], Is.EqualTo(48));
            Assert.That(values["HO_SPACED"], Is.EqualTo(128));
            Assert.That(values["HO_FLOAT"], Is.EqualTo(.25f));
            Assert.That(values.ContainsKey("GUARD"), Is.False);
            Assert.That(values.ContainsKey("HO_ALIAS"), Is.False);
        }

        [Test]
        public void FinalDefineDoesNotRequireATrailingNewline()
        {
            Assert.That(Parse("#define HO_VALUE 48")["HO_VALUE"], Is.EqualTo(48));
        }

        [TestCase("\n"), TestCase("\r\n")]
        public void ShippingContractParsesAllNumericLimits(string newline)
        {
            var find = Validator.GetMethod("FindAssetPath", BindingFlags.NonPublic | BindingFlags.Static);
            string path = (string)find.Invoke(null, new object[] { "HoShadowCastShaderContract.hlsl" });
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            string text = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\n", newline);
            var values = Parse(text);
            Assert.That(values.Count, Is.EqualTo(14));
            Assert.That(values["HO_SHADOW_CAST_ARRAY_LIGHTS"], Is.EqualTo(HoShadowCastShaderContract.ArrayLights));
            Assert.That(values["HO_SHADOW_CAST_ARRAY_SLICES"], Is.EqualTo(HoShadowCastShaderContract.ArraySlices));
            Assert.That(values["HO_SHADOW_CAST_ARRAY_ADDITIONAL_LIGHT_MAP"], Is.EqualTo(HoShadowCastShaderContract.AdditionalLightMapSize));
            Assert.That(values["HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES"], Is.EqualTo(HoShadowCastShaderContract.PcssBlockerSamples));
            Assert.That(values["HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES"], Is.EqualTo(HoShadowCastShaderContract.PcssFilterSamples));
            Assert.That(values["HO_SHADOW_CAST_LIGHT_DIRECTIONAL"], Is.EqualTo(HoShadowCastShaderContract.LightTypeIdDirectional));
            Assert.That(values["HO_SHADOW_CAST_LIGHT_SPOT"], Is.EqualTo(HoShadowCastShaderContract.LightTypeIdSpot));
            Assert.That(values["HO_SHADOW_CAST_LIGHT_POINT"], Is.EqualTo(HoShadowCastShaderContract.LightTypeIdPoint));
        }

        [TestCase(false), TestCase(true)]
        public void MissingOrChangedValuesStillProduceAnError(bool missing)
        {
            var values = Parse(missing ? "" : "#define HO_VALUE 49\r\n");
            var errors = new List<string>();
            var check = Validator.GetMethod("CheckValue", BindingFlags.NonPublic | BindingFlags.Static);
            check.Invoke(null, new object[] { values, "HO_VALUE", 48f, errors });
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain(missing ? "is missing" : "is 49"));
        }

        [TestCase("HoShadowCastShaderContract.hlsl", "/ShaderLibrary/Shadow/Receiver/")]
        [TestCase("HoShadowCastSampling.hlsl", "/ShaderLibrary/Shadow/Receiver/")]
        [TestCase("HoShadowCastDebug.shader", "/Runtime/ShadowCast/Shaders/Debug/")]
        public void AssetLookupUsesLibraryHeadersAndRetainsRuntimeDebugEntry(string fileName, string directory)
        {
            var find = Validator.GetMethod("FindAssetPath", BindingFlags.NonPublic | BindingFlags.Static);
            string path = (string)find.Invoke(null, new object[] { fileName });
            Assert.That(path, Is.Not.Null.And.Not.Empty);
            Assert.That(path.Replace('\\', '/'), Does.Contain(directory));
        }

        [Test]
        public void ShippingContractPassesCompleteValidation()
        {
            var validate = Validator.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That((bool)validate.Invoke(null, new object[] { false }), Is.True);
        }
    }
}
