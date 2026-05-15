using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private static readonly string[] DitheringModeNames = { "单色调", "颜色" };
        private static readonly string[] DitheringTextureNames = { "V1", "V2", "V3" };

        private static bool GetDitheringCustomUsesColorMode(SerializedProperty element)
        {
            SerializedProperty parameters0 = element?.FindPropertyRelative("parameters0");
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return false;
            }

            return Mathf.RoundToInt(parameters0.vector4Value.x) == 1;
        }

        private void DrawDitheringCustomElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty parameters3 = element.FindPropertyRelative("parameters3");
            SerializedProperty parameters4 = element.FindPropertyRelative("parameters4");
            SerializedProperty parameters5 = element.FindPropertyRelative("parameters5");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureDitheringCustomDefaults(parameters0, parameters1, parameters2, parameters3, parameters4, parameters5);
            AssignDitheringTexture(parameters0, texture);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 p0 = parameters0.vector4Value;
            Vector4 p1 = parameters1.vector4Value;
            Vector4 p2 = parameters2.vector4Value;
            Vector4 shadows = parameters3.vector4Value;
            Vector4 midtones = parameters4.vector4Value;
            Vector4 highlights = parameters5.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(p0.x), 0, 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, DitheringModeNames);
            p0.x = mode;
            y += LineHeight + LineSpacing;

            p0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "分辨率", Mathf.Clamp01(p0.y), 0.01f, 1.0f);
            y += LineHeight + LineSpacing;

            int ditheringType = Mathf.Clamp(Mathf.RoundToInt(p0.z), 0, 2);
            ditheringType = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "抖动类型", ditheringType, DitheringTextureNames);
            p0.z = ditheringType;
            y += LineHeight + LineSpacing;

            p0.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "网格线", p0.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            if (mode == 0)
            {
                p1.x = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "亮度阶调", Mathf.RoundToInt(p1.x), 2, 16);
                y += LineHeight + LineSpacing;
                shadows = DrawVectorColorLine(rect.x, y, rect.width, "阴影", shadows);
                y += LineHeight + LineSpacing;
                midtones = DrawVectorColorLine(rect.x, y, rect.width, "中间调", midtones);
                y += LineHeight + LineSpacing;
                highlights = DrawVectorColorLine(rect.x, y, rect.width, "高光", highlights);
                y += LineHeight + LineSpacing;
            }
            else
            {
                p1.y = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "红色阶调", Mathf.RoundToInt(p1.y), 2, 32);
                y += LineHeight + LineSpacing;
                p1.z = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "绿色阶调", Mathf.RoundToInt(p1.z), 2, 32);
                y += LineHeight + LineSpacing;
                p1.w = EditorGUI.IntSlider(new Rect(rect.x, y, rect.width, LineHeight), "蓝色阶调", Mathf.RoundToInt(p1.w), 2, 32);
                y += LineHeight + LineSpacing;
                p2.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "混合量", p2.x, 0.0f, 1.0f);
                y += LineHeight + LineSpacing;
            }

            parameters0.vector4Value = p0;
            parameters1.vector4Value = p1;
            parameters2.vector4Value = p2;
            parameters3.vector4Value = shadows;
            parameters4.vector4Value = midtones;
            parameters5.vector4Value = highlights;
            AssignDitheringTexture(parameters0, texture);
            EditorGUI.indentLevel--;
        }

        private static Vector4 DrawVectorColorLine(float x, float y, float width, string label, Vector4 value)
        {
            Color color = new Color(value.x, value.y, value.z, 1.0f);
            color = EditorGUI.ColorField(new Rect(x, y, width, LineHeight), new GUIContent(label), color, true, false, false);
            return new Vector4(color.r, color.g, color.b, 1.0f);
        }

        private static void EnsureDitheringCustomDefaults(
            SerializedProperty parameters0,
            SerializedProperty parameters1,
            SerializedProperty parameters2,
            SerializedProperty parameters3,
            SerializedProperty parameters4,
            SerializedProperty parameters5)
        {
            if (parameters0 != null && parameters0.propertyType == SerializedPropertyType.Vector4 && parameters0.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters0.vector4Value = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
            }

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4 && parameters1.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters1.vector4Value = new Vector4(4.0f, 32.0f, 32.0f, 32.0f);
            }

            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4 && parameters2.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters2.vector4Value = new Vector4(0.5f, 0.0f, 0.0f, 0.0f);
            }

            if (parameters3 != null && parameters3.propertyType == SerializedPropertyType.Vector4 && parameters3.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters3.vector4Value = new Vector4(0.1254902f, 0.2f, 0.1764706f, 1.0f);
            }

            if (parameters4 != null && parameters4.propertyType == SerializedPropertyType.Vector4 && parameters4.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters4.vector4Value = new Vector4(0.3372549f, 0.4980392f, 0.3803922f, 1.0f);
            }

            if (parameters5 != null && parameters5.propertyType == SerializedPropertyType.Vector4 && parameters5.vector4Value.sqrMagnitude <= 0.000001f)
            {
                parameters5.vector4Value = new Vector4(0.8627451f, 0.8862745f, 0.3882353f, 1.0f);
            }
        }

        private static void AssignDitheringTexture(SerializedProperty parameters0, SerializedProperty texture)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4 || texture == null || texture.propertyType != SerializedPropertyType.ObjectReference)
            {
                return;
            }

            texture.objectReferenceValue = LoadDitheringTexture(Mathf.Clamp(Mathf.RoundToInt(parameters0.vector4Value.z), 0, 2));
        }

        private static Texture2D LoadDitheringTexture(int index)
        {
            string name = index == 1 ? "ShoostDitheringV2" : index == 2 ? "ShoostDitheringV3" : "ShoostDitheringV1";
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{PackageAssetRoot}/Runtime/PostProcessing/Textures/{name}.png");
        }
    }
}
