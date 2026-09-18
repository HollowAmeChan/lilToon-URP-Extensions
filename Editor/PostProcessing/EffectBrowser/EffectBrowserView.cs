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
        private const float RowHeight = 18.0f;
        private const float SearchHeight = 18.0f;
        /// <summary>One width for both styles: the toolbar, the grid and the search row's toggle align to it.</summary>
        private const float SidebarWidth = 104.0f;
        private const float StyleToggleWidth = 26.0f;
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

            // The search row is drawn first so a keystroke takes effect in the same frame.
            DrawSearchBar(state);

            string query = EffectBrowserSearch.Normalize(state.Search);
            CurrentQuery = query;
            EffectBrowserSearch.Filter(catalog.Entries, query, MatchBuffer);
            int highlightCount = catalog.CountHighlightedLayers(query);

            bool split = EditorGUIUtility.currentViewWidth >= MinSplitWidth;
            if (split)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
                DrawSidebar(catalog, state, query, MatchBuffer, highlightCount);
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
                DrawSidebar(catalog, state, query, MatchBuffer, highlightCount);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4.0f);
                drawLayerList();
            }

            state.Save();
        }

        // ------------------------------------------------------------------ search bar

        /// <summary>
        /// One row: the sidebar style toggle, then the search field taking all the remaining width,
        /// then the clear button. No counts text - the numbers live in the page label's tooltip.
        /// </summary>
        private static void DrawSearchBar(EffectBrowserState state)
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
            Rect styleRect = new Rect(row.x, row.y, StyleToggleWidth, SearchHeight);
            Rect clearRect = new Rect(row.xMax - 20.0f, row.y, 20.0f, SearchHeight);
            Rect fieldRect = new Rect(styleRect.xMax + 4.0f, row.y, Mathf.Max(60.0f, clearRect.x - styleRect.xMax - 6.0f), SearchHeight);

            DrawStyleToggle(state, styleRect);

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

            // The clear button is chromeless and only lights up when there is something to clear.
            if (DrawChromeLessButton(clearRect, new GUIContent("×", "清空搜索"), !string.IsNullOrEmpty(state.Search)))
            {
                state.ClearSearch();
                GUI.FocusControl(null);
                GUI.changed = true;
            }
        }

        /// <summary>The one boolean style switch: it shows the style in use and flips on click.</summary>
        private static void DrawStyleToggle(EffectBrowserState state, Rect rect)
        {
            GUIContent content = state.IconOnly
                ? new GUIContent("⊞", "纯图标（3 列 × 20 = 60/页）：点击切到「图标 + 名字」")
                : new GUIContent("≣", "图标 + 名字（1 列 × 20 = 20/页）：点击切到「纯图标」");

            if (DrawChromeLessButton(rect, content))
            {
                state.IconOnly = !state.IconOnly;
                state.Page = 0;
            }
        }

        // ------------------------------------------------------------------ sidebar

        private static void DrawSidebar(
            EffectBrowserCatalog catalog,
            EffectBrowserState state,
            string query,
            List<int> matches,
            int highlightCount)
        {
            int matchCount = matches.Count;
            int page = EffectBrowserSearch.ClampPage(state.Page, matchCount, state.IconOnly);
            state.Page = page;

            DrawSidebarToolbar(state, matchCount, page, highlightCount);
            DrawIconGrid(catalog, state, query, matches, page, EditorGUIUtility.currentViewWidth);

            if (string.IsNullOrEmpty(query) && catalog.LegacyEntries.Length > 0)
            {
                EditorGUILayout.LabelField("旧实现", EditorStyles.miniBoldLabel);
                DrawEntryRow(catalog, state, query, catalog.LegacyEntries);
            }
        }

        /// <summary>
        /// Exactly one sidebar width: the two chromeless arrows hug the edges and the page label
        /// fills the middle, so this row lines up with the icon grid below it.
        /// </summary>
        private static void DrawSidebarToolbar(EffectBrowserState state, int matchCount, int page, int highlightCount)
        {
            int pageCount = EffectBrowserSearch.PageCount(matchCount, state.IconOnly);
            Rect row = GUILayoutUtility.GetRect(
                SidebarWidth,
                RowHeight,
                GUILayout.Width(SidebarWidth),
                GUILayout.Height(RowHeight));

            const float arrowWidth = 16.0f;
            Rect previousRect = new Rect(row.x, row.y, arrowWidth, row.height);
            Rect nextRect = new Rect(row.xMax - arrowWidth, row.y, arrowWidth, row.height);
            Rect labelRect = new Rect(previousRect.xMax, row.y, Mathf.Max(0.0f, nextRect.x - previousRect.xMax), row.height);

            DrawPagingArrow(previousRect, "◀", page > 0, () => state.Page = page - 1);
            DrawPagingArrow(nextRect, "▶", page + 1 < pageCount, () => state.Page = page + 1);

            EditorGUI.LabelField(
                labelRect,
                new GUIContent(
                    EffectBrowserSearch.FormatPageLabel(page, matchCount, state.IconOnly),
                    EffectBrowserSearch.FormatCounts(matchCount, highlightCount)),
                EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>
        /// Chromeless clickable glyph: no button background at all, just a faint hover highlight,
        /// the link cursor and a click. Used by every control of the browser (style switch, clear,
        /// paging arrows) and by the layer rows' remove button, so the whole surface looks flat.
        /// </summary>
        public static bool DrawChromeLessButton(Rect rect, GUIContent content, bool enabled = true)
        {
            bool hovered = rect.Contains(Event.current.mousePosition);
            if (enabled && hovered)
            {
                EditorGUI.DrawRect(
                    rect,
                    EditorGUIUtility.isProSkin ? new Color(1.0f, 1.0f, 1.0f, 0.10f) : new Color(0.0f, 0.0f, 0.0f, 0.07f));
            }

            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUI.LabelField(rect, content, EditorStyles.centeredGreyMiniLabel);
            }

            if (!enabled)
            {
                return false;
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                GUI.changed = true;
                return true;
            }

            return false;
        }

        /// <summary>Paging arrow without button chrome, so the row can be aligned to the sidebar width.</summary>
        private static void DrawPagingArrow(Rect rect, string glyph, bool enabled, System.Action onClick)
        {
            if (DrawChromeLessButton(rect, new GUIContent(glyph), enabled))
            {
                onClick?.Invoke();
            }
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

            float sidebarWidth = SidebarWidth;
            // The table is always columns x rows cells, whether or not the page is full: short pages
            // leave the remaining cells blank instead of shrinking the block (so the layout never
            // jumps when a search narrows the result set). Only in the split layout do icon cells
            // keep a maximum width, so three columns are not flung apart inside the fixed sidebar.
            bool folded = availableWidth < MinSplitWidth;
            float maxCellWidth = folded ? float.MaxValue : (state.IconOnly ? 40.0f : SidebarWidth);
            if (folded)
            {
                sidebarWidth = Mathf.Max(sidebarWidth, availableWidth - 40.0f);
            }

            float cellWidth = Mathf.Min(sidebarWidth / columns, maxCellWidth);
            // One row height for both styles (the icon cell), so switching the style never changes the
            // sidebar's total height - the smaller named icon is centred in the same row instead.
            float cellHeight = IconSize + IconSpacing;
            float gridWidth = cellWidth * columns;
            float gridHeight = rows * cellHeight;

            Rect grid = GUILayoutUtility.GetRect(
                gridWidth,
                gridHeight,
                GUILayout.Width(gridWidth),
                GUILayout.Height(gridHeight));
            EditorGUI.DrawRect(grid, EditorGUIUtility.isProSkin ? new Color(0.0f, 0.0f, 0.0f, 0.22f) : new Color(0.0f, 0.0f, 0.0f, 0.06f));

            if (count == 0)
            {
                // The notice fills the blank table instead of adding a row below it.
                EditorGUI.LabelField(grid, "无匹配", EditorStyles.centeredGreyMiniLabel);
            }

            for (int i = 0; i < count; i++)
            {
                EffectBrowserEntry entry = catalog.Entries[matches[start + i]];
                int column = columns == 1 ? 0 : i % columns;
                int rowIndex = columns == 1 ? i : i / columns;
                float x = grid.x + (column * cellWidth) + 2.0f;
                float y = grid.y + (rowIndex * cellHeight) + 2.0f;
                Rect cell = new Rect(x, y, cellWidth - 4.0f, cellHeight - 4.0f);

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
                    cell.y + ((cell.height - IconSize) * 0.5f),
                    IconSize,
                    IconSize);
                DrawIcon(iconRect, icon, present, entry.Tooltip);
                HandleEntryInput(catalog, state, query, entry, iconRect, present);
                return;
            }

            // Same row height as the icon-only style: the smaller icon and the label are centred in
            // the row instead of shrinking it.
            float textWidth = Mathf.Max(0.0f, cell.width - NamedIconSize - 6.0f);
            Rect namedIconRect = new Rect(cell.x, cell.y + ((cell.height - NamedIconSize) * 0.5f), NamedIconSize, NamedIconSize);
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
