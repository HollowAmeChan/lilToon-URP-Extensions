using System;
using System.Collections.Generic;
using System.Reflection;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>GPU regression: every roughness mip must preserve a constant HDR input.</summary>
public static class HoMmdPlrGpuCheck
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string CheckPrefilter(Camera initializedCamera)
    {
        var go = new GameObject("PLR prefilter regression") { hideFlags = HideFlags.HideAndDontSave };
        var cmd = new CommandBuffer { name = "PLR HDR regression" };
        var results = new List<string>();
        try
        {
            var surface = go.AddComponent<HoPlanarReflectionSurface>();
            var type = typeof(HoPlanarReflectionSurface);
            type.GetField("resolution", Private).SetValue(surface, 64);
            type.GetMethod("EnsureResources", Private).Invoke(surface, new object[] { initializedCamera });
            var texture = (RenderTexture)type.GetField("reflectionTexture", Private).GetValue(surface);
            var expected = new Color(2f, .5f, .25f, 1f);
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                cmd.SetRenderTarget(texture, mip);
                cmd.ClearRenderTarget(false, true, mip == 0 ? expected : Color.black);
            }
            type.GetMethod("BuildReflectionPrefilter", Private).Invoke(surface, new object[] { cmd });
            Graphics.ExecuteCommandBuffer(cmd);
            for (int mip = 1; mip < texture.mipmapCount; mip++)
            {
                var desc = texture.descriptor;
                desc.width = Mathf.Max(1, texture.width >> mip); desc.height = Mathf.Max(1, texture.height >> mip);
                desc.useMipMap = false; desc.autoGenerateMips = false; desc.mipCount = 1;
                var level = RenderTexture.GetTemporary(desc);
                var prior = RenderTexture.active;
                Texture2D cpu = null;
                try
                {
                    Graphics.CopyTexture(texture, 0, mip, level, 0, 0);
                    RenderTexture.active = level;
                    cpu = new Texture2D(level.width, level.height, TextureFormat.RGBAFloat, false, true);
                    cpu.ReadPixels(new Rect(0, 0, level.width, level.height), 0, 0); cpu.Apply();
                    float error = 0;
                    foreach (var p in cpu.GetPixels())
                        error = Mathf.Max(error, Mathf.Abs(p.r - expected.r), Mathf.Abs(p.g - expected.g), Mathf.Abs(p.b - expected.b));
                    if (error > .025f) throw new InvalidOperationException("PLR mip " + mip + " lost HDR energy; max error=" + error);
                    results.Add("mip" + mip + " maxError=" + error.ToString("G5", System.Globalization.CultureInfo.InvariantCulture));
                }
                finally
                {
                    RenderTexture.active = prior;
                    if (cpu != null) Object.DestroyImmediate(cpu);
                    RenderTexture.ReleaseTemporary(level);
                }
            }
            return string.Join("\n", results);
        }
        finally { cmd.Release(); Object.DestroyImmediate(go); }
    }
}
