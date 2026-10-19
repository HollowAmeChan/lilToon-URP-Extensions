using System;
using System.IO;
using lilToon.URP.Extensions.Atmosphere;
using lilToon.URP.Extensions.Editor.Atmosphere;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace lilToon.URP.Extensions.VolumeLight
{
    public static partial class HoVolumeLightValidation
    {
        private static void ValidateDensityPresets()
        {
            lightFeature.FeatureSettings.enabled = false;
            region.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); region.transform.localScale = Vector3.one;
            region.size = Vector3.one * 20; region.density = .3f; region.tint = Color.white; region.noiseStrength = 1;
            region.noiseWorldSize = 4; region.wind = Vector3.zero; HoAtmosphereMediumClock.TimeOverride = 0;
            probe.material.SetFloat("_ProbeMode", 0); probe.material.SetFloat("_ProbeTime", 0);
            var report = "Preset,Format,Min,Max,Mean,GPU centres,Repeat seams\n";
            for (int index = 0; index < HoAtmosphereDensityPresets.All.Length; ++index)
            {
                var texture = HoAtmosphereDensityPresets.Load(index);
                Require(texture != null, "Bundled Texture3D missing: " + HoAtmosphereDensityPresets.All[index].file);
                Require(texture.width == 64 && texture.height == 64 && texture.depth == 64 && texture.mipmapCount == 1, "Invalid preset dimensions/mips: " + texture.name);
                Require(texture.graphicsFormat == GraphicsFormat.R8_UNorm && texture.filterMode == FilterMode.Bilinear &&
                    texture.wrapModeU == TextureWrapMode.Repeat && texture.wrapModeV == TextureWrapMode.Repeat && texture.wrapModeW == TextureWrapMode.Repeat, "Invalid preset format/filter/wrap: " + texture.name);
                var values = texture.GetPixelData<byte>(0);
                double sum = 0; byte min = 255, max = 0;
                foreach (byte value in values) { sum += value; min = Math.Min(min, value); max = Math.Max(max, value); }
                float mean = (float)(sum / values.Length / 255);
                Require(min < 30 && max > 225 && Mathf.Abs(mean - .5f) < .002f, "Preset density distribution is invalid: " + texture.name);
                region.densityTexture = texture;
                var coordinates = new[] { new Vector3Int(3, 9, 21), new Vector3Int(45, 17, 50), new Vector3Int(62, 32, 1) };
                var points = new Vector3[9];
                for (int i = 0; i < coordinates.Length; ++i) points[i] = ((Vector3)coordinates[i] + Vector3.one * .5f) / 64 * 4;
                for (int axis = 0; axis < 3; ++axis)
                {
                    var point = new Vector3(.83f, 1.27f, 2.14f); point[axis] = -.0001f; points[3 + axis * 2] = point;
                    point[axis] = .0001f; points[4 + axis * 2] = point;
                }
                probe.Points(points); Render(); var pixels = Read(probe.destination.rt);
                for (int i = 0; i < coordinates.Length; ++i)
                {
                    var p = coordinates[i]; float expected = values[p.x + p.y * 64 + p.z * 64 * 64] / 255f * region.density;
                    Near(pixels[i].g, expected, texture.name + " GPU density " + i, .001f);
                }
                for (int axis = 0; axis < 3; ++axis) Near(pixels[3 + axis * 2].g, pixels[4 + axis * 2].g, texture.name + " repeat seam axis " + axis, .001f);
                report += FormattableString.Invariant($"{texture.name},R8_UNorm,{min / 255f:F4},{max / 255f:F4},{mean:F6},PASS,PASS\n");
            }
            // The actual multi-object assignment path must preserve all art parameters and Undo.
            var second = Keep(new GameObject("Density preset multi-edit validation")).AddComponent<HoAtmosphereRegion>(); second.enabled = false;
            var oldTexture = region.densityTexture; second.densityTexture = null;
            Vector3 wind = region.wind; float scale = region.noiseWorldSize, density = region.density; Color tint = region.tint;
            var presetTexture = HoAtmosphereDensityPresets.Load(1);
            Undo.IncrementCurrentGroup();
            HoAtmosphereDensityPresets.Apply(new UnityEngine.Object[] { region, second }, presetTexture);
            Undo.FlushUndoRecordObjects();
            Require(region.densityTexture == presetTexture && second.densityTexture == presetTexture, "Preset multi-object assignment failed");
            Require(region.wind == wind && region.noiseWorldSize == scale && region.density == density && region.tint == tint, "Preset changed art parameters beyond texture");
            Undo.PerformUndo(); Require(region.densityTexture == oldTexture && second.densityTexture == null, "Preset Undo failed");
            Undo.PerformRedo(); Require(region.densityTexture == presetTexture && second.densityTexture == presetTexture, "Preset Redo failed");
            var directory = Path.Combine(Directory.GetCurrentDirectory(), "AtmosphereDensityValidation"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "presets-" + SystemInfo.graphicsDeviceType + ".csv"), report);
            Debug.Log("[HoAtmosphereDensityPresets] PASS: four R8 Texture3D assets, GPU density samples, XYZ repeat seams, multi-object assignment + Undo/Redo; API=" + SystemInfo.graphicsDeviceType);
        }
    }
}
