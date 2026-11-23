using System;
using System.Reflection;
using lilToon.URP.Extensions.AttributeComposite;
using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HoVolumeUILayerValidation
{
    public static void Validate()
    {
        var image = ScriptableObject.CreateInstance<ImageProcessStackVolume>();
        UnityEditor.Editor editor = null;
        try
        {
            image.layers.value.Add(new ImageProcessLayer { effect = ImageProcessEffect.Glow, parameters0 = new Vector4(.7f,.5f,1,0) });
            editor = UnityEditor.Editor.CreateEditor(image);
            var lines = editor.GetType().GetMethod("GetGlowLineCount", BindingFlags.Instance | BindingFlags.NonPublic);
            var element = editor.serializedObject.FindProperty("layers").FindPropertyRelative("m_Value").GetArrayElementAtIndex(0);
            foreach (bool mapped in new[] { false, true })
            foreach (int mode in new[] { 0, 2 })
            {
                editor.serializedObject.FindProperty("ReplaceBuiltinPostProcessing").FindPropertyRelative("m_Value").boolValue = !mapped;
                element.FindPropertyRelative("parameters0").vector4Value = new Vector4(2,.5f,1,mode);
                int actual = (int)lines.Invoke(editor, new object[] { element });
                int expected = 8 + (mapped ? 4 : 0) + (mode == 2 ? 2 : 0);
                if (actual != expected) throw new Exception($"Glow row budget mismatch: {actual}/{expected}");
            }
        }
        finally { if (editor != null) Object.DestroyImmediate(editor); Object.DestroyImmediate(image); }

        var screen = ScriptableObject.CreateInstance<ScreenProcessStackVolume>(); editor = null;
        try
        {
            screen.layers.value.Add(new ScreenProcessLayer { useMask = true, maskSource = HoACQueryKind.Geometry });
            editor = UnityEditor.Editor.CreateEditor(screen);
            var lines = editor.GetType().GetMethod("GetMaskExtraLineCount", BindingFlags.Static | BindingFlags.NonPublic);
            var element = editor.serializedObject.FindProperty("layers").FindPropertyRelative("m_Value").GetArrayElementAtIndex(0);
            if ((int)lines.Invoke(null, new object[] { element }) != 2) throw new Exception("Valid mask retains an informational row");
            element.FindPropertyRelative("maskSource").intValue = (int)HoACQueryKind.Semantic;
            element.FindPropertyRelative("maskSemanticName").stringValue = "__missing_volume_ui_fixture__";
            if ((int)lines.Invoke(null, new object[] { element }) != 5) throw new Exception("Invalid mask warning has no two-row budget");
            element.FindPropertyRelative("useMask").boolValue = false;
            element.FindPropertyRelative("debugMask").boolValue = false;
            if ((int)lines.Invoke(null, new object[] { element }) != 0) throw new Exception("Disabled mask consumes rows");
        }
        finally { if (editor != null) Object.DestroyImmediate(editor); Object.DestroyImmediate(screen); }
        Debug.Log("HO_VOLUME_UI_LAYER_LAYOUT_PASSED glow=4 mask=3");
    }
}
