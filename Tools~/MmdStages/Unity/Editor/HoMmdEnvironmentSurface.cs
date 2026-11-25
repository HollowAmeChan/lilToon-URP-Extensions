using System;
using UnityEngine;

internal static class HoMmdEnvironmentSurface
{
    private const string EnvironmentShader = "HoShader/Environment URP";
    private const string EnvironmentEditor = "HoShader.EnvironmentURP.Editor.EnvironmentShaderGUI, HoShader.EnvironmentURP.Editor";
    private static readonly Lazy<Action<Material>> Sync = new(ResolveSynchronizer);

    internal static bool Available => Shader.Find(EnvironmentShader) != null;

    internal static Shader FindSky(string environmentName, string legacyName)
    {
        return Shader.Find(environmentName) ?? Shader.Find(legacyName);
    }

    internal static Material CreateStageSky(string name, Color top, Color bottom, float exposure)
    {
        Shader skyShader = FindSky("HoShader/Environment Gradient Sky", "Skybox/Procedural");
        if (skyShader == null)
            throw new InvalidOperationException("没有可用的环境天空 Shader。");
        var sky = new Material(skyShader) { name = name };
        if (sky.HasProperty("_Top"))
        {
            sky.SetColor("_Top", top);
            sky.SetColor("_Bottom", bottom);
        }
        else
        {
            sky.SetColor("_SkyTint", top);
            sky.SetColor("_GroundColor", bottom);
        }
        sky.SetFloat("_Exposure", exposure);
        return sky;
    }

    internal static bool IsEnvironment(Material material)
    {
        return material != null && material.GetTag("HoEnvironmentSurface", false) == "1";
    }

    internal static Material Create(string name, Color color, bool shadowOnly = false)
    {
        Shader shader = Shader.Find(EnvironmentShader) ?? Shader.Find("lilToon");
        if (shader == null)
            throw new InvalidOperationException("没有可用的环境材质 Shader。");

        var material = new Material(shader) { name = name };
        SetColor(material, color);
        if (IsEnvironment(material))
        {
            material.SetFloat("_SpecularHighlights", 0);
            if (shadowOnly)
            {
                material.SetFloat("_Cull", 0);
                material.SetFloat("_ReceiveShadows", 0);
            }
            Sync.Value(material);
        }
        return material;
    }

    internal static void SetColor(Material material, Color color)
    {
        material.SetColor(IsEnvironment(material) ? "_BaseColor" : "_Color", color);
    }

    internal static void Configure(Material material, bool reflectiveMetal, bool glass, float glassAlpha, Color emission)
    {
        material.SetFloat("_SpecularHighlights", reflectiveMetal || glass ? 1 : 0);
        material.SetFloat("_Metallic", reflectiveMetal ? 0.55f : 0);
        material.SetFloat("_Smoothness", glass ? 0.82f : reflectiveMetal ? 0.58f : 0.28f);
        material.SetFloat("_Reflectance", 0.035f);
        if (glass)
        {
            Color color = material.GetColor("_BaseColor");
            color.a = Mathf.Min(color.a, glassAlpha);
            SetColor(material, color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", 0.001f);
            material.SetFloat("_Blend", 1);
            material.SetFloat("_AlphaToMask", 0);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_CastShadows", 0);
            material.SetFloat("_EnvironmentReflections", 1);
        }
        if (emission.maxColorComponent > 0)
            material.SetColor("_EmissionColor", emission);

        Sync.Value(material);
    }

    private static Action<Material> ResolveSynchronizer()
    {
        Type type = Type.GetType(EnvironmentEditor);
        var method = type?.GetMethod("SyncMaterial", new[] { typeof(Material) });
        if (method == null)
            throw new InvalidOperationException("Environment_URP 的材质编辑器未加载。");
        return (Action<Material>)Delegate.CreateDelegate(typeof(Action<Material>), method);
    }
}
