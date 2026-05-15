using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] CrtEffectModeNames = { "RGB", "RGB 单色", "圆形", "线条" };

        private void DrawCrtEffectsElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureCrtEffectsDefaults(parameters0);
            AssignCrtEffectsTexture(parameters0, texture);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, CrtEffectModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "类型", mode, CrtEffectModeNames);
            p0.x = mode;
            p0.z = mode <= 1 ? 3.0f : 1.5f;
            y += LineHeight + LineSpacing;

            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分辨率", Mathf.Clamp01(p0.y), 0.01f, 1.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = p0;
            AssignCrtEffectsTexture(parameters0, texture);
            EditorGUI.indentLevel--;
        }

        private static void EnsureCrtEffectsDefaults(SerializedProperty parameters0)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 1.0f, 3.0f, 0.0f);
            }
        }

        private static void AssignCrtEffectsTexture(SerializedProperty parameters0, SerializedProperty texture)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4 || texture == null || texture.propertyType != SerializedPropertyType.ObjectReference)
            {
                return;
            }

            texture.objectReferenceValue = LoadCrtEffectsTexture(Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.x), 0, CrtEffectModeNames.Length - 1));
        }

        private static Texture2D LoadCrtEffectsTexture(int index)
        {
            string name;
            switch (index)
            {
                case 1:
                    name = "ShoostCRTScanlinesRGBMono";
                    break;
                case 2:
                    name = "ShoostCRTScanlinesCircle";
                    break;
                case 3:
                    name = "ShoostCRTScanlinesLine";
                    break;
                default:
                    name = "ShoostCRTScanlinesRGB";
                    break;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{PackageAssetRoot}/Runtime/PostProcessing/Textures/{name}.png");
        }
    }
}
