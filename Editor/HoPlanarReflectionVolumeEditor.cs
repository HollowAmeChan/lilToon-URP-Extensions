using System.Collections.Generic;
using lilToon.URP.Extensions.PlanarReflection;
using UnityEditor;
using UnityEditor.Rendering;

namespace lilToon.URP.Extensions.Editor
{
    [CustomEditor(typeof(HoPlanarReflectionVolume))]
    internal sealed class HoPlanarReflectionVolumeEditor : HoVolumeComponentEditor
    {
        private readonly Dictionary<string, SerializedDataParameter> parameters = new Dictionary<string, SerializedDataParameter>();
        private bool showComposite, showAdvanced, showDebug;

        public override void OnEnable()
        {
            parameters.Clear();
            foreach (string name in new[] {
                "enable", "renderGameView", "renderSceneView", "maxSurfacesPerCamera",
                "compositeEnabled", "compositeStrength", "distortion", "edgeExtendDistance", "compositeFlipY",
                "minSmoothness", "enableDepthGate", "depthTolerance", "reflectionExposure", "reflectionBlurRadiusPixels", "tint",
                "debugMode", "debugDepthFar", "debugDistortionScale" })
                parameters.Add(name, Unpack(serializedObject.FindProperty(name)));
        }

        public override void OnInspectorGUI()
        {
            Draw("enable", "renderSceneView", "renderGameView", "maxSurfacesPerCamera");
            if (HoVolumeEditorGUI.Section(ref showComposite, "合成"))
                Draw("compositeEnabled", "compositeStrength", "distortion", "edgeExtendDistance", "tint");
            if (HoVolumeEditorGUI.Section(ref showAdvanced, "高级"))
                Draw("minSmoothness", "enableDepthGate", "depthTolerance", "reflectionExposure", "reflectionBlurRadiusPixels", "compositeFlipY");
            if (HoVolumeEditorGUI.Section(ref showDebug, "调试"))
                Draw("debugMode", "debugDepthFar", "debugDistortionScale");
        }

        private void Draw(params string[] names)
        {
            foreach (string name in names) PropertyField(parameters[name]);
        }
    }
}
