using System.Collections.Generic;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>Explicit, undoable migration; legacy serialized effect numbers remain supported.</summary>
    public static class ScreenProcessLensMigration
    {
        public static bool IsLegacyLensEffect(ImageProcessLayer layer)
        {
            return layer != null && (layer.effect == ImageProcessEffect.BokehZoomBlur ||
                layer.effect == ImageProcessEffect.ApertureBokeh);
        }

        public static int CountMigratable(ImageProcessStackVolume source)
        {
            int count = 0;
            if (source == null || source.layers.value == null) return count;
            foreach (var layer in source.layers.value)
                if (IsLegacyLensEffect(layer) && layer.materialOverride == null && layer.shaderOverride == null && layer.passIndex == 0)
                    count++;
            return count;
        }

        public static int Migrate(VolumeProfile profile)
        {
            if (profile == null || !profile.TryGet(out ImageProcessStackVolume source) || CountMigratable(source) == 0)
                return 0;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate lens effects to ScreenProcess");
            Undo.RegisterCompleteObjectUndo(profile, "Migrate lens effects");
            Undo.RegisterCompleteObjectUndo(source, "Migrate lens effects");
            if (!profile.TryGet(out ScreenProcessStackVolume destination))
            {
                destination = profile.Add<ScreenProcessStackVolume>(true);
                destination.Enable.Override(source.Enable.value);
                destination.ShowInSceneView.Override(source.ShowInSceneView.value);
                Undo.RegisterCreatedObjectUndo(destination, "Create ScreenProcess stack");
                if (AssetDatabase.Contains(profile)) AssetDatabase.AddObjectToAsset(destination, profile);
            }
            else Undo.RegisterCompleteObjectUndo(destination, "Migrate lens effects");
            if (destination.layers.value == null) destination.layers.value = new List<ScreenProcessLayer>();
            destination.layers.overrideState = true;
            int count = 0;
            var retained = new List<ImageProcessLayer>();
            foreach (var layer in source.layers.value)
            {
                // A custom override has a different shader contract; leave it functional in IP.
                if (!IsLegacyLensEffect(layer) || layer.materialOverride != null || layer.shaderOverride != null || layer.passIndex != 0)
                {
                    retained.Add(layer);
                    continue;
                }
                ScreenProcessLayer converted = Convert(layer);
                converted.enabled &= source.active && source.Enable.value;
                destination.layers.value.Add(converted);
                count++;
            }
            source.layers.value = retained;
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(destination);
            EditorUtility.SetDirty(profile);
            Undo.CollapseUndoOperations(group);
            return count;
        }

        private static ScreenProcessLayer Convert(ImageProcessLayer source)
        {
            var layer = new ScreenProcessLayer {
                name = source.name, enabled = source.enabled, intensity = source.intensity, color = source.color
            };
            int blend = ConvertBlend(source.parameters3.x);
            if (source.effect == ImageProcessEffect.BokehZoomBlur)
            {
                layer.effect = ScreenProcessEffect.BokehZoomBlur;
                layer.blendMode = (ScreenProcessBlendMode)blend;
                layer.parameters0 = new Vector4(0,10,50,5.6f);
                layer.parameters1 = source.parameters0;
                layer.parameters2 = source.parameters1;
                layer.parameters3 = new Vector4(3,1,1,1);
                layer.parameters4 = source.parameters2;
                layer.parameters5 = new Vector4(source.parameters3.z,source.parameters3.y,.25f,0);
            }
            else
            {
                // Each old layer gets its own DoF layer, so an existing authored DoF is not overwritten.
                // This intentionally changes a full-screen overlay into focus-dependent bokeh.
                layer.effect = ScreenProcessEffect.DepthOfField;
                layer.blendMode = ScreenProcessBlendMode.Normal;
                layer.parameters0 = new Vector4(1,10,50,5.6f);
                float size = Mathf.Clamp01(source.parameters0.x);
                float radius = Mathf.Lerp(2,24,size) * (size > .35f ? 2 : 1);
                layer.parameters1 = new Vector4(10,30,radius,source.parameters1.w >= 1 ? 1 : 0);
                layer.parameters2 = new Vector4(Mathf.Clamp(source.parameters2.x,3,9),
                    source.parameters2.y,source.parameters2.z,blend);
                layer.parameters3 = new Vector4(3,1,1,1);
                layer.parameters4 = new Vector4(1,source.parameters0.y,source.parameters0.z,source.parameters3.z);
                layer.parameters5 = new Vector4(source.parameters0.w,source.parameters1.y,source.parameters2.w,source.parameters3.y);
            }
            return layer;
        }

        private static int ConvertBlend(float mode)
        {
            switch (Mathf.Clamp(Mathf.RoundToInt(mode),0,3))
            {
                case 1: return (int)ScreenProcessBlendMode.Screen;
                case 2: return (int)ScreenProcessBlendMode.Overlay;
                case 3: return (int)ScreenProcessBlendMode.Normal;
                default: return (int)ScreenProcessBlendMode.Add;
            }
        }
    }
}
