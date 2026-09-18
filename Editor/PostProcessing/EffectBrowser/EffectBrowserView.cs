using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace lilToon.URP.Extensions.Editor.PostProcessing
{
    /// <summary>
    /// Draws the effect browser: the search bar on top, the paged icon sidebar on the left, and the
    /// host's layer list on the right (or below, when the inspector is too narrow to split).
    /// </summary>
    /// <remarks>
    /// Search only filters the sidebar. The layer list is never filtered or reordered - matching
    /// rows are just highlighted by the host, which reads <see cref="CurrentQuery"/>.
    /// </remarks>
    internal static class EffectBrowserView
    {
        private const float IconSize = 24.0f;
        private const float IconSpacing = 4.0f;
        private const float NamedIconSize = 18.0f;
        private const float ToolbarHeight = 18.0f;
        private const float SearchHeight = 18.0f;
        private const float SidebarWidth = 64.0f;
        private const float NamedSidebarWidth = 150.0f;
        private const float MinSplitWidth = 320.0f;
        private const string SearchControlName = "lilToonEffectBrowserSearch";

        private static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>();
        private static readonly List<int> MatchBuffer = new List<int>(128);

        /// <summary>Normalized query of the frame that was drawn last (empty when not searching).</summary>
        public static string CurrentQuery { get; private set; } = string.Empty;

        /// <summary>
        /// Draws the browser and then the layer list (through <paramref name="drawLayerList"/>).
        /// </summary>
        public static void Draw(EffectBrowserCatalog catalog, EffectBrowserState state, System.Action drawLayerList)
        {
            if (catalog == null || state == null || drawLayerList == null)
            {
                return;
            }

            // The search row is drawn first so a keystroke takes effect in the same frame; the counts
            // label lives in a rect we only fill once the filtering below has run.
            Rect countsRect = DrawSearchBar(state);

            string query = EffectBrowserSearch.Normalize(state.Search);
            CurrentQuery = query;
            EffectBrowserSearch.Filter(catalog.Entries, query, MatchBuffer);
            int highlightCount = catalog.CountHighlightedLayers(query);

            GUI.Label(
                countsRect,
                string.IsNullOrEmpty(query)
                    ? "共 " + MatchBuffer.Count.ToString() + " 个效果"
                    : EffectBrowserSearch.FormatCounts(MatchBuffer.Count, highlightCount),
                EditorStyles.miniLabel);

            bool split = EditorGUIUtility.currentViewWidth >= MinSplitWidth;
            if (split)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(state.IconOnly ? SidebarWidth : NamedSidebarWidth));
                DrawSidebar(catalog, state, query, MatchBuffer);
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                drawLayerList();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // Narrow inspector: the sidebar folds above the list and wraps to the available width.
                EditorGUILayout.BeginVertical();
                DrawSidebar(catalog, state, query, MatchBuffer);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4.0f);
                drawLayerList();
            }

            state.Save();
        }

        // ------------------------------------------------------------------ search bar

        /// <summary>Draws the field and the clear button; returns the rect the counts label goes into.</summary>
        private static Rect DrawSearchBar(EffectBrowserState state)
        {
            bool focused = GUI.GetNameOfFocusedControl() == SearchControlName;
            if (focused && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                state.ClearSearch();
                GUI.FocusControl(null);
                Event.current.Use();
                GUI.changed = true;
            }

            Rect row = EditorGUILayout.GetControlRect(false, SearchHeight);
            float countsWidth = 190.0f;
            Rect fieldRect = new Rect(row.x, row.y, Mathf.Max(80.0f, row.width - countsWidth - 24.0f), SearchHeight);
            Rect clearRect = new Rect(fieldRect.xMax + 2.0f, row.y, 20.0f, SearchHeight);
            Rect countsRect = new Rect(clearRect.xMax + 4.0f, row.y, Mathf.Max(60.0f, row.xMax - clearRect.xMax - 4.0f), SearchHeight);

            GUI.SetNextControlName(SearchControlName);
            string typed = EditorGUI.TextField(fieldRect, state.Search ?? string.Empty);
            if (!string.Equals(typed, state.Search, System.StringComparison.Ordinal))
            {
                state.Search = typed;
                state.Page = 0;
            }

            if (string.IsNullOrEmpty(state.Search) && !focused)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(
                        new Rect(fieldRect.x + 4.0f, fieldRect.y, fieldRect.width - 8.0f, fieldRect.height),
                        "搜索效果（中文名或枚举名）",
                        EditorStyles.miniLabel);
                }
            }

            if (GUI.Button(clearRect, "×", EditorStyles.miniButton))
            {
                state.ClearSearch();
                GUI.FocusControl(null);
                GUI.changed = true;
            }

            return countsRect;
        }

        // ------------------------------------------------------------------ sidebar

        private static void DrawSidebar(
            EffectBrowserCatalog catalog,
            EffectBrowserState state,
            string query,
            List<int> matches)
        {
            int matchCount = matches.Count;
            int page = EffectBrowserSearch.ClampPage(state.Page, matchCount, state.IconOnly);
            state.Page = page;

            DrawSidebarToolbar(state, matchCount, page);
            DrawIconGrid(catalog, state, query, matches, page, EditorGUIUtility.currentViewWidth);

            if (matchCount == 0)
            {
                EditorGUILayout.LabelField("无匹配", EditorStyles.miniLabel);
            }

            if (string.IsNullOrEmpty(query) && catalog.LegacyEntries.Length > 0)
            {
                EditorGUILayout.LabelField("旧实现", EditorStyles.miniBoldLabel);
                DrawEntryRow(catalog, state, query, catalog.LegacyEntries);
            }
        }

        private static void DrawSidebarToolbar(EffectBrowserState state, int matchCount, int page)
        {
            EditorGUILayout.BeginHorizontal();
            bool previousEnabled = page > 0;
            using (new EditorGUI.DisabledScope(!previousEnabled))
            {
                if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(22.0f)))
                {
                    state.Page = page - 1;
                }
            }

            EditorGUILayout.LabelField(
                EffectBrowserSearch.FormatPageLabel(page, matchCount, state.IconOnly),
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(34.0f));

            bool nextEnabled = page + 1 < EffectBrowserSearch.PageCount(matchCount, state.IconOnly);
            using (new EditorGUI.DisabledScope(!nextEnabled))
            {
                if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(22.0f)))
                {
                    state.Page = page + 1;
                }
            }

            GUILayout.FlexibleSpace();
            DrawStyleButton(state, true, "⊞");
            DrawStyleButton(state, false, "≣");
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawStyleButton(EffectBrowserState state, bool iconOnly, string label)
        {
            Color oldColor = GUI.backgroundColor;
            if (state.IconOnly == iconOnly)
            {
                GUI.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.35f, 0.6f, 1.0f, 1.0f)
                    : new Color(0.6f, 0.75f, 1.0f, 1.0f);
            }

            if (GUILayout.Button(new GUIContent(label, iconOnly ? "纯图标（2 列 × 10）" : "图标 + 名字（1 列 × 10）"), EditorStyles.miniButton, GUILayout.Width(22.0f)))
            {
                state.IconOnly = iconOnly;
                state.Page = 0;
            }

            GUI.backgroundColor = oldColor;
        }

        private static void DrawIconGrid(
            EffectBrowserCatalog catalog,
            EffectBrowserState state,
            string query,
            List<int> matches,
            int page,
            float availableWidth)
        {
            int columns = EffectBrowserSearch.Columns(state.IconOnly);
            int rows = EffectBrowserSearch.Rows(state.IconOnly);
            EffectBrowserSearch.PageRange(page, matches.Count, state.IconOnly, out int start, out int count);

            float iconSize = state.IconOnly ? IconSize : NamedIconSize;
            float sidebarWidth = state.IconOnly ? SidebarWidth : NamedSidebarWidth;
            float maxCellWidth = state.IconOnly ? 40.0f : 200.0f;
            if (availableWidth < MinSplitWidth)
            {
                // Folded above the list: the grid may use the full width, but the cells keep a sane
                // size so a wide-but-short inspector does not fling the icons apart.
                sidebarWidth = Mathf.Max(sidebarWidth, availableWidth - 40.0f);
            }

            float cellWidth = Mathf.Min(sidebarWidth / columns, maxCellWidth);
            float cellHeight = iconSize + IconSpacing;
            float gridWidth = cellWidth * columns;
            float gridHeight = rows * cellHeight;

            Rect grid = GUILayoutUtility.GetRect(
                gridWidth,
                gridHeight,
                GUILayout.Width(gridWidth),
                GUILayout.Height(gridHeight));
            EditorGUI.DrawRect(grid, EditorGUIUtility.isProSkin ? new Color(0.0f, 0.0f, 0.0f, 0.22f) : new Color(0.0f, 0.0f, 0.0f, 0.06f));

            for (int i = 0; i < count; i++)
            {
                EffectBrowserEntry entry = catalog.Entries[matches[start + i]];
                int column = columns == 1 ? 0 : i % columns;
                int rowIndex = columns == 1 ? i : i / columns;
                float x = grid.x + (column * cellWidth) + 2.0f;
                float y = grid.y + (rowIndex * cellHeight) + 2.0f;
                Rect cell = new Rect(x, y, cellWidth - 4.0f, iconSize);

                DrawEntryCell(catalog, state, query, entry, cell, state.IconOnly);
            }
        }

        /// <summary>One icon (icon-only style) with tooltip, click-to-toggle and a right-click menu.</summary>
        private static void DrawEntryCell(
            EffectBrowserCatalog catalog,
            EffectBrowserState state,
            string query,
            EffectBrowserEntry entry,
            Rect cell,
            bool iconOnly)
        {
            bool present = catalog.IsPresent != null && catalog.IsPresent(entry.Effect);
            Texture2D icon = LoadIcon(entry.IconName);

            if (iconOnly)
            {
                Rect iconRect = new Rect(
                    cell.x + ((cell.width - IconSize) * 0.5f),
                    cell.y,
                    IconSize,
                    IconSize);
                DrawIcon(iconRect, icon, present, entry.Tooltip);
                HandleEntryInput(catalog, state, query, entry, iconRect, present);
                return;
            }

            float textWidth = Mathf.Max(0.0f, cell.width - NamedIconSize - 6.0f);
            Rect namedIconRect = new Rect(cell.x, cell.y + 1.0f, NamedIconSize, NamedIconSize);
            Rect labelRect = new Rect(namedIconRect.xMax + 4.0f, cell.y, textWidth, cell.height);
            DrawIcon(namedIconRect, icon, present, entry.Tooltip);
            EditorGUI.LabelField(labelRect, new GUIContent(entry.Label, entry.Tooltip), EditorStyles.miniLabel);
            HandleEntryInput(catalog, state, query, entry, cell, present);
        }

        private static void DrawIcon(Rect rect, Texture2D icon, bool present, string tooltip)
        {
            if (icon != null)
            {
                Color oldColor = GUI.color;
                GUI.color = present ? new Color(0.45f, 1.0f, 0.45f, 1.0f) : Color.white;
                GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
                GUI.color = oldColor;
            }
            else
            {
                EditorGUI.LabelField(rect, new GUIContent("?", tooltip), EditorStyles.centeredGreyMiniLabel);
            }

            GUI.Label(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        private static void HandleEntryInput(
            EffectBrowserCatalog catalog,
            EffectBrowserState state,
            string query,
            EffectBrowserEntry entry,
            Rect rect,
            bool present)
        {
            if (Event.current.type != EventType.MouseDown || !rect.Contains(Event.current.mousePosition))
            {
                return;
            }

            if (Event.current.button == 0)
            {
                catalog.Toggle?.Invoke(entry.Effect);
                Event.current.Use();
                GUI.changed = true;
                return;
            }

            if (Event.current.button == 1)
            {
                int layers = catalog.LayerCount != null ? catalog.LayerCount(entry.Effect) : 0;
                var menu = new GenericMenu();
                menu.AddDisabledItem(new GUIContent(entry.Tooltip));
                if (present)
                {
                    menu.AddItem(new GUIContent("移除该图层（" + layers.ToString() + " 个）"), false, () => catalog.Toggle?.Invoke(entry.Effect));
                }
                else
                {
                    menu.AddItem(new GUIContent("添加为图层"), false, () => catalog.Toggle?.Invoke(entry.Effect));
                }

                menu.AddItem(new GUIContent("重置为默认参数"), false, () => catalog.ResetToDefaults?.Invoke(entry.Effect));
                if (!string.IsNullOrEmpty(query))
                {
                    menu.AddSeparator(string.Empty);
                    menu.AddItem(new GUIContent("清空搜索"), false, () =>
                    {
                        state.ClearSearch();
                        GUI.changed = true;
                    });
                }

                menu.ShowAsContext();
                Event.current.Use();
            }
        }

        /// <summary>Horizontal row used for the legacy section (no paging, wraps with the inspector).</summary>
        private static void DrawEntryRow(EffectBrowserCatalog catalog, EffectBrowserState state, string query, EffectBrowserEntry[] entries)
        {
            float width = Mathf.Max(160.0f, EditorGUIUtility.currentViewWidth - 60.0f);
            float cellWidth = IconSize + IconSpacing;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + IconSpacing) / cellWidth));
            int rowCount = Mathf.CeilToInt(entries.Length / (float)perRow);
            float height = rowCount * cellWidth;

            Rect area = GUILayoutUtility.GetRect(0.0f, height, GUILayout.ExpandWidth(true));
            for (int i = 0; i < entries.Length; i++)
            {
                int column = i % perRow;
                int rowIndex = i / perRow;
                Rect cell = new Rect(area.x + (column * cellWidth), area.y + (rowIndex * cellWidth), IconSize, IconSize);
                DrawEntryCell(catalog, state, query, entries[i], cell, true);
            }
        }

        // ------------------------------------------------------------------ icons

        /// <summary>
        /// Icon lookup used by both the sidebar and the layer rows' preset button. Package path first
        /// (the normal case), then a project-local override folder.
        /// </summary>
        public static Texture2D LoadIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            if (IconCache.TryGetValue(iconName, out Texture2D cached))
            {
                return cached;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/jp.lilxyzw.liltoon.urp.extensions/Editor/ImageProcessIcons/" + iconName + ".png");
            if (texture == null)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ImageProcessIcons/" + iconName + ".png");
            }

            IconCache[iconName] = texture;
            return texture;
        }
    }
}
