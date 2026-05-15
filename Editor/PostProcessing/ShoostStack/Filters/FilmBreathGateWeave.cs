using lilToon.URP.Extensions.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    internal sealed partial class ShoostPostProcessStackVolumeEditor
    {
        private const float FilmInitMarker = 202605.0f;

        private static readonly string[] FilmModeNames = { "60年代", "70年代", "80年代", "90年代" };

        private static readonly string[][] FilmFilterTypeNames =
        {
            new[] { "单色v1", "单色v2", "单色v3" },
            new[] { "柯达v1" },
            new[] { "柯达v2", "富士v2" },
            new[] { "柯达v3", "富士v3" }
        };

        private static readonly string[][] FilmLutGuids =
        {
            new[] { "4cdb4a3a04be3954f81ba4e7912a2a54", "4cdb4a3a04be3954f81ba4e7912a2a54", "4cdb4a3a04be3954f81ba4e7912a2a54" },
            new[] { "7432d4ca450073346a0580882e10ee2a" },
            new[] { "a83e01c459730b5459ee3f175b755606", "ac45cb4b9b650c045b543093cdc2502e" },
            new[] { "9c6aeda51076c9e479448f6c24ecc697", "c5d1e0809ad8b184885a007d89cd323f" }
        };

        private void DrawFilmBreathGateWeaveElement(Rect rect, SerializedProperty element)
        {
            SerializedProperty parameters0 = element.FindPropertyRelative("parameters0");
            SerializedProperty parameters1 = element.FindPropertyRelative("parameters1");
            SerializedProperty parameters2 = element.FindPropertyRelative("parameters2");
            SerializedProperty texture = element.FindPropertyRelative("texture");
            SerializedProperty enabled = element.FindPropertyRelative("enabled");

            EnsureFilmBreathGateWeaveDefaults(parameters0, parameters1, parameters2, texture);

            float y = rect.y;
            y = DrawFoldoutLine(rect, y, element, enabled);
            if (!element.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            y = DrawLayerCoreFields(rect.x, y, rect.width, element, includeBlendMode: false, includeColor: false, includeTexture: false, includePassIndex: false, includeMaterialOverride: false, includeParameters: false, showAdvancedFields: showAdvancedSettings);

            Vector4 filmParams0 = parameters0.vector4Value;
            Vector4 filmParams1 = parameters1.vector4Value;

            int mode = Mathf.Clamp(Mathf.RoundToInt(filmParams0.x), 0, FilmModeNames.Length - 1);
            mode = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "模式", mode, FilmModeNames);
            if (!Mathf.Approximately(filmParams0.x, mode))
            {
                filmParams0.y = 0.0f;
            }

            filmParams0.x = mode;
            y += LineHeight + LineSpacing;

            string[] filterNames = FilmFilterTypeNames[mode];
            int filterType = Mathf.Clamp(Mathf.RoundToInt(filmParams0.y), 0, filterNames.Length - 1);
            filterType = EditorGUI.Popup(new Rect(rect.x, y, rect.width, LineHeight), "滤镜类型", filterType, filterNames);
            filmParams0.y = filterType;
            AssignFilmLutTexture(texture, mode, filterType);
            y += LineHeight + LineSpacing;

            filmParams0.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "滤镜强度", Mathf.Clamp01(filmParams0.z), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            filmParams0.w = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "锐化", Mathf.Clamp01(filmParams0.w), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            filmParams1.x = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "颗粒强度", Mathf.Clamp01(filmParams1.x), 0.0f, 1.0f);
            y += LineHeight + LineSpacing;

            filmParams1.y = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "颗粒大小", Mathf.Clamp(filmParams1.y, 0.3f, 3.0f), 0.3f, 3.0f);
            y += LineHeight + LineSpacing;

            filmParams1.z = EditorGUI.Slider(new Rect(rect.x, y, rect.width, LineHeight), "屏幕抖动量", Mathf.Clamp(filmParams1.z, 0.0f, 2.0f), 0.0f, 2.0f);
            y += LineHeight + LineSpacing;

            parameters0.vector4Value = filmParams0;
            parameters1.vector4Value = filmParams1;
            EditorGUI.indentLevel--;
        }

        private static void EnsureFilmBreathGateWeaveDefaults(SerializedProperty parameters0, SerializedProperty parameters1, SerializedProperty parameters2, SerializedProperty texture)
        {
            if (parameters0 == null || parameters0.propertyType != SerializedPropertyType.Vector4)
            {
                return;
            }

            bool hasMarker = parameters2 != null
                && parameters2.propertyType == SerializedPropertyType.Vector4
                && Mathf.Abs(parameters2.vector4Value.w - FilmInitMarker) < 0.5f;

            if (!hasMarker)
            {
                parameters0.vector4Value = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);

                if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
                {
                    parameters1.vector4Value = new Vector4(0.2f, 1.0f, 1.0f, 0.0f);
                }

                if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
                {
                    parameters2.vector4Value = new Vector4(0.0f, 0.0f, 0.0f, FilmInitMarker);
                }
            }

            Vector4 filmParams0 = parameters0.vector4Value;
            filmParams0.x = Mathf.Clamp(Mathf.Round(filmParams0.x), 0.0f, FilmModeNames.Length - 1);
            filmParams0.y = Mathf.Clamp(Mathf.Round(filmParams0.y), 0.0f, FilmFilterTypeNames[Mathf.RoundToInt(filmParams0.x)].Length - 1);
            filmParams0.z = Mathf.Clamp01(filmParams0.z <= 0.0001f ? 1.0f : filmParams0.z);
            filmParams0.w = Mathf.Clamp01(filmParams0.w);
            parameters0.vector4Value = filmParams0;

            if (parameters1 != null && parameters1.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 filmParams1 = parameters1.vector4Value;
                filmParams1.x = Mathf.Clamp01(filmParams1.x);
                filmParams1.y = Mathf.Clamp(filmParams1.y <= 0.0001f ? 1.0f : filmParams1.y, 0.3f, 3.0f);
                filmParams1.z = Mathf.Clamp(filmParams1.z <= 0.0001f ? 1.0f : filmParams1.z, 0.0f, 2.0f);
                filmParams1.w = 0.0f;
                parameters1.vector4Value = filmParams1;
            }

            AssignFilmLutTexture(texture, Mathf.RoundToInt(filmParams0.x), Mathf.RoundToInt(filmParams0.y));
            if (parameters2 != null && parameters2.propertyType == SerializedPropertyType.Vector4)
            {
                Vector4 marker = parameters2.vector4Value;
                marker.w = FilmInitMarker;
                parameters2.vector4Value = marker;
            }
        }

        private static void AssignFilmLutTexture(SerializedProperty texture, int mode, int filterType)
        {
            if (texture == null || texture.propertyType != SerializedPropertyType.ObjectReference)
            {
                return;
            }

            texture.objectReferenceValue = LoadFilmLutTexture(mode, filterType);
        }

        private static Texture2D LoadFilmLutTexture(int mode, int filterType)
        {
            string[] guids = FilmLutGuids[Mathf.Clamp(mode, 0, FilmLutGuids.Length - 1)];
            string guid = guids[Mathf.Clamp(filterType, 0, guids.Length - 1)];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
