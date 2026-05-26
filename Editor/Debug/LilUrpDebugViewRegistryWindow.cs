using System.Collections.Generic;
using lilToon.URP.Extensions.Debugging;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.Debugging
{
    internal sealed class LilUrpDebugViewRegistryWindow : EditorWindow
    {
        private const string MenuPath = "lilToon URP Extensions/Debug/Open Debug View Registry";
        private const float TileWidth = 54.0f;
        private const float TileHeight = 36.0f;
        private const float RowSpacing = 4.0f;

        private static GUIStyle featureTitleStyle;
        private static GUIStyle tileStyle;
        private static GUIStyle dimLabelStyle;
        private static GUIStyle wrapMiniLabelStyle;

        private Vector2 scrollPosition;

        [MenuItem(MenuPath, false, 2190)]
        private static void Open()
        {
            LilUrpDebugViewRegistryWindow window = GetWindow<LilUrpDebugViewRegistryWindow>();
            window.titleContent = new GUIContent("Debug Views");
            window.minSize = new Vector2(720.0f, 420.0f);
            window.Show();
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.HelpBox(
                "This window is read-only. Debug shaders, materials and render targets stay owned by each feature.",
                MessageType.Info);

            IReadOnlyList<HoDebugViewInfo> views = HoDebugViewRegistry.AllViews;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Feature-local debug view registry", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Views: " + views.Count, EditorStyles.miniLabel);
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            string currentFeature = null;
            for (int i = 0; i < views.Count; i++)
            {
                HoDebugViewInfo view = views[i];
                if (currentFeature != view.FeatureName)
                {
                    currentFeature = view.FeatureName;
                    EditorGUILayout.Space(6.0f);
                    EditorGUILayout.LabelField(currentFeature, featureTitleStyle);
                }

                DrawViewTile(view);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawViewTile(HoDebugViewInfo view)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect rowRect = EditorGUILayout.GetControlRect(false, TileHeight);
                Rect tileRect = new Rect(rowRect.x, rowRect.y, TileWidth, TileHeight);
                Rect contentRect = new Rect(
                    rowRect.x + TileWidth + 8.0f,
                    rowRect.y,
                    rowRect.width - TileWidth - 8.0f,
                    TileHeight);

                GUI.Label(tileRect, view.ShortName, tileStyle);

                float lineHeight = EditorGUIUtility.singleLineHeight;
                Rect firstLine = new Rect(contentRect.x, contentRect.y, contentRect.width, lineHeight);
                Rect secondLine = new Rect(contentRect.x, contentRect.y + lineHeight, contentRect.width, lineHeight);

                EditorGUI.LabelField(firstLine, view.ViewId, EditorStyles.boldLabel);
                EditorGUI.LabelField(secondLine, BuildStatusText(view), dimLabelStyle);

                if (!string.IsNullOrEmpty(view.MissingFallback))
                {
                    EditorGUILayout.LabelField(view.MissingFallback, wrapMiniLabelStyle);
                }
            }

            EditorGUILayout.Space(RowSpacing);
        }

        private static string BuildStatusText(HoDebugViewInfo view)
        {
            string collectionStatus = view.RequiresShaderCollection
                ? "Shader collection: required"
                : "Shader collection: not required";
            string tileStatus = view.SupportsAutomaticTile
                ? "Tile: " + view.RenderKind
                : "Tile: not wired";
            string shaderStatus = view.HasShader
                ? "Shader asset: " + GetShaderAssetStatus(view.ShaderAssetPath)
                : "Shader asset: none";
            return "Mode: " + view.ModeValue + "    " + collectionStatus + "    " + tileStatus + "    " + shaderStatus;
        }

        private static string GetShaderAssetStatus(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "none";
            }

            System.Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (assetType == null)
            {
                return "missing (" + assetPath + ")";
            }

            return assetType == typeof(Shader)
                ? "present (" + assetPath + ")"
                : "unexpected type " + assetType.Name + " (" + assetPath + ")";
        }

        private static void EnsureStyles()
        {
            if (featureTitleStyle == null)
            {
                featureTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };
            }

            if (tileStyle == null)
            {
                tileStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fixedWidth = TileWidth,
                    fixedHeight = TileHeight,
                    wordWrap = false
                };
            }

            if (dimLabelStyle == null)
            {
                dimLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    clipping = TextClipping.Clip
                };
            }

            if (wrapMiniLabelStyle == null)
            {
                wrapMiniLabelStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
