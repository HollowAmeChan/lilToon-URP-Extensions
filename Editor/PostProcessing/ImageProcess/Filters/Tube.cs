using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ImageProcessStackVolumeEditor
    {
        private const float TubeLegacyInitMarker = 202601.0f;
        private const float TubeSoftDefaultMarker = 202602.0f;
        private const float TubeMotionTrailDefaultMarker = 202603.0f;
        private const float TubeInitMarker = 202604.0f;
        private static readonly string[] TubeModeNames = { "60年代", "70年代", "80年代", "90年代" };
        private static readonly string[] TubeLutGuids =
        {
            "4cdb4a3a04be3954f81ba4e7912a2a54",
            "ac45cb4b9b650c045b543093cdc2502e",
            "ac45cb4b9b650c045b543093cdc2502e",
            "c5d1e0809ad8b184885a007d89cd323f"
        };

        private void DrawTubeElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureTubeDefaults(parameters0, parameters1, parameters2, texture);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 tubeParams0 = parameters0.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(tubeParams0.x), 0, TubeModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, TubeModeNames);
            tubeParams0.x = mode;
            AssignTubeLutTexture(texture, mode);
            y += LineHeight + LineSpacing;

            tubeParams0.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化", Mathf.Clamp01(tubeParams0.y), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            tubeParams0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "屏幕抖动量", Mathf.Clamp(tubeParams0.z, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;

            tubeParams0.w = EditorGUI.Toggle(new Rect(rect.x, y, rect.width, LineHeight), "运动残影", tubeParams0.w > 0.5f) ? 1.0f : 0.0f;
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = tubeParams0;
            EditorGUI.indentLevel--;
        }

        private static void EnsureTubeDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty texture)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            bool hasMarker = parameters2 != null
                && parameters2.propertyType == SerializedPropertyType.Vector4
                && Mathf.Abs(parameters2.vector4Value.w - TubeInitMarker) < 0.5f;
            bool hasLegacyMarker = parameters2 != null
                && parameters2.propertyType == SerializedPropertyType.Vector4
                && Mathf.Abs(parameters2.vector4Value.w - TubeLegacyInitMarker) < 0.5f;
            bool hasSoftDefaultMarker = parameters2 != null
                && parameters2.propertyType == SerializedPropertyType.Vector4
                && Mathf.Abs(parameters2.vector4Value.w - TubeSoftDefaultMarker) < 0.5f;
            bool hasMotionTrailDefaultMarker = parameters2 != null
                && parameters2.propertyType == SerializedPropertyType.Vector4
                && Mathf.Abs(parameters2.vector4Value.w - TubeMotionTrailDefaultMarker) < 0.5f;

            if (!hasMarker && !hasLegacyMarker && !hasSoftDefaultMarker && !hasMotionTrailDefaultMarker)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
                AssignTubeLutTexture(texture, 0);
                if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
                {
                    parameters1.vector4Value = Vector4.zero;
                }

                if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
                {
                    parameters2.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, TubeInitMarker);
                }

                return;
            }

            Vector4 value = parameters0.vector4Value;
            value.x = Mathf.Clamp(Mathf.Round(value.x), 0.0f, 3.0f);
            value.y = Mathf.Clamp01(value.y);
            if (hasSoftDefaultMarker && Mathf.Abs(value.z - 0.35f) < 0.0001f)
            {
                value.z = 1.0f;
            }

            value.z = Mathf.Clamp(value.z, 0.0f, 2.0f);
            if (hasMotionTrailDefaultMarker)
            {
                value.w = 0.0f;
            }

            value.w = value.w > 0.5f ? 1.0f : 0.0f;
            parameters0.vector4Value = value;
            AssignTubeLutTexture(texture, Mathf.Clamp(Mathf.RoundToInt(value.x), 0, TubeModeNames.Length - 1));
            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 marker = parameters2.vector4Value;
                marker.w = TubeInitMarker;
                parameters2.vector4Value = marker;
            }
        }

        private static void AssignTubeLutTexture(SerializedProperty texture, int mode)
        {
            if (texture == null || texture.propertyType != SerializedPropertyType.ObjectReference)
            {
                return;
            }

            texture.objectReferenceValue = LoadTubeLutTexture(mode);
        }

        private static Texture2D LoadTubeLutTexture(int mode)
        {
            string guid = TubeLutGuids[Mathf.Clamp(mode, 0, TubeLutGuids.Length - 1)];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
