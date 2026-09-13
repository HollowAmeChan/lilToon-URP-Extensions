using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using lilToon.URP.Extensions.ShadowCast;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.ShadowCast
{
    /// <summary>
    /// Keeps <c>Runtime/ShadowCast/HoShadowCastShaderContract.cs</c> and
    /// <c>Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl</c> in sync.
    ///
    /// The compiler cannot relate C# constants to shader macros, so this validator parses the shader
    /// side and compares it with the C# side. It runs on editor load (silent while both sides agree)
    /// and on demand from the menu.
    /// </summary>
    internal static class HoShadowCastShaderContractValidator
    {
        private const string ContractFileName = "HoShadowCastShaderContract.hlsl";
        private const string SamplingFileName = "HoShadowCastSampling.hlsl";
        private const string DebugShaderFileName = "HoShadowCastDebug.shader";
        private const string DebugTileShaderFileName = "HoDebugTile.shader";
        private const string LogPrefix = "[lilToon URP Extensions] ShadowCast shader contract: ";

        private static readonly Regex DefinePattern = new Regex(
            @"^[ \t]*#define[ \t]+([A-Za-z_][A-Za-z0-9_]*)[ \t]+([^\s/]+)[ \t]*(?://.*)?$",
            RegexOptions.Multiline);

        private static readonly Regex WhitespacePattern = new Regex(@"\s+");

        // Only ShadowCast globals are matched, so unrelated literal arrays (blit quad UVs, etc.) are not
        // flagged.
        private static readonly Regex LiteralArrayDeclarationPattern = new Regex(
            @"\b(?:float|float2|float3|float4|float4x4|half|half2|half3|half4|int|uint|bool|matrix)\s+_HoShadowCast\w+\s*\[\s*\d+\s*\]");

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            // Deferred so the AssetDatabase is ready after a domain reload or package import.
            EditorApplication.delayCall += () => Validate(false);
        }

        [MenuItem("Tools/lilToon URP Extensions/Validate ShadowCast Shader Contract")]
        private static void ValidateFromMenu()
        {
            Validate(true);
        }

        internal static bool Validate(bool verbose)
        {
            List<string> errors = new List<string>();
            string contractPath = FindAssetPath(ContractFileName);
            if (string.IsNullOrEmpty(contractPath))
            {
                Debug.LogWarning(LogPrefix + ContractFileName + " was not found in the project, validation skipped.");
                return false;
            }

            string contractText;
            try
            {
                contractText = File.ReadAllText(contractPath);
            }
            catch (IOException exception)
            {
                Debug.LogWarning(LogPrefix + "could not read " + contractPath + ": " + exception.Message);
                return false;
            }

            Dictionary<string, float> defines = ParseDefines(contractText);
            string normalized = WhitespacePattern.Replace(contractText, " ");

            CheckStorageValues(defines, errors);
            CheckTierValues(defines, errors);
            CheckTierResolution(normalized, errors);
            CheckTierBounds(errors);
            CheckPcssSampleCounts(errors);
            CheckSamplingHeader(errors);
            CheckDebugShaders(errors);

            if (errors.Count == 0)
            {
                if (verbose)
                {
                    Debug.Log(LogPrefix + "OK. " + ContractFileName + " matches HoShadowCastShaderContract.cs.");
                }

                return true;
            }

            for (int i = 0; i < errors.Count; i++)
            {
                Debug.LogError(LogPrefix + errors[i]);
            }

            return false;
        }

        private static void CheckStorageValues(Dictionary<string, float> defines, List<string> errors)
        {
            CheckValue(defines, "HO_SHADOW_CAST_ARRAY_LIGHTS", HoShadowCastShaderContract.ArrayLights, errors);
            CheckValue(defines, "HO_SHADOW_CAST_ARRAY_SLICES", HoShadowCastShaderContract.ArraySlices, errors);
            CheckValue(defines, "HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_LIGHTS", HoShadowCastShaderContract.SecondDirectionalLights, errors);
            CheckValue(defines, "HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_CASCADES", HoShadowCastShaderContract.SecondDirectionalCascades, errors);
            CheckValue(defines, "HO_SHADOW_CAST_MAX_SECOND_DIRECTIONAL_SLICES", HoShadowCastShaderContract.SecondDirectionalSlices, errors);
            CheckValue(defines, "HO_SHADOW_CAST_MAX_PCSS_BLOCKER_SAMPLES", HoShadowCastShaderContract.PcssBlockerSamples, errors);
            CheckValue(defines, "HO_SHADOW_CAST_MAX_PCSS_FILTER_SAMPLES", HoShadowCastShaderContract.PcssFilterSamples, errors);
            CheckValue(defines, "HO_SHADOW_CAST_LIGHT_DIRECTIONAL", HoShadowCastShaderContract.LightTypeIdDirectional, errors);
            CheckValue(defines, "HO_SHADOW_CAST_LIGHT_SPOT", HoShadowCastShaderContract.LightTypeIdSpot, errors);
            CheckValue(defines, "HO_SHADOW_CAST_LIGHT_POINT", HoShadowCastShaderContract.LightTypeIdPoint, errors);
        }

        private static void CheckTierValues(Dictionary<string, float> defines, List<string> errors)
        {
            CheckValue(defines, "HO_SHADOW_CAST_CAPACITY_LOW_LIGHTS", HoShadowCastShaderContract.LowLights, errors);
            CheckValue(defines, "HO_SHADOW_CAST_CAPACITY_MEDIUM_LIGHTS", HoShadowCastShaderContract.MediumLights, errors);
            CheckValue(defines, "HO_SHADOW_CAST_CAPACITY_HIGH_LIGHTS", HoShadowCastShaderContract.HighLights, errors);
        }

        private static void CheckTierResolution(string normalized, List<string> errors)
        {
            CheckContains(ContractFileName, normalized, "#if defined(HO_SHADOW_CAST_CAPACITY_HIGH)", errors);
            CheckContains(ContractFileName, normalized, "#define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_HIGH_LIGHTS", errors);
            CheckContains(ContractFileName, normalized, "#elif defined(HO_SHADOW_CAST_CAPACITY_MEDIUM)", errors);
            CheckContains(ContractFileName, normalized, "#define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_MEDIUM_LIGHTS", errors);
            CheckContains(ContractFileName, normalized, "#define HO_SHADOW_CAST_CAPACITY_LIGHTS HO_SHADOW_CAST_CAPACITY_LOW_LIGHTS", errors);

            // Slices are deliberately not tiered: they follow the atlas geometry.
            if (normalized.IndexOf("HO_SHADOW_CAST_CAPACITY_SLICES", StringComparison.Ordinal) >= 0)
            {
                errors.Add(ContractFileName + " must not declare tiered slice capacity (HO_SHADOW_CAST_CAPACITY_SLICES); slices follow the atlas geometry.");
            }
        }

        /// <summary>
        /// The array lengths are fixed for the session (Unity cannot grow a global array property), so a
        /// tier larger than the arrays would collect lights the publisher could never upload.
        /// </summary>
        private static void CheckTierBounds(List<string> errors)
        {
            HoShadowCastLightCapacity[] tiers = (HoShadowCastLightCapacity[])Enum.GetValues(typeof(HoShadowCastLightCapacity));
            for (int i = 0; i < tiers.Length; i++)
            {
                HoShadowCastCapacityLimits limits = HoShadowCastShaderContract.GetLimits(tiers[i]);
                if (limits.LightCount > HoShadowCastShaderContract.ArrayLights || limits.SliceCount > HoShadowCastShaderContract.ArraySlices)
                {
                    errors.Add(
                        "tier " + tiers[i] + " (" + limits.LightCount + " lights / " + limits.SliceCount + " slices) exceeds the fixed array sizes (" +
                        HoShadowCastShaderContract.ArrayLights + " lights / " + HoShadowCastShaderContract.ArraySlices + " slices).");
                }
            }
        }

        private static void CheckPcssSampleCounts(List<string> errors)
        {
            HoShadowCastPcssQuality[] qualities = (HoShadowCastPcssQuality[])Enum.GetValues(typeof(HoShadowCastPcssQuality));
            for (int i = 0; i < qualities.Length; i++)
            {
                HoShadowCastShaderContract.GetPcssSampleCounts(qualities[i], out int blockerSamples, out int filterSamples);
                if (blockerSamples > HoShadowCastShaderContract.PcssBlockerSamples || filterSamples > HoShadowCastShaderContract.PcssFilterSamples)
                {
                    errors.Add(
                        "PCSS quality " + qualities[i] + " requests " + blockerSamples + "/" + filterSamples +
                        " samples, above the shader ceilings " + HoShadowCastShaderContract.PcssBlockerSamples + "/" +
                        HoShadowCastShaderContract.PcssFilterSamples + ".");
                }
            }
        }

        /// <summary>
        /// The sampling header must consume the contract instead of re-declaring numeric array/capacity
        /// macros (that is how the limits drifted apart in the first place) and is the file that has to
        /// declare the tier keywords, because it is the one with the tier bounded loops.
        /// </summary>
        private static void CheckSamplingHeader(List<string> errors)
        {
            string path = FindAssetPath(SamplingFileName);
            if (string.IsNullOrEmpty(path))
            {
                errors.Add(SamplingFileName + " was not found in the project.");
                return;
            }

            string text = ReadAllText(path, errors);
            if (text == null)
            {
                return;
            }

            if (text.IndexOf(ContractFileName, StringComparison.Ordinal) < 0)
            {
                errors.Add(SamplingFileName + " must include " + ContractFileName + " instead of declaring capacity limits itself.");
            }

            string normalized = WhitespacePattern.Replace(text, " ");
            CheckContains(SamplingFileName, normalized, "#pragma multi_compile", errors);
            CheckContains(SamplingFileName, normalized, HoShadowCastShaderContract.MediumKeywordName, errors);
            CheckContains(SamplingFileName, normalized, HoShadowCastShaderContract.HighKeywordName, errors);

            Dictionary<string, float> defines = ParseDefines(text);
            foreach (KeyValuePair<string, float> define in defines)
            {
                if (define.Key.StartsWith("HO_SHADOW_CAST_ARRAY_", StringComparison.Ordinal)
                    || define.Key.StartsWith("HO_SHADOW_CAST_CAPACITY_", StringComparison.Ordinal))
                {
                    errors.Add(SamplingFileName + " re-declares " + define.Key + " as a literal (" + define.Value.ToString(CultureInfo.InvariantCulture) + "); it must come from " + ContractFileName + ".");
                }
            }
        }

        private static void CheckDebugShaders(List<string> errors)
        {
            CheckDebugShader(DebugShaderFileName, "its arrays use the shared sizes", errors);
            CheckDebugShader(DebugTileShaderFileName, "its slice overlay uses the shared sizes", errors);
        }

        private static void CheckDebugShader(string fileName, string includeReason, List<string> errors)
        {
            string path = FindAssetPath(fileName);
            if (string.IsNullOrEmpty(path))
            {
                errors.Add(fileName + " was not found in the project.");
                return;
            }

            string text = ReadAllText(path, errors);
            if (text == null)
            {
                return;
            }

            if (text.IndexOf(ContractFileName, StringComparison.Ordinal) < 0)
            {
                errors.Add(fileName + " must include " + ContractFileName + " so " + includeReason + ".");
            }

            // Debug views draw with runtime counts and the fixed array sizes, so they must not use the
            // tier macros: they do not declare the tier keywords and would silently fall back to Low.
            if (text.IndexOf("HO_SHADOW_CAST_CAPACITY_", StringComparison.Ordinal) >= 0)
            {
                errors.Add(fileName + " must not depend on the capacity tier; use the runtime counts and the HO_SHADOW_CAST_ARRAY_* sizes.");
            }

            Match literalArray = LiteralArrayDeclarationPattern.Match(text);
            if (literalArray.Success)
            {
                errors.Add(fileName + " declares a hard-coded array size (" + literalArray.Value.Trim() + "); use the HO_SHADOW_CAST_* contract macros.");
            }
        }

        private static string ReadAllText(string path, List<string> errors)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException exception)
            {
                errors.Add("could not read " + path + ": " + exception.Message);
                return null;
            }
        }

        private static Dictionary<string, float> ParseDefines(string text)
        {
            Dictionary<string, float> defines = new Dictionary<string, float>(StringComparer.Ordinal);
            Match match = DefinePattern.Match(text);
            while (match.Success)
            {
                // Alias defines (value is another macro name) do not parse as numbers and are skipped.
                if (float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    defines[match.Groups[1].Value] = value;
                }

                match = match.NextMatch();
            }

            return defines;
        }

        private static void CheckValue(Dictionary<string, float> defines, string name, float expected, List<string> errors)
        {
            if (!defines.TryGetValue(name, out float actual))
            {
                errors.Add(name + " is missing from " + ContractFileName + " (expected " + expected.ToString(CultureInfo.InvariantCulture) + ").");
                return;
            }

            if (Mathf.Abs(actual - expected) > 0.0001f)
            {
                errors.Add(
                    name + " is " + actual.ToString(CultureInfo.InvariantCulture) + " in " + ContractFileName +
                    " but " + expected.ToString(CultureInfo.InvariantCulture) + " in HoShadowCastShaderContract.cs.");
            }
        }

        private static void CheckContains(string fileName, string normalizedText, string expected, List<string> errors)
        {
            if (normalizedText.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                errors.Add(fileName + " is missing the expected declaration: " + expected);
            }
        }

        private static string FindAssetPath(string fileName)
        {
            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName));
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path) && path.EndsWith("/" + fileName, StringComparison.Ordinal))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
